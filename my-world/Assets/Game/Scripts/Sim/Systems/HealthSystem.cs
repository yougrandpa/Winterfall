using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HealthSystem
    {
        internal static bool NeedsRescue(NpcState target) => target.Survival.Rescuer.IsNone &&
            (target.Survival.Life == LifeState.Downed || target.Survival.Life == LifeState.Recovering &&
            (target.Position != target.Home || target.Needs.Food < 3500 && target.Inventory.Food == 0 || target.Needs.Water < 3500 && target.Inventory.Water == 0));
        internal static void Cancel(WorldState world, NpcState npc, string reason)
        {
            world.Resources.Release(npc.Id); world.Ecology.Release(npc.Id); ProductionSystem.Release(world, npc);
            if (npc.Plan.IsActive) { npc.Plan.Fail(reason); }
            npc.Action.SetIdle(reason, world.Tick + 1);
        }
        internal static bool Update(WorldState world, NpcState npc, List<SimEvent> events)
        {
            var state = npc.Survival;
            if (state.Life == LifeState.Dead) { return false; }
            if (state.Life == LifeState.Downed)
            {
                if (world.Tick >= state.RescueDeadline)
                {
                    Cancel(world, npc, "rescue_expired"); state.Life = LifeState.Dead; state.Rescuer = EntityId.None;
                    var ground = world.Survival.Drop(npc.Position); ground.Coins = npc.Coins; npc.Coins = 0;
                    for (int i = 0; i < (int)ItemKind.Count; i++)
                    { ItemKind item = (ItemKind)i; int amount = npc.Inventory.Count(item); if (amount > 0) { npc.Inventory.TryRemove(item, amount); ground.Inventory.TryAdd(item, amount); } }
                    for (int i = 0; i < 4; i++) { ground.Tools[i] = state.Tools[i]; state.Tools[i] = 0; }
                    events.Add(new SimEvent(SimEventKind.NpcDied, world.Tick, npc.Id, npc.Name + " died; belongings remain at " + npc.Position + "."));
                }
                return false;
            }
            if (npc.Needs.Health <= 0)
            {
                Cancel(world, npc, "downed"); state.Life = LifeState.Downed; state.RescueDeadline = world.Tick + SurvivalRules.RescueWindow;
                state.Rescuer = EntityId.None;
                events.Add(new SimEvent(SimEventKind.NpcDowned, world.Tick, npc.Id, npc.Name + " needs rescue before " + state.RescueDeadline + ".")); return false;
            }
            if (state.DiseaseTicks > 0)
            {
                state.DiseaseTicks--; npc.Needs.Health = Math.Max(0, npc.Needs.Health - 5); npc.Needs.Thirst = Math.Max(0, npc.Needs.Thirst - 4);
            }
            if (state.Life == LifeState.Recovering)
            {
                if (npc.Position != npc.Home && npc.Survival.Rescuer.IsNone && !npc.Plan.IsActive) { npc.Action.SetIdle("Awaiting transport", world.Tick + 1); return false; }
                if (npc.Position == npc.Home)
                {
                    if (npc.Needs.Food < 5000 && npc.Inventory.TryRemove(ItemKind.Food, 1)) { world.Materials.Consume(ItemKind.Food, 1); npc.Needs.Hunger = Math.Min(10000, npc.Needs.Hunger + 4500); }
                    if (npc.Needs.Water < 5000 && npc.Inventory.TryRemove(ItemKind.Water, 1)) { world.Materials.Consume(ItemKind.Water, 1); npc.Needs.Thirst = Math.Min(10000, npc.Needs.Thirst + 5500); }
                    if (npc.Needs.Food >= 3000 && npc.Needs.Water >= 3000) { npc.Needs.Health = Math.Min(10000, npc.Needs.Health + 12); }
                    if (npc.Needs.Health >= 4000) { state.Life = LifeState.Active; }
                    else { npc.Action.SetTimed(NpcActionType.Sleep, "Recovering", npc.Home, world.Tick + 1); return false; }
                }
            }
            return true;
        }

        internal static bool Rescue(WorldState world, NpcState npc, PlanStep step, GridPathfinder pathfinder, List<SimEvent> events, out bool complete)
        {
            complete = false;
            if (!world.Entities.TryGetNpc(new EntityId(step.ResourceId), out NpcState target) || target.Survival.Rescuer != npc.Id || target.Survival.Life == LifeState.Dead) { return false; }
            if (!npc.Plan.StepStarted)
            { npc.Plan.StepStarted = true; npc.Action.SetTimed(NpcActionType.Work, "Rescue", target.Position, world.Tick + SurvivalRules.RescueWindow); }
            if (world.Tick >= npc.Action.EndTick) { return false; }
            if (npc.Position.ManhattanDistance(target.Position) > 1)
            {
                var approach = pathfinder.FindPath(world.Map, npc.Position, target.Position);
                if (!approach.Found || approach.Path.Count == 0) { return false; }
                npc.Position = approach.Path[0]; return true;
            }
            if (target.Survival.Life == LifeState.Downed)
            {
                if (!npc.Inventory.TryRemove(ItemKind.Medicine, 1)) { return false; }
                world.Materials.Consume(ItemKind.Medicine, 1); target.Survival.Life = LifeState.Recovering;
                target.Needs.Health = 1500; target.Survival.DiseaseTicks = 0;
                // Transfer carried provisions; no free replenishment on treatment.
                foreach (ItemKind item in new[] { ItemKind.Food, ItemKind.Water })
                { if (target.Inventory.Count(item) == 0 && target.Inventory.TotalCount < target.Inventory.Capacity && npc.Inventory.TryRemove(item, 1)) { target.Inventory.TryAdd(item, 1); } }
            }
            if (target.Position != target.Home)
            {
                var path = pathfinder.FindPath(world.Map, target.Position, target.Home);
                if (!path.Found || path.Path.Count == 0) { return false; }
                target.Position = path.Path[0]; npc.Position = target.Position; return true;
            }
            foreach (ItemKind item in new[] { ItemKind.Food, ItemKind.Water })
            {
                if (target.Inventory.Count(item) > 0 || npc.Inventory.Count(item) == 0) { continue; }
                if (target.Inventory.TotalCount < target.Inventory.Capacity) { npc.Inventory.TryRemove(item, 1); target.Inventory.TryAdd(item, 1); }
                else if ((item == ItemKind.Food ? target.Needs.Food : target.Needs.Water) < 5000)
                {
                    // Bedside feeding works even when the patient's bag is full of unrelated supplies.
                    npc.Inventory.TryRemove(item, 1); world.Materials.Consume(item, 1);
                    if (item == ItemKind.Food) { target.Needs.Hunger = Math.Min(10000, target.Needs.Hunger + 4500); }
                    else { target.Needs.Thirst = Math.Min(10000, target.Needs.Thirst + 5500); }
                }
            }
            target.Survival.Rescuer = EntityId.None; npc.Survival.Rescues++; complete = true;
            events.Add(new SimEvent(SimEventKind.NpcRescued, world.Tick, npc.Id, npc.Name + " rescued " + target.Name + ".")); return true;
        }
    }
}
