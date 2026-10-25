using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class GrowingBedSystem
    {
        public const int TargetTemperature = 1000;
        public const int FuelDuration = 720;
        public const int HeatPerTick = 20;
        public const int SnowWorkThreshold = 600;

        internal static void Initialize(WorldState world)
        {
            var remaining = new List<FarmPlotState>(world.Resources.FarmPlots);
            while (remaining.Count > 0)
            {
                var cluster = new List<FarmPlotState> { remaining[0] };
                remaining.RemoveAt(0);
                for (int i = 0; i < cluster.Count; i++)
                {
                    for (int j = remaining.Count - 1; j >= 0; j--)
                    {
                        if (cluster[i].Position.ManhattanDistance(remaining[j].Position) != 1) { continue; }
                        cluster.Add(remaining[j]); remaining.RemoveAt(j);
                    }
                }
                cluster.Sort((a, b) => { int d = a.Position.ManhattanDistance(world.Center).CompareTo(b.Position.ManhattanDistance(world.Center)); return d != 0 ? d : a.Id.CompareTo(b.Id); });
                var selected = new List<FarmPlotState> { cluster[0] };
                for (int i = 0; i < selected.Count && selected.Count < 4; i++)
                {
                    foreach (var p in cluster)
                    {
                        if (selected.Count == 4) { break; }
                        if (!selected.Contains(p) && selected[i].Position.ManhattanDistance(p.Position) == 1) { selected.Add(p); }
                    }
                }
                var bed = new FacilityState(world.Survival.NextId++, FacilityKind.GrowingBed, selected[0].Position, selected[0].Position)
                { Temperature = world.Environment.Temperature };
                foreach (var p in selected) { bed.MutablePlotIds.Add(p.Id); p.BedId = bed.Id; }
                world.Survival.MutableFacilities.Add(bed);
            }
        }

        public static FacilityState Bed(WorldState world, FarmPlotState plot) => plot.BedId == 0 ? null : world.Survival.FindFacility(plot.BedId);
        public static bool Protected(FacilityState bed) => bed != null && bed.Kind == FacilityKind.GrowingBed && bed.Complete && bed.Condition > 0;
        public static int SoilTemperature(WorldState world, FarmPlotState plot) => Protected(Bed(world, plot)) ? Bed(world, plot).Temperature : world.Environment.Temperature;
        public static int Snow(WorldState world, FarmPlotState plot) => Protected(Bed(world, plot)) ? Bed(world, plot).SnowDepth : world.Environment.SnowDepth;
        public static string Status(WorldState world, FacilityState bed)
        {
            if (!bed.Complete) { return "bed_construction"; }
            if (bed.Condition <= 30) { return "bed_maintenance"; }
            if (bed.SnowDepth >= SnowWorkThreshold) { return "bed_snow"; }
            if (bed.HeatPolicy == GrowingBedHeatPolicy.Off) { return bed.FuelTicks > 0 ? "bed_embers" : "bed_off"; }
            if (!Active(world, bed)) { return "bed_idle"; }
            if (bed.FuelTicks == 0 && bed.Inventory.Count(ItemKind.Wood) == 0) { return "bed_no_fuel"; }
            return bed.Temperature < TargetTemperature ? "bed_warming" : "bed_warm";
        }
        public static bool Active(WorldState world, FacilityState bed)
        {
            foreach (int id in bed.PlotIds)
            {
                var plot = world.Resources.FindPlot(id);
                if (plot.Crop != CropType.None || plot.Policy != FarmCropPolicy.Fallow && AgricultureSystem.ChooseCrop(world, plot) != null) { return true; }
            }
            return false;
        }
        public static bool NeedsWork(WorldState world, FacilityState bed) => bed.Kind == FacilityKind.GrowingBed &&
            bed.ReservedBy.IsNone && Active(world, bed) && (!bed.Complete || bed.Condition <= 30 ||
            bed.SnowDepth >= SnowWorkThreshold || bed.HeatPolicy != GrowingBedHeatPolicy.Off && bed.Inventory.Count(ItemKind.Wood) < 2);
        public static void Update(WorldState world)
        {
            foreach (var bed in world.Survival.Facilities)
            {
                if (bed.Kind != FacilityKind.GrowingBed) { continue; }
                int ambient = world.Environment.Temperature;
                int solar = Protected(bed) ? world.Environment.Daylight * Math.Max(0, 10000 - bed.SnowDepth) / 10000 * 250 / 10000 : 0;
                int difference = ambient + solar - bed.Temperature;
                bed.Temperature += Math.Sign(difference) * Math.Min(Math.Abs(difference), Math.Max(1, Math.Abs(difference) / (Protected(bed) ? 240 : 30)));
                int snow = ambient <= 0 ? world.Environment.Weather == WeatherKind.Blizzard ? 6 : world.Environment.Weather == WeatherKind.Snow ? 2 : 0 : -4;
                bed.SnowDepth = Math.Max(0, Math.Min(EnvironmentState.MaxSnowDepth, bed.SnowDepth + snow));
                if (!Protected(bed)) { bed.FuelTicks = 0; bed.HeatBudget = 0; continue; }
                if (bed.FuelTicks == 0 && bed.HeatPolicy != GrowingBedHeatPolicy.Off && bed.Temperature < TargetTemperature && Active(world, bed) && bed.Inventory.Count(ItemKind.Wood) > (bed.Condition <= 30 ? 1 : 0) && bed.Inventory.TryRemove(ItemKind.Wood, 1))
                {
                    world.Materials.Consume(ItemKind.Wood, 1); bed.FuelTicks = FuelDuration; bed.HeatBudget = FuelDuration * HeatPerTick;
                }
                if (bed.FuelTicks > 0)
                {
                    int heat = Math.Min(HeatPerTick, bed.HeatBudget);
                    bed.HeatBudget -= heat; bed.FuelTicks--;
                    bed.Temperature += Math.Min(heat, Math.Max(0, TargetTemperature - bed.Temperature));
                }
            }
        }
    }
}
