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
            npc.Needs.Food < 1500 || npc.Needs.Water < 1800 || ThermalRisk(world, npc);

        internal static bool ThermalRisk(WorldState world, NpcState npc)
        {
            if (npc.Survival.BodyTemperature < 3600 || npc.Survival.Wetness >= 5000) { return true; }
            if (npc.Survival.CoatDurability == 0) { return ThermalSystem.ShouldSeekWarmth(world, npc); }
            PathResult path = Pathfinder.FindPath(world.Map, npc.Position, npc.Home);
            return !path.Found || !ThermalSystem.CanHuntTrip(world, npc, path.Cost);
        }

        internal static string Reason(WorldState world, NpcState npc)
        {
            if (npc.Needs.Health < HuntingRules.RetreatHealth) { return npc.Name + "受伤，生命不足"; }
            if (npc.Needs.Energy < HuntingRules.RetreatEnergy) { return npc.Name + "长期精力不足，需要睡眠"; }
            if (npc.Needs.Food < 1500) { return npc.Name + "食物耗尽，无法继续追猎"; }
            if (npc.Needs.Water < 1800) { return npc.Name + "饮水耗尽，无法继续追猎"; }
            return npc.Name + "受寒风险，需要返村取暖";
        }

        internal static bool VisibleDanger(WorldState world, NpcState npc)
        {
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Region == -1 && animal.Health > 0 && animal.Position.ManhattanDistance(npc.Position) <= 2 &&
                    HuntingSystem.Perceived(world, npc, animal) && (animal.Encounter.Phase == EncounterPhase.Defending || animal.LastDamageSourceId == npc.Id)) { return true; }
            }
            return false;
        }

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
                if (task.Stage != HuntStage.Gathering && !VisibleDanger(world, npc)) { HuntingPreparation.EatDrink(world, npc); }
                if (task.Stage != HuntStage.Gathering && NeedsRetreat(world, npc) || npc.Needs.Health < HuntingRules.RetreatHealth || npc.Survival.IsCold)
                {
                    return Reason(world, npc);
                }
                if (!Pathfinder.FindPath(world.Map, npc.Position, npc.Home).Found) { return "队员退路不可达"; }
                bool arrowInFlight = false;
                foreach (ProjectileState arrow in world.Combat.Projectiles) { if (arrow.Shooter == npc.Id && arrow.Phase == ProjectilePhase.Flying) { arrowInFlight = true; break; } }
                if ((task.Stage == HuntStage.Approaching || task.Stage == HuntStage.Engaging) &&
                    !HuntingEquipment.Ready(npc, member) && !(member.Role != HuntRole.Spear && npc.Survival.Durability(ItemKind.Bow) > 0 && arrowInFlight))
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
                    npc.Survival.Stamina = Math.Max(0, npc.Survival.Stamina - 180);
                    npc.Action.Label = "Hunt: dodge";
                    npc.Action.Target = side;
                    return true;
                }
            }
            return false;
        }
    }
}
