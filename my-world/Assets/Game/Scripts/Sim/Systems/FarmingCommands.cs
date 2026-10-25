using System;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public sealed class SetFarmPlotPolicyCommand : IWorldCommand
    {
        public SetFarmPlotPolicyCommand(int plotId, FarmCropPolicy policy) { PlotId = plotId; Policy = policy; }
        public int PlotId { get; }
        public FarmCropPolicy Policy { get; }
        public string Describe() => "Farm plot " + PlotId + " policy " + Policy;
        public bool Apply(WorldState world, CommandContext context)
        {
            var plot = world.Resources.FindPlot(PlotId);
            if (plot == null || !Enum.IsDefined(typeof(FarmCropPolicy), Policy)) { return Reject(context); }
            if (plot.Policy == Policy) { return true; }
            plot.Policy = Policy;
            if (plot.Crop != CropType.None) { plot.FinishAfterHarvest = true; }
            if (world.Campaign.Result == null) { world.Campaign.Assisted = true; }
            return true;
        }
        internal static bool Reject(CommandContext context)
        {
            context.Events.Add(new SimEvent(SimEventKind.CommandRejected, context.Tick, EntityId.None, "无效的农田、菜床或种植方针。")); return false;
        }
    }
    public sealed class SetGrowingBedHeatPolicyCommand : IWorldCommand
    {
        public SetGrowingBedHeatPolicyCommand(int bedId, GrowingBedHeatPolicy policy) { BedId = bedId; Policy = policy; }
        public int BedId { get; }
        public GrowingBedHeatPolicy Policy { get; }
        public string Describe() => "Growing bed " + BedId + " heating " + Policy;
        public bool Apply(WorldState world, CommandContext context)
        {
            var bed = world.Survival.FindFacility(BedId);
            if (bed == null || bed.Kind != FacilityKind.GrowingBed || !Enum.IsDefined(typeof(GrowingBedHeatPolicy), Policy)) { return SetFarmPlotPolicyCommand.Reject(context); }
            if (bed.HeatPolicy == Policy) { return true; }
            bed.HeatPolicy = Policy;
            if (world.Campaign.Result == null) { world.Campaign.Assisted = true; }
            return true;
        }
    }
}
