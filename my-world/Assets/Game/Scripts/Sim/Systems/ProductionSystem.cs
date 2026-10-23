using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class ProductionSystem
    {
        internal static bool IsProductionStep(PlanStepType type)
        {
            switch (type)
            {
                case PlanStepType.Deliver:
                case PlanStepType.TakeFacility:
                case PlanStepType.Build:
                case PlanStepType.Craft:
                case PlanStepType.Maintain:
                case PlanStepType.Equip:
                case PlanStepType.Pickup:
                case PlanStepType.DrinkRaw:
                case PlanStepType.Treat:
                case PlanStepType.DeliverFuel:
                case PlanStepType.MeltSnow:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool Needed(WorldState world, NpcState npc)
        {
            if (world.Tick < npc.Survival.WorkRetryTick) { return false; }
            if (ThermalSystem.NeedsFuel(world)) { return true; }
            if (npc.Survival.CoatDurability == 0 && (npc.Inventory.Count(ItemKind.WinterCoat) > 0 || world.Resources.AvailableStock(ItemKind.WinterCoat) > 0)) { return true; }
            if (NeedsArrows(npc) || ArrowShortage(world) || CoatShortage(world) || CookingNeeded(world)) { return true; }
            ItemKind tool = npc.Role == "Hunter" ? (npc.Personality.Caution >= 50 ? ItemKind.Bow : ItemKind.Spear) : npc.Role == "Farmer" ? ItemKind.Hoe : ItemKind.Bucket;
            if (npc.Survival.Durability(tool) == 0 && (npc.Inventory.Count(tool) > 0 || world.Resources.AvailableStock(tool) > 0)) { return true; }
            foreach (FacilityState facility in world.Survival.Facilities)
            {
                if (!facility.ReservedBy.IsNone) { continue; }
                foreach (ProductionRecipe recipe in ProductionRecipe.All)
                {
                    if (facility.Inventory.Count(recipe.Output) > 0) { return true; }
                }
            }
            foreach (GroundStockState ground in world.Survival.Ground) { if (!ground.Empty && ground.ReservedBy.IsNone) { return true; } }
            if (npc.Role != "Builder" && npc.Role != "Trader" && npc.Role != "Gatherer")
            { return world.Resources.Stockpile.Water < Math.Max(4, world.LivingCount / 2); }
            if (world.Survival.Facilities.Count < 5 || world.Survival.RefillWater || world.Resources.Stockpile.Count(ItemKind.Medicine) < Math.Max(4, world.LivingCount / 2)) { return true; }
            foreach (var facility in world.Survival.Facilities)
            { if (!facility.Complete || facility.Condition <= 30 || facility.Inventory.Water > 0 || facility.Inventory.Count(ItemKind.Medicine) > 0) { return true; } }
            foreach (var target in world.Entities.Npcs)
            {
                if (target.Survival.Life == LifeState.Active && (target.Role == "Hunter" && !target.HasHuntingTool || target.Role == "Farmer" && target.Survival.Durability(ItemKind.Hoe) == 0 || target.Role == "Trader" && target.Survival.Durability(ItemKind.Bucket) == 0)) { return true; }
            }
            var cistern = world.Survival.Facility(FacilityKind.Cistern);
            return cistern != null && cistern.Inventory.Count(ItemKind.RawWater) < 30;
        }

        internal static bool NeedsArrows(NpcState npc) => npc.Role == "Hunter" &&
            npc.Survival.Durability(ItemKind.Bow) > 0 && npc.Inventory.Count(ItemKind.Arrow) == 0;

        internal static bool CoatShortage(WorldState world)
        {
            int needed = 0;
            long available = world.Resources.Stockpile.Count(ItemKind.WinterCoat) + world.Survival.Count(ItemKind.WinterCoat);
            foreach (NpcState target in world.Entities.Npcs)
            {
                if (target.Survival.Life == LifeState.Dead) { continue; }
                available += target.Inventory.Count(ItemKind.WinterCoat);
                if (target.Survival.CoatDurability == 0 &&
                    (target.Survival.IsCold || world.Environment.Temperature - world.Environment.Wind * 12 < 500)) { needed++; }
            }
            return needed > available;
        }

        internal static bool ArrowShortage(WorldState world)
        {
            int needed = 0;
            foreach (NpcState target in world.Entities.Npcs)
            {
                if (target.Survival.Life == LifeState.Active && NeedsArrows(target)) { needed += ProductionRecipe.For(ItemKind.Arrow).Quantity; }
            }
            return needed > world.Resources.Stockpile.Count(ItemKind.Arrow) + world.Survival.Count(ItemKind.Arrow);
        }

        internal static bool CookingNeeded(WorldState world) => world.Resources.Stockpile.Food < AgricultureSystem.FoodTarget(world) &&
            MaterialBalance.Count(world, ItemKind.RawMeat) > 0;

        internal static bool CanSupply(WorldState world, NpcState npc, int[] inputs)
        {
            for (int i = 0; i < inputs.Length; i += 2)
            {
                ItemKind item = (ItemKind)inputs[i];
                int available = npc.Inventory.Count(item) + world.Resources.AvailableStock(item);
                foreach (ResourceNodeState node in world.Resources.Nodes)
                {
                    if (node.Item == item && node.ReservedBy.IsNone && WaterSystem.SourceAvailable(world, node)) { available += node.Amount; }
                }
                if (available < inputs[i + 1]) { return false; }
            }
            return true;
        }

        internal static FacilityState CreateSite(WorldState world, FacilityKind kind, GridPathfinder pathfinder)
        {
            GridPos anchor = world.Center;
            if (kind == FacilityKind.Irrigation && world.Resources.FarmPlots.Count > 0) { anchor = world.Resources.FarmPlots[world.Resources.FarmPlots.Count / 2].Position; }
            var candidates = world.Map.CollectReachable(anchor);
            candidates.Sort((a, b) => { int order = a.ManhattanDistance(anchor).CompareTo(b.ManhattanDistance(anchor)); return order != 0 ? order : (a.Y * world.Map.Width + a.X).CompareTo(b.Y * world.Map.Width + b.X); });
            int[] dx = { 0, 1, 0, -1 }, dy = { 1, 0, -1, 0 };
            foreach (var cell in candidates)
            {
                if (cell.ManhattanDistance(anchor) < 3 || cell.ManhattanDistance(anchor) > 10 || world.Map.GetTile(cell) == TileType.Path || world.Map.GetTile(cell) == TileType.Farmland) { continue; }
                bool blocked = false;
                foreach (var node in world.Resources.Nodes) { if (cell.ManhattanDistance(node.Position) <= 1) { blocked = true; break; } }
                foreach (var npc in world.Entities.Npcs) { if (cell.ManhattanDistance(npc.Home) <= 1 || cell == npc.Position) { blocked = true; break; } }
                foreach (var facility in world.Survival.Facilities) { if (cell.ManhattanDistance(facility.Position) <= 2 || cell == facility.WorkCell) { blocked = true; break; } }
                for (int d = 0; d < 4; d++) { if (!world.Map.IsWalkable(cell.Offset(dx[d], dy[d]))) { blocked = true; } }
                if (blocked) { continue; }
                // Temporarily block the footprint and prove every resource/home remains connected.
                TileType previous = world.Map.GetTile(cell); world.Map.SetTile(cell, TileType.Rock);
                var reachable = new HashSet<GridPos>(world.Map.CollectReachable(world.Center));
                foreach (var npc in world.Entities.Npcs) { if (!reachable.Contains(npc.Home)) { blocked = true; } }
                foreach (var node in world.Resources.Nodes) { if (!reachable.Contains(node.Position)) { blocked = true; } }
                foreach (var plot in world.Resources.FarmPlots) { if (!reachable.Contains(plot.Position)) { blocked = true; } }
                if (blocked) { world.Map.SetTile(cell, previous); continue; }
                GridPos work = cell.Offset(0, -1);
                if (!reachable.Contains(work) || !world.Resources.IsWorkCellAvailable(EntityId.None, work)) { world.Map.SetTile(cell, previous); continue; }
                var site = new FacilityState(world.Survival.NextId++, kind, cell, work);
                world.Survival.MutableFacilities.Add(site); return site;
            }
            return null;
        }

        internal static void Release(WorldState world, NpcState npc)
        {
            CombatSystem.CancelUnreleased(world, npc.Id);
            world.Survival.Release(npc.Id);
            npc.Survival.HuntDropId = 0;
            foreach (var target in world.Entities.Npcs)
            { if (target.Survival.Rescuer == npc.Id) { target.Survival.Rescuer = EntityId.None; } }
        }

        internal static bool Check(WorldState world, NpcState npc, PlanStep step)
        {
            if (step.Type == PlanStepType.DeliverFuel || step.Type == PlanStepType.MeltSnow)
            {
                ShelterState shelter = world.Survival.FindShelter(step.ResourceId);
                if (shelter == null || npc.Position != shelter.Entrance) { return false; }
                bool reserved = false;
                foreach (ResourceReservation reservation in world.Resources.Reservations)
                {
                    if (reservation.Owner == npc.Id && reservation.Kind == ReservationKind.WorkCell &&
                        reservation.ResourceId == shelter.Id && reservation.Position == shelter.Entrance) { reserved = true; break; }
                }
                if (!reserved || step.Quantity <= 0) { return false; }
                return step.Type == PlanStepType.DeliverFuel
                    ? npc.Inventory.Count(ItemKind.Wood) >= step.Quantity && step.Quantity <= shelter.Inventory.Capacity - shelter.Inventory.TotalCount
                    : world.Environment.SnowDepth >= step.Quantity && shelter.FuelTicks > 0 &&
                      step.Quantity <= shelter.HeatBudget / SurvivalRules.SnowMeltHeatPerWater &&
                      step.Quantity <= npc.Inventory.Capacity - npc.Inventory.TotalCount;
            }
            if (step.Type == PlanStepType.Treat) { return npc.Inventory.Count(ItemKind.Medicine) > 0; }
            if (step.Type == PlanStepType.Equip)
            {
                return npc.Inventory.Count(step.Item) > 0 && (step.Item == ItemKind.WinterCoat
                    ? npc.Survival.CoatDurability == 0
                    : SurvivalRules.IsTool(step.Item) && npc.Survival.Durability(step.Item) == 0);
            }
            if (step.Type == PlanStepType.DrinkRaw) { return npc.Inventory.Count(ItemKind.RawWater) > 0; }
            if (step.Type == PlanStepType.Pickup) { var g = world.Survival.FindGround(step.ResourceId); return g != null && g.ReservedBy == npc.Id && !g.Empty; }
            var f = world.Survival.FindFacility(step.ResourceId);
            if (f == null || f.WorkCell != npc.Position || f.ReservedBy != npc.Id) { return false; }
            if (step.Type == PlanStepType.Deliver) { return step.Quantity > 0 && npc.Inventory.Count(step.Item) >= step.Quantity && step.Quantity <= f.Inventory.Capacity - f.Inventory.TotalCount; }
            if (step.Type == PlanStepType.TakeFacility) { return step.Quantity > 0 && f.Inventory.Count(step.Item) >= step.Quantity && step.Quantity <= npc.Inventory.Capacity - npc.Inventory.TotalCount; }
            if (step.Type == PlanStepType.Build) { return !f.Complete && HasInputs(f.Inventory, ProductionRecipe.ConstructionCost(f.Kind)); }
            if (step.Type == PlanStepType.Maintain) { return f.Complete && f.Inventory.Count(ItemKind.Wood) >= 1; }
            if (step.Type != PlanStepType.Craft) { return false; }
            var recipe = ProductionRecipe.For(step.Item);
            return recipe != null && f.Complete && f.Condition > 0 && f.Kind == recipe.Facility && HasInputs(f.Inventory, recipe.Inputs) && f.Inventory.TotalCount + recipe.Quantity - InputCount(recipe.Inputs) <= f.Inventory.Capacity;
        }
        internal static bool HasInputs(InventoryState inventory, int[] inputs)
        { for (int i = 0; i < inputs.Length; i += 2) { if (inventory.Count((ItemKind)inputs[i]) < inputs[i + 1]) { return false; } } return true; }
        private static int InputCount(int[] inputs) { int total = 0; for (int i = 1; i < inputs.Length; i += 2) { total += inputs[i]; } return total; }
        internal static void Consume(WorldState world, InventoryState inventory, int[] inputs)
        { for (int i = 0; i < inputs.Length; i += 2) { inventory.TryRemove((ItemKind)inputs[i], inputs[i + 1]); world.Materials.Consume((ItemKind)inputs[i], inputs[i + 1]); } }
        internal static void Wear(WorldState world, NpcState npc, ItemKind tool)
        {
            int durability = npc.Survival.Durability(tool);
            if (durability <= 0) { return; }
            npc.Survival.SetTool(tool, durability - 1);
            if (durability == 1) { world.Materials.Consume(tool, 1); }
        }
        internal static bool Apply(WorldState world, NpcState npc, PlanStep step)
        {
            if (!Check(world, npc, step)) { return false; }
            if (step.Type == PlanStepType.DeliverFuel)
            {
                ShelterState shelter = world.Survival.FindShelter(step.ResourceId);
                npc.Inventory.TryRemove(ItemKind.Wood, step.Quantity);
                shelter.Inventory.TryAdd(ItemKind.Wood, step.Quantity);
                return true;
            }
            if (step.Type == PlanStepType.MeltSnow)
            {
                ShelterState shelter = world.Survival.FindShelter(step.ResourceId);
                world.Environment.SnowDepth -= step.Quantity;
                // Debit the same finite budget that ThermalSystem spends on the room.
                // Unburnt shelter stock and facility fuel cannot supply this heat.
                shelter.HeatBudget -= step.Quantity * SurvivalRules.SnowMeltHeatPerWater;
                npc.Inventory.TryAdd(ItemKind.RawWater, step.Quantity);
                world.Materials.Produce(ItemKind.RawWater, step.Quantity);
                return true;
            }
            if (step.Type == PlanStepType.Treat) { npc.Inventory.TryRemove(ItemKind.Medicine, 1); world.Materials.Consume(ItemKind.Medicine, 1); npc.Survival.DiseaseTicks = 0; return true; }
            if (step.Type == PlanStepType.DrinkRaw)
            {
                npc.Inventory.TryRemove(ItemKind.RawWater, 1); world.Materials.Consume(ItemKind.RawWater, 1);
                npc.Needs.Thirst = Math.Min(10000, npc.Needs.Thirst + 4000);
                var rng = new DeterministicRng(npc.RngState);
                if (rng.Chance(SurvivalRules.DiseaseRisk)) { npc.Survival.DiseaseTicks = SurvivalRules.DiseaseDuration; }
                npc.RngState = rng.State; return true;
            }
            if (step.Type == PlanStepType.Equip)
            {
                npc.Inventory.TryRemove(step.Item, 1);
                if (step.Item == ItemKind.WinterCoat) { npc.Survival.CoatDurability = SurvivalRules.ToolDurability; }
                else { npc.Survival.SetTool(step.Item, SurvivalRules.ToolDurability); }
                return true;
            }
            if (step.Type == PlanStepType.Pickup)
            {
                var ground = world.Survival.FindGround(step.ResourceId);
                for (int i = 0; i < (int)ItemKind.Count; i++)
                {
                    ItemKind item = (ItemKind)i;
                    int amount = Math.Min(ground.Inventory.Count(item), npc.Inventory.Capacity - npc.Inventory.TotalCount);
                    if (amount > 0) { ground.Inventory.TryRemove(item, amount); npc.Inventory.TryAdd(item, amount); }
                }
                for (int i = 0; i < 4; i++)
                { ItemKind tool = (ItemKind)((int)ItemKind.Bow + i); if (ground.Tools[i] > 0 && npc.Survival.Durability(tool) == 0) { npc.Survival.SetTool(tool, ground.Tools[i]); ground.Tools[i] = 0; } }
                if (ground.Coins <= long.MaxValue - npc.Coins) { npc.Coins += ground.Coins; ground.Coins = 0; }
                ground.ReservedBy = EntityId.None; return true;
            }
            var f = world.Survival.FindFacility(step.ResourceId);
            if (step.Type == PlanStepType.Deliver) { npc.Inventory.TryRemove(step.Item, step.Quantity); f.Inventory.TryAdd(step.Item, step.Quantity); }
            else if (step.Type == PlanStepType.TakeFacility) { f.Inventory.TryRemove(step.Item, step.Quantity); npc.Inventory.TryAdd(step.Item, step.Quantity); }
            else if (step.Type == PlanStepType.Build)
            {
                Consume(world, f.Inventory, ProductionRecipe.ConstructionCost(f.Kind)); f.Progress = SurvivalRules.ConstructionTicks; npc.Survival.Built++;
                if (f.Kind == FacilityKind.Well)
                {
                    int id = 1; foreach (var node in world.Resources.Nodes) { id = Math.Max(id, node.Id + 1); }
                    var well = new ResourceNodeState(id, f.WorkCell, ItemKind.RawWater, 40, 20) { Amount = 0 };
                    world.Resources.AddNode(well); f.WellNodeId = id;
                }
            }
            else if (step.Type == PlanStepType.Maintain) { Consume(world, f.Inventory, new[] { (int)ItemKind.Wood, 1 }); f.Condition = 100; }
            else
            {
                var recipe = ProductionRecipe.For(step.Item); Consume(world, f.Inventory, recipe.Inputs);
                f.Inventory.TryAdd(recipe.Output, recipe.Quantity); world.Materials.Produce(recipe.Output, recipe.Quantity);
                npc.Survival.Crafted++; if (step.Item == ItemKind.Water) { npc.Survival.WaterProduced += recipe.Quantity; }
            }
            return true;
        }
    }
}
