using System;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public sealed class ContinueObservationCommand : IWorldCommand
    {
        public string Describe() => "Continue observation after settlement (no time advance)";

        public bool Apply(WorldState world, CommandContext context)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (world.Campaign.Phase != CampaignPhase.SettlementLocked)
            {
                context.Events.Add(new SimEvent(SimEventKind.CommandRejected, context.Tick, EntityId.None,
                    "Continue observation rejected: the campaign is not settlement-locked."));
                return false;
            }

            // No simulation minute or result field changes at this command boundary.
            world.Campaign.Phase = CampaignPhase.Observation;
            return true;
        }
    }
}
