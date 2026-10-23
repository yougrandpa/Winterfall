using System;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class AgricultureSystem
    {
        internal static int FoodTarget(WorldState world) => Math.Max(40, world.LivingCount * (world.Date.Season == Season.Autumn ? 24 : 8));
        internal static bool NeedsWork(WorldState world, FarmPlotState plot) => plot.Stage == CropStage.Mature ||
            (plot.Crop != CropType.None && plot.Moisture < 180) ||
            (plot.Crop == CropType.None && plot.Fertility >= 2000 && world.Date.Season != Season.Winter && world.Resources.Stockpile.Food < FoodTarget(world));
        internal static CropDefinition ChooseCrop(WorldState world, FarmPlotState plot)
        {
            if (plot.Fertility < 6500 && plot.PreviousCrop != CropType.Bean) { return CropDefinition.Bean; }
            if (world.Date.Season == Season.Autumn && world.Date.DayOfSeason >= 26) { return CropDefinition.Turnip; }
            if (plot.PreviousCrop == CropType.Turnip) { return CropDefinition.Wheat; }
            if (plot.PreviousCrop == CropType.Wheat) { return CropDefinition.Bean; }
            return CropDefinition.Turnip;
        }
        internal static void Update(WorldState world)
        {
            foreach (FarmPlotState plot in world.Resources.FarmPlots)
            {
                if (plot.Crop == CropType.None) { plot.Fertility = Math.Min(10000, plot.Fertility + 2); continue; }
                if (plot.Stage == CropStage.Mature) { continue; }
                bool winter = world.Date.Season == Season.Winter;
                if (plot.Moisture > 0)
                {
                    plot.Moisture = Math.Max(0, plot.Moisture - (world.Date.Season == Season.Summer ? 2 : 1));
                    if (!winter || world.Tick % 4 == 0) { plot.GrowthTicks++; }
                }
                else { plot.Damage++; }
                if (winter && world.Tick % 2 == 0) { plot.Damage++; }
                if (plot.Damage >= SurvivalRules.CropDeathDamage)
                {
                    plot.PreviousCrop = plot.Crop; plot.Crop = CropType.None; plot.GrowthTicks = 0;
                    plot.Damage = 0; plot.Moisture = 0; world.Survival.CropsLost++;
                }
            }
        }
    }
}
