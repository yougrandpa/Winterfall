using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Ai
{
    internal sealed partial class SurvivalPlanner
    {
        private bool Acquire(WorldState world, NpcState npc, ItemKind item, int quantity, List<PlanStep> steps)
        {
            int carried = npc.Inventory.Count(item);
            if (carried >= quantity) { return true; }
            int needed = quantity - carried;
            int free = npc.Inventory.Capacity - npc.Inventory.TotalCount;
            if (free < needed) { return false; }
            GridPos stock = world.Resources.StockpilePosition;
            if (world.Resources.AvailableStock(item) >= needed && CanReach(world, npc.Position, stock) &&
                world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Stockpile, 0, item, needed, stock)))
            {
                Move(steps, stock); steps.Add(new PlanStep(PlanStepType.TakeFromStock, stock, item: item, quantity: needed)); return true;
            }
            return Gather(world, npc, item, steps, needed) && steps[steps.Count - 1].Quantity == needed;
        }

        private bool BuildRescue(WorldState world, NpcState npc, List<PlanStep> steps)
        {
            var targets = new List<NpcState>();
            foreach (NpcState target in world.Entities.Npcs)
            {
                if (target.Id != npc.Id && HealthSystem.NeedsRescue(target)) { targets.Add(target); }
            }
            targets.Sort((a, b) => { int order = a.Survival.RescueDeadline.CompareTo(b.Survival.RescueDeadline); return order != 0 ? order : a.Id.Value.CompareTo(b.Id.Value); });
            foreach (var target in targets)
            {
                if (!CanReach(world, npc.Position, target.Position) || !CanReach(world, target.Position, target.Home)) { continue; }
                if (target.Survival.Life == LifeState.Downed && !EnsureItem(world, npc, ItemKind.Medicine, steps)) { return false; }
                if (target.Survival.Life == LifeState.Recovering && target.Position == target.Home)
                {
                    ItemKind item = target.Needs.Water < 3500 && target.Inventory.Water == 0 ? ItemKind.Water : ItemKind.Food;
                    if (!EnsureItem(world, npc, item, steps)) { return false; }
                }
                target.Survival.Rescuer = npc.Id;
                steps.Add(new PlanStep(PlanStepType.Rescue, target.Position, target.Id.Value, durationTicks: SurvivalRules.RescueWindow)); return true;
            }
            return false;
        }

        private bool BuildProduction(WorldState world, NpcState npc, List<PlanStep> steps)
        {
            ItemKind preferred = npc.Role == "Hunter" ? (npc.Personality.Caution >= 50 ? ItemKind.Bow : ItemKind.Spear) :
                npc.Role == "Farmer" ? ItemKind.Hoe : ItemKind.Bucket;
            if (npc.Survival.Durability(preferred) == 0 && (npc.Inventory.Count(preferred) > 0 || world.Resources.AvailableStock(preferred) > 0) &&
                Acquire(world, npc, preferred, 1, steps))
            {
                steps.Add(new PlanStep(PlanStepType.Equip, steps.Count == 0 ? npc.Position : steps[steps.Count - 1].Target, item: preferred)); return true;
            }
            // Deliver finished batches first, keeping finite facility buffers available.
            foreach (var facility in world.Survival.Facilities)
            {
                if (!facility.ReservedBy.IsNone || !CanReach(world, npc.Position, facility.WorkCell)) { continue; }
                for (int i = 0; i < (int)ItemKind.Count; i++)
                {
                    ItemKind item = (ItemKind)i;
                    if (item == ItemKind.RawWater || item == ItemKind.Wood || item == ItemKind.Stone || item == ItemKind.Fiber || item == ItemKind.Herb) { continue; }
                    int amount = Math.Min(facility.Inventory.Count(item), npc.Inventory.Capacity - npc.Inventory.TotalCount);
                    if (amount <= 0 || world.Resources.Stockpile.TotalCount + amount > world.Resources.Stockpile.Capacity) { continue; }
                    if (!ClaimFacility(world, npc, facility)) { continue; }
                    Move(steps, facility.WorkCell); steps.Add(new PlanStep(PlanStepType.TakeFacility, facility.WorkCell, facility.Id, item, amount));
                    AddDeposit(world, steps, item, amount); return true;
                }
            }
            if (world.Survival.RefillWater && world.Survival.Facility(FacilityKind.Purifier) != null &&
                Recipe(world, npc, ProductionRecipe.For(ItemKind.Water), steps)) { return true; }
            if (world.Resources.Stockpile.Count(ItemKind.Medicine) < Math.Max(4, world.LivingCount / 2) &&
                Recipe(world, npc, ProductionRecipe.For(ItemKind.Medicine), steps)) { return true; }
            foreach (var target in world.Entities.Npcs)
            {
                if (target.Survival.Life != LifeState.Active) { continue; }
                ItemKind tool = target.Role == "Hunter" ? (target.Personality.Caution >= 50 ? ItemKind.Bow : ItemKind.Spear) : target.Role == "Farmer" ? ItemKind.Hoe : ItemKind.Bucket;
                if (target.Survival.Durability(tool) < 15 && world.Resources.Stockpile.Count(tool) < 2 && Recipe(world, npc, ProductionRecipe.For(tool), steps)) { return true; }
            }
            FacilityKind[] order = { FacilityKind.Purifier, FacilityKind.Workbench, FacilityKind.Cistern, FacilityKind.Well, FacilityKind.Irrigation };
            foreach (FacilityKind kind in order)
            {
                FacilityState facility = null;
                foreach (var existing in world.Survival.Facilities) { if (existing.Kind == kind) { facility = existing; break; } }
                if (facility == null) { facility = ProductionSystem.CreateSite(world, kind, _pathfinder); }
                if (facility == null || !facility.ReservedBy.IsNone || !CanReach(world, npc.Position, facility.WorkCell)) { continue; }
                if (!facility.Complete && StageOrWork(world, npc, facility, ProductionRecipe.ConstructionCost(kind), PlanStepType.Build, ItemKind.Food, steps)) { return true; }
                if (facility.Complete && facility.Condition <= 30 && StageOrWork(world, npc, facility, new[] { (int)ItemKind.Wood, 1 }, PlanStepType.Maintain, ItemKind.Wood, steps)) { return true; }
            }
            var cistern = world.Survival.Facility(FacilityKind.Cistern);
            if (cistern != null && cistern.ReservedBy.IsNone && cistern.Inventory.Count(ItemKind.RawWater) < 30 && world.Resources.Stockpile.Water >= Math.Max(8, world.LivingCount))
            {
                if (StageOrWork(world, npc, cistern, new[] { (int)ItemKind.RawWater, 36 }, PlanStepType.Wait, ItemKind.RawWater, steps)) { return true; }
            }
            foreach (GroundStockState ground in world.Survival.Ground)
            {
                if (ground.Empty || !ground.ReservedBy.IsNone || !CanReach(world, npc.Position, ground.Position)) { continue; }
                bool useful = ground.Coins > 0 || (ground.Inventory.TotalCount > 0 && npc.Inventory.TotalCount < npc.Inventory.Capacity);
                for (int i = 0; i < 4; i++) { if (ground.Tools[i] > 0 && npc.Survival.Tools[i] == 0) { useful = true; } }
                if (!useful) { continue; }
                ground.ReservedBy = npc.Id; Move(steps, ground.Position); steps.Add(new PlanStep(PlanStepType.Pickup, ground.Position, ground.Id));
                // Pickup quantity is dynamic; the next plan deposits or uses the recovered goods.
                return true;
            }
            return false;
        }

        private bool Recipe(WorldState world, NpcState npc, ProductionRecipe recipe, List<PlanStep> steps)
        {
            var facility = world.Survival.Facility(recipe.Facility);
            return facility != null && facility.ReservedBy.IsNone && CanReach(world, npc.Position, facility.WorkCell) &&
                StageOrWork(world, npc, facility, recipe.Inputs, PlanStepType.Craft, recipe.Output, steps);
        }

        private bool StageOrWork(WorldState world, NpcState npc, FacilityState facility, int[] inputs, PlanStepType work, ItemKind output, List<PlanStep> steps)
        {
            for (int i = 0; i < inputs.Length; i += 2)
            {
                ItemKind item = (ItemKind)inputs[i]; int missing = inputs[i + 1] - facility.Inventory.Count(item);
                if (missing <= 0) { continue; }
                int amount = Math.Min(missing, npc.Survival.Durability(ItemKind.Bucket) > 0 ? 10 : 6);
                amount = Math.Min(amount, npc.Inventory.Capacity - npc.Inventory.TotalCount + npc.Inventory.Count(item));
                if (amount <= 0 || !Acquire(world, npc, item, amount, steps))
                { world.Resources.Release(npc.Id); steps.Clear(); return false; }
                if (!ClaimFacility(world, npc, facility)) { world.Resources.Release(npc.Id); steps.Clear(); return false; }
                Move(steps, facility.WorkCell); steps.Add(new PlanStep(PlanStepType.Deliver, facility.WorkCell, facility.Id, item, amount, 2)); return true;
            }
            if (work == PlanStepType.Wait || !ClaimFacility(world, npc, facility)) { return false; }
            Move(steps, facility.WorkCell);
            steps.Add(new PlanStep(work, facility.WorkCell, facility.Id, output, durationTicks: work == PlanStepType.Build ? SurvivalRules.ConstructionTicks - facility.Progress : work == PlanStepType.Craft ? ProductionRecipe.For(output).Ticks : 8));
            return true;
        }

        private static bool ClaimFacility(WorldState world, NpcState npc, FacilityState facility)
        {
            if (!facility.ReservedBy.IsNone || !world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.WorkCell, facility.Id, ItemKind.Food, 0, facility.WorkCell))) { return false; }
            facility.ReservedBy = npc.Id; return true;
        }
    }
}
