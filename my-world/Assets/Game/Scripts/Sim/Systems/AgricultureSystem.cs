using System;
using MyAiWorld.Sim.World;
using MyAiWorld.Sim.Agents;

namespace MyAiWorld.Sim.Systems
{
    public static class AgricultureSystem
    {
        internal static int FoodTarget(WorldState world) => Math.Max(40, world.LivingCount * (world.Date.Season == Season.Autumn ? 24 : 8));
        public static bool CanWater(WorldState world, FarmPlotState plot) => plot.Crop != CropType.None && plot.Stage != CropStage.Mature && GrowingBedSystem.SoilTemperature(world, plot) > 0;
        public static bool CanPlant(WorldState world, FarmPlotState plot, CropDefinition crop) => crop != null && plot.Crop == CropType.None && plot.Policy != FarmCropPolicy.Fallow &&
            (plot.Policy == FarmCropPolicy.Auto || (int)plot.Policy == (int)crop.Type) && plot.Fertility >= 2000 &&
            GrowingBedSystem.SoilTemperature(world, plot) >= crop.MinimumTemperature && GrowingBedSystem.Snow(world, plot) < 3000;
        internal static bool NeedsWork(WorldState world, FarmPlotState plot, NpcState farmer = null) => plot.Stage == CropStage.Mature ||
            CanWater(world, plot) && plot.Moisture < 180 || CanPlant(world, plot, ChooseCrop(world, plot, farmer)) && world.Resources.Stockpile.Food < FoodTarget(world);
        internal static CropDefinition ChooseCrop(WorldState world, FarmPlotState plot, NpcState farmer = null)
        {
            if (plot.Policy == FarmCropPolicy.Fallow) { return null; }
            CropDefinition best = null; int bestCount = int.MaxValue;
            int temperature = GrowingBedSystem.SoilTemperature(world, plot);
            bool bestCanGrow = false;
            foreach (var crop in CropDefinition.All)
            {
                if (plot.Policy != FarmCropPolicy.Auto && (int)plot.Policy != (int)crop.Type) { continue; }
                int seeds = farmer == null ? world.Resources.Stockpile.Count(crop.Seed) : world.Resources.AvailableStock(crop.Seed);
                if (farmer != null) { seeds += farmer.Inventory.Count(crop.Seed); }
                else { foreach (var npc in world.Entities.Npcs) { seeds += npc.Inventory.Count(crop.Seed); } }
                if (seeds == 0) { continue; }
                int count = 0;
                foreach (var other in world.Resources.FarmPlots) { if (other.Crop == crop.Type) { count++; } }
                count = count * 100 + Math.Max(0, crop.MinimumTemperature - temperature);
                bool canGrow = temperature >= crop.MinimumTemperature;
                // Prefer a crop that can grow now; keep a cold candidate only so
                // an empty bed can still request construction and heating.
                if (best == null || (canGrow && !bestCanGrow) || (canGrow == bestCanGrow && count < bestCount))
                { best = crop; bestCount = count; bestCanGrow = canGrow; }
            }
            return best;
        }
        public static string Status(WorldState world, FarmPlotState plot)
        {
            if (plot.Crop == CropType.None)
            {
                if (plot.Policy == FarmCropPolicy.Fallow) { return "fallow"; }
                if (ChooseCrop(world, plot) == null) { return "no_seeds"; }
                var bed = GrowingBedSystem.Bed(world, plot);
                if (bed != null && !bed.Complete) { return "bed_construction"; }
                return CanPlant(world, plot, ChooseCrop(world, plot)) ? "ready_to_plant" : "soil_too_cold";
            }
            if (plot.Stage == CropStage.Mature) { return "ready_to_harvest"; }
            if (GrowingBedSystem.SoilTemperature(world, plot) < CropDefinition.For(plot.Crop).MinimumTemperature) { return "soil_too_cold"; }
            if (GrowingBedSystem.Snow(world, plot) >= 10000) { return "snow_covered"; }
            if (world.Environment.Daylight == 0) { return "no_daylight"; }
            if (plot.Moisture == 0) { return "dry_soil"; }
            if (GrowingBedSystem.Snow(world, plot) >= 3000) { return "snow_shaded"; }
            return plot.HarvestCount > 0 ? "regrowing" : "growing";
        }
        internal static void Update(WorldState world)
        {
            foreach (FarmPlotState plot in world.Resources.FarmPlots)
            {
                int temperature = GrowingBedSystem.SoilTemperature(world, plot);
                if (plot.Crop == CropType.None)
                {
                    if (temperature > 0 && world.Tick % 10 == 0) { plot.Fertility = Math.Min(10000, plot.Fertility + 2); }
                    continue;
                }
                var crop = CropDefinition.For(plot.Crop);
                if (temperature > 0 && world.Tick % 3 == 0) { plot.Moisture = Math.Max(0, plot.Moisture - 1); }
                if (plot.Moisture == 0 && temperature > 0 && world.Tick % 3 == 0) { plot.Damage++; }
                int tolerance = plot.Stage == CropStage.Seedling ? crop.ColdTolerance / 2 : crop.ColdTolerance;
                if (temperature < tolerance) { plot.Damage += Math.Max(1, (tolerance - temperature) / 400); }
                if (plot.Damage >= SurvivalRules.CropDeathDamage)
                {
                    ApplyDamage(world, plot, 0); continue;
                }
                if (plot.Stage == CropStage.Mature || plot.Moisture == 0 || temperature < crop.MinimumTemperature) { continue; }
                int light = world.Environment.Daylight * Math.Max(0, 10000 - GrowingBedSystem.Snow(world, plot)) / 10000;
                int warmth = Math.Min(10000, Math.Max(0, temperature - crop.MinimumTemperature + 100) * 10000 / (1000 - crop.MinimumTemperature + 100));
                plot.GrowthRemainder += (long)light * warmth;
                plot.GrowthTicks = Math.Min(plot.GrowthRequired, plot.GrowthTicks + (int)(plot.GrowthRemainder / 100000000));
                plot.GrowthRemainder %= 100000000;
            }
        }
        internal static void ApplyDamage(WorldState world, FarmPlotState plot, int amount)
        {
            plot.Damage += amount;
            if (plot.Crop != CropType.None && plot.Damage >= SurvivalRules.CropDeathDamage)
            {
                Clear(plot);
                plot.LostCrop = true;
                world.Survival.CropsLost++;
            }
        }
        internal static void Harvest(FarmPlotState plot)
        {
            plot.HarvestCount++; plot.TotalHarvests++;
            plot.Fertility = Math.Max(0, plot.Fertility + CropDefinition.For(plot.Crop).FertilityChange);
            if (plot.FinishAfterHarvest || plot.Policy == FarmCropPolicy.Fallow || plot.HarvestCount >= CropDefinition.For(plot.Crop).MaxHarvests || plot.Fertility < 2000)
            { Clear(plot); return; }
            plot.GrowthTicks = 0; plot.GrowthRemainder = 0;
        }
        private static void Clear(FarmPlotState plot)
        {
            plot.PreviousCrop = plot.Crop; plot.Crop = CropType.None; plot.GrowthTicks = 0; plot.GrowthRemainder = 0;
            plot.HarvestCount = 0; plot.Damage = 0; plot.FinishAfterHarvest = false;
        }
    }
}
