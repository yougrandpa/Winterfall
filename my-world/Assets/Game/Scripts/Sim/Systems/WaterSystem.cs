using System;
using MyAiWorld.Sim.World;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Navigation;

namespace MyAiWorld.Sim.Systems
{
    internal static class WaterSystem
    {
        internal static int CollectionAmount(WorldState world, NpcState npc)
        {
            int dry = 0; foreach (var plot in world.Resources.FarmPlots) { if (plot.Crop != CropType.None && plot.Moisture < 180) { dry++; } }
            int missing = Math.Max(1, Math.Max(12, world.LivingCount * 2) + dry - world.Resources.Stockpile.Count(ItemKind.RawWater));
            int cost = int.MaxValue;
            foreach (var node in world.Resources.Nodes)
            {
                if (node.Item != ItemKind.RawWater || node.Amount <= 0 || !node.ReservedBy.IsNone || !SourceAvailable(world, node)) { continue; }
                var path = new GridPathfinder().FindPath(world.Map, npc.Position, node.Position);
                if (path.Found) { cost = Math.Min(cost, path.Cost); }
            }
            if (cost == int.MaxValue)
            {
                return 0;
            }
            int capacity = npc.Survival.Durability(ItemKind.Bucket) > 0 ? 10 : 5;
            int batch = Math.Min(capacity, 3 + cost / 4);
            return Math.Min(missing, Math.Min(batch, npc.Inventory.Capacity - npc.Inventory.TotalCount));
        }
        internal static bool SourceAvailable(WorldState world, ResourceNodeState node)
        {
            if (node == null)
            {
                return false;
            }
            if (node.Item != ItemKind.RawWater)
            {
                return true;
            }
            if (world.Environment.WaterFrozen)
            {
                return false;
            }
            foreach (var facility in world.Survival.Facilities)
            {
                if (facility.WellNodeId == node.Id)
                {
                    return facility.Complete && facility.Condition > 0;
                }
            }
            return true;
        }
        internal static void Update(WorldState world)
        {
            int target = Math.Max(20, world.LivingCount * 5);
            int available = world.Resources.Stockpile.Water;
            foreach (var f in world.Survival.Facilities) { available += f.Inventory.Water; }
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                available += shelter.Inventory.Water;
            }
            if (available < target / 2) { world.Survival.RefillWater = true; }
            if (available >= target) { world.Survival.RefillWater = false; }
            if (world.Tick % SurvivalRules.MaintenanceInterval == 0)
            { foreach (var f in world.Survival.Facilities) { if (f.Complete) { f.Condition = Math.Max(0, f.Condition - 20); } } }
            var cistern = world.Survival.Facility(FacilityKind.Cistern);
            var irrigation = world.Survival.Facility(FacilityKind.Irrigation);
            if (world.Environment.WaterFrozen || world.Tick % 30 != 0 || cistern == null || irrigation == null || !cistern.ReservedBy.IsNone) { return; }
            FarmPlotState targetPlot = null;
            foreach (var plot in world.Resources.FarmPlots)
            {
                if (plot.Crop == CropType.None || plot.Stage == CropStage.Mature || plot.Moisture >= 180 || !plot.ReservedBy.IsNone || plot.Position.ManhattanDistance(irrigation.Position) > 7) { continue; }
                if (targetPlot == null || plot.Moisture < targetPlot.Moisture) { targetPlot = plot; }
            }
            if (targetPlot != null && cistern.Inventory.TryRemove(ItemKind.RawWater, 1))
            { world.Materials.Consume(ItemKind.RawWater, 1); targetPlot.Moisture = Math.Min(1800, targetPlot.Moisture + 900); }
        }
    }
}
