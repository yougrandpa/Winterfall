using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingSafety
    {
        private static readonly GridPathfinder Pathfinder = new GridPathfinder();

        internal static bool NeedsRetreat(WorldState world, NpcState npc) =>
            npc.Needs.Health < HuntingRules.RetreatHealth || npc.Needs.Energy < HuntingRules.RetreatEnergy ||
            npc.Needs.Food < 1500 || npc.Needs.Water < 1800 || ThermalSystem.ShouldSeekWarmth(world, npc);

        internal static bool CanCover(WorldState world, NpcState npc) =>
            npc.Survival.Life == LifeState.Active && npc.Needs.Health >= HuntingRules.JoinHealth &&
            npc.Needs.Energy >= HuntingRules.JoinEnergy && !NeedsRetreat(world, npc);

        internal static int VisibleWolves(WorldState world, IReadOnlyList<NpcState> members, GridPos center)
        {
            int count = 0;
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Species != AnimalSpecies.Wolf || animal.Region != -1 || animal.Health <= 0) { continue; }
                foreach (NpcState member in members)
                {
                    if ((animal.Position.ManhattanDistance(center) <= HuntingRules.PackRadius ||
                        animal.Position.ManhattanDistance(member.Position) <= HuntingRules.PackRadius) &&
                        HuntingSystem.Perceived(world, member, animal))
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        internal static string Check(WorldState world, HuntTaskState task)
        {
            var members = new List<NpcState>();
            foreach (HuntMemberState member in task.Members)
            {
                if (!world.Entities.TryGetNpc(member.NpcId, out NpcState npc) || npc.Survival.Life != LifeState.Active)
                {
                    return "队员倒地或离队";
                }
                if (NeedsRetreat(world, npc))
                {
                    return "队员受伤、疲惫或需求告急";
                }
                if (!Pathfinder.FindPath(world.Map, npc.Position, npc.Home).Found) { return "队员退路不可达"; }
                if ((task.Stage == HuntStage.Approaching || task.Stage == HuntStage.Engaging) &&
                    !HuntingEquipment.Ready(npc, member))
                {
                    return "队伍武器损坏或箭矢耗尽，停止追猎";
                }
                members.Add(npc);
            }
            return VisibleWolves(world, members, task.LastSeenPosition) > members.Count ? "附近狼群超过队伍承受能力" : null;
        }

        internal static bool Occupied(WorldState world, NpcState mover, GridPos cell)
        {
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Id != mover.Id && npc.Survival.Life != LifeState.Dead && npc.Position == cell) { return true; }
            }
            return false;
        }

        internal static bool ChargeLane(WorldState world, NpcState npc, GridPos cell)
        {
            foreach (CombatAttackState attack in world.Combat.Attacks)
            {
                if (attack.Kind != CombatAttackKind.Charge || attack.Phase != CombatAttackPhase.Preparing) { continue; }
                AnimalState boar = world.Ecology.Find(attack.Attacker.Value);
                if (boar != null && HuntingSystem.Perceived(world, npc, boar) &&
                    AnimalEncounterSystem.OnChargeLane(boar.Position, cell, attack.FacingX, attack.FacingY)) { return true; }
            }
            return false;
        }

        internal static bool Dodge(WorldState world, NpcState npc)
        {
            foreach (CombatAttackState attack in world.Combat.Attacks)
            {
                if (attack.Kind != CombatAttackKind.Charge || attack.Phase != CombatAttackPhase.Preparing) { continue; }
                AnimalState boar = world.Ecology.Find(attack.Attacker.Value);
                if (boar == null || !HuntingSystem.Perceived(world, npc, boar) ||
                    !AnimalEncounterSystem.OnChargeLane(boar.Position, npc.Position, attack.FacingX, attack.FacingY)) { continue; }
                int sign = (npc.Id.Value & 1) == 0 ? 1 : -1;
                for (int i = 0; i < 2; i++)
                {
                    GridPos side = npc.Position.Offset(attack.FacingY == 0 ? 0 : sign, attack.FacingX == 0 ? 0 : sign);
                    sign = -sign;
                    if (!world.Map.IsWalkable(side) || Occupied(world, npc, side) || ChargeLane(world, npc, side) ||
                        !Pathfinder.FindPath(world.Map, side, npc.Home).Found) { continue; }
                    CombatSystem.CancelUnreleased(world, npc.Id);
                    npc.Position = side;
                    npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - 4);
                    npc.Action.Label = "Hunt: dodge";
                    npc.Action.Target = side;
                    return true;
                }
            }
            return false;
        }
    }
}
