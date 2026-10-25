using System;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class StateHasher
    {
        public const int Version = 9;
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Compute(WorldState state, int version = Version)
        {
            if (version != Version) { throw new ArgumentOutOfRangeException(nameof(version)); }
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            ulong hash = FnvOffset;
            hash = Mix(hash, version);
            hash = Mix(hash, state.Tick);
            hash = Mix(hash, state.Config.Seed);
            hash = Mix(hash, state.Config.Width);
            hash = Mix(hash, state.Config.Height);
            hash = Mix(hash, state.Config.NpcCount);
            hash = Mix(hash, state.Config.StartTick);
            hash = Mix(hash, state.Config.ChallengeDays);
            hash = Mix(hash, state.Config.TicksPerSecondAt1x);
            int[] speeds = state.Config.SpeedMultipliers;
            hash = Mix(hash, speeds.Length);
            for (int i = 0; i < speeds.Length; i++)
            {
                hash = Mix(hash, speeds[i]);
            }
            hash = Mix(hash, state.Rng.State);
            hash = Mix(hash, state.NextEntityId);

            for (int y = 0; y < state.Map.Height; y++)
            {
                for (int x = 0; x < state.Map.Width; x++)
                {
                    hash = Mix(hash, (int)state.Map.GetTile(x, y));
                }
            }

            hash = Mix(hash, state.Ecology.Rng.State);
            hash = Mix(hash, state.Ecology.BirthProgress);
            hash = Mix(hash, state.Ecology.Births);
            hash = Mix(hash, state.Ecology.NaturalDeaths);
            hash = Mix(hash, state.Ecology.HuntKills);
            hash = Mix(hash, state.Ecology.Animals.Count);
            for (int i = 0; i < state.Ecology.Animals.Count; i++)
            {
                AnimalState animal = state.Ecology.Animals[i];
                hash = Mix(hash, animal.Id.Value);
                hash = Mix(hash, (int)animal.Species);
                hash = Mix(hash, animal.Position.X);
                hash = Mix(hash, animal.Position.Y);
                hash = Mix(hash, animal.Health);
                hash = Mix(hash, animal.AgeTicks);
                hash = Mix(hash, animal.RngState);
                hash = Mix(hash, animal.ReservedBy.Value);
                hash = Mix(hash, animal.Fleeing ? 1 : 0);
            }
            hash = MixInventory(hash, state.Market.Inventory);
            hash = Mix(hash, state.Market.Treasury);
            hash = Mix(hash, state.Market.IssuedCurrency);
            hash = Mix(hash, state.Market.NextOrderId);
            hash = Mix(hash, state.Market.FilledCount);
            hash = Mix(hash, state.Market.RejectedCount);
            hash = Mix(hash, state.Market.Volume);
            hash = Mix(hash, state.Market.Ledger.Count);
            hash = Mix(hash, state.Market.LedgerHash);
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                ItemKind item = (ItemKind)i;
                hash = Mix(hash, state.Market.Price(item));
                hash = Mix(hash, state.Market.Bought(item));
                hash = Mix(hash, state.Market.Sold(item));
                hash = Mix(hash, state.Materials.Initial(item));
                hash = Mix(hash, state.Materials.Produced(item));
                hash = Mix(hash, state.Materials.Consumed(item));
            }
            hash = Mix(hash, state.Resources.StockpilePosition.X);
            hash = Mix(hash, state.Resources.StockpilePosition.Y);
            hash = MixInventory(hash, state.Resources.Stockpile);
            hash = Mix(hash, state.Resources.Nodes.Count);
            for (int i = 0; i < state.Resources.Nodes.Count; i++)
            {
                ResourceNodeState node = state.Resources.Nodes[i];
                hash = Mix(hash, node.Id);
                hash = Mix(hash, node.Position.X);
                hash = Mix(hash, node.Position.Y);
                hash = Mix(hash, (int)node.Item);
                hash = Mix(hash, node.Amount);
                hash = Mix(hash, node.Capacity);
                hash = Mix(hash, node.RegenerationTicks);
                hash = Mix(hash, node.RegenerationProgress);
                hash = Mix(hash, node.ReservedBy.Value);
            }
            hash = Mix(hash, state.Resources.FarmPlots.Count);
            for (int i = 0; i < state.Resources.FarmPlots.Count; i++)
            {
                FarmPlotState plot = state.Resources.FarmPlots[i];
                hash = Mix(hash, plot.Id);
                hash = Mix(hash, plot.Position.X);
                hash = Mix(hash, plot.Position.Y);
                hash = Mix(hash, (int)plot.Crop);
                hash = Mix(hash, plot.GrowthTicks);
                hash = Mix(hash, plot.Moisture);
                hash = Mix(hash, plot.Fertility);
                hash = Mix(hash, plot.ReservedBy.Value);
            }
            hash = Mix(hash, state.Resources.Reservations.Count);
            for (int i = 0; i < state.Resources.Reservations.Count; i++)
            {
                ResourceReservation reservation = state.Resources.Reservations[i];
                hash = Mix(hash, reservation.Owner.Value);
                hash = Mix(hash, (int)reservation.Kind);
                hash = Mix(hash, reservation.ResourceId);
                hash = Mix(hash, (int)reservation.Item);
                hash = Mix(hash, reservation.Quantity);
                hash = Mix(hash, reservation.Position.X);
                hash = Mix(hash, reservation.Position.Y);
            }
            hash = Mix(hash, state.Entities.Npcs.Count);
            for (int i = 0; i < state.Entities.Npcs.Count; i++)
            {
                NpcState npc = state.Entities.Npcs[i];
                hash = Mix(hash, npc.Id.Value);
                hash = Mix(hash, npc.Name);
                hash = Mix(hash, npc.Role);
                hash = Mix(hash, npc.Identity.Age);
                hash = MixInventory(hash, npc.Inventory);
                hash = Mix(hash, npc.Inventory.UndeliveredFarmFood);
                hash = Mix(hash, npc.Coins);
                hash = Mix(hash, npc.HasHuntingTool ? 1 : 0);
                hash = Mix(hash, npc.HuntingSkill);
                hash = Mix(hash, npc.HuntKills);
                hash = Mix(hash, npc.HarvestedFood);
                hash = Mix(hash, npc.GatheredFood);
                hash = Mix(hash, npc.Purchases);
                hash = Mix(hash, npc.Sales);
                hash = Mix(hash, (int)npc.Plan.Goal);
                hash = Mix(hash, npc.Plan.StepIndex);
                hash = Mix(hash, npc.Plan.FailedStepIndex);
                hash = Mix(hash, npc.Plan.StepStarted ? 1 : 0);
                hash = Mix(hash, npc.Plan.ReplanCount);
                hash = Mix(hash, npc.Plan.FailureReason);
                hash = Mix(hash, npc.Plan.Steps.Count);
                for (int p = 0; p < npc.Plan.Steps.Count; p++)
                {
                    PlanStep step = npc.Plan.Steps[p];
                    hash = Mix(hash, (int)step.Type);
                    hash = Mix(hash, step.Target.X);
                    hash = Mix(hash, step.Target.Y);
                    hash = Mix(hash, step.ResourceId);
                    hash = Mix(hash, (int)step.Item);
                    hash = Mix(hash, step.Quantity);
                    hash = Mix(hash, step.DurationTicks);
                    hash = Mix(hash, step.UnitPrice);
                    hash = Mix(hash, step.RecipeId);
                }
                hash = Mix(hash, npc.LastDecision.Goal);
                hash = Mix(hash, npc.LastDecision.ReasonCode);
                hash = Mix(hash, npc.LastDecision.ChosenScore);
                hash = Mix(hash, npc.LastDecision.Options.Count);
                for (int option = 0; option < npc.LastDecision.Options.Count; option++)
                {
                    hash = Mix(hash, npc.LastDecision.Options[option].Id);
                    hash = Mix(hash, npc.LastDecision.Options[option].Score);
                }
                hash = Mix(hash, npc.Position.X);
                hash = Mix(hash, npc.Position.Y);
                hash = Mix(hash, npc.Home.X);
                hash = Mix(hash, npc.Home.Y);
                hash = Mix(hash, npc.RngState);
                hash = Mix(hash, npc.Personality.Diligence);
                hash = Mix(hash, npc.Personality.Caution);
                hash = Mix(hash, npc.Personality.Sociability);
                hash = Mix(hash, npc.Personality.Curiosity);
                hash = Mix(hash, npc.Needs.Hunger);
                hash = Mix(hash, npc.Needs.Thirst);
                hash = Mix(hash, npc.Needs.Fatigue);
                hash = Mix(hash, npc.Needs.Health);
                hash = Mix(hash, npc.Needs.Safety);
                hash = Mix(hash, (int)npc.Action.Type);
                hash = Mix(hash, npc.Action.Label);
                hash = Mix(hash, npc.Action.EndTick);
                hash = Mix(hash, npc.Action.Target.X);
                hash = Mix(hash, npc.Action.Target.Y);
                hash = Mix(hash, npc.Action.PathIndex);
                hash = Mix(hash, npc.Action.Path.Count);
                for (int step = 0; step < npc.Action.Path.Count; step++)
                {
                    hash = Mix(hash, npc.Action.Path[step].X);
                    hash = Mix(hash, npc.Action.Path[step].Y);
                }
            }

            if (version >= 4)
            {
                hash = Mix(hash, state.Social.NextSequence);
                hash = Mix(hash, state.Social.Greetings); hash = Mix(hash, state.Social.Gifts);
                hash = Mix(hash, state.Social.Timeline.Count);
                foreach (SocialEvent evt in state.Social.Timeline) { hash = MixSocialEvent(hash, evt); }
                foreach (NpcState npc in state.Entities.Npcs)
                {
                    hash = Mix(hash, npc.Social.NextInteractionTick); hash = Mix(hash, npc.Social.Relations.Count);
                    foreach (TrustRelation relation in npc.Social.Relations)
                    {
                        hash = Mix(hash, relation.Other.Value); hash = Mix(hash, relation.Trust);
                    }
                    hash = Mix(hash, npc.Social.Memories.Count);
                    foreach (SocialEvent evt in npc.Social.Memories) { hash = MixSocialEvent(hash, evt); }
                }
            }
            foreach (byte value in SurvivalSnapshot.Capture(state)) { hash = Mix(hash, (int)value); }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WorldSnapshot.WriteEnvironment(writer, state);
                state.Campaign.Write(writer);
                state.Ecology.WriteSnapshot(writer);
                CombatSnapshot.Write(writer, state);
                HuntingSnapshot.Write(writer, state);
                foreach (byte value in stream.ToArray()) { hash = Mix(hash, (int)value); }
            }
            return hash;
        }

        private static ulong MixSocialEvent(ulong hash, SocialEvent evt)
        {
            hash = Mix(hash, evt.Sequence); hash = Mix(hash, evt.Tick); hash = Mix(hash, evt.Actor.Value); hash = Mix(hash, evt.Target.Value);
            hash = Mix(hash, (int)evt.Kind); hash = Mix(hash, evt.TrustBefore); hash = Mix(hash, evt.TrustAfter);
            hash = Mix(hash, evt.Reason); return Mix(hash, evt.CauseSequence);
        }

        public static string ToHex(ulong hash)
        {
            return hash.ToString("x16");
        }

        private static ulong MixInventory(ulong hash, InventoryState inventory)
        {
            hash = Mix(hash, inventory.Capacity);
            for (int i = 0; i < (int)ItemKind.Count; i++) { hash = Mix(hash, inventory.Count((ItemKind)i)); }
            return hash;
        }

        private static ulong Mix(ulong hash, string value)
        {
            if (value == null)
            {
                return Mix(hash, -1);
            }
            hash = Mix(hash, value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                hash = Mix(hash, (int)value[i]);
            }
            return hash;
        }

        private static ulong Mix(ulong hash, long value)
        {
            unchecked
            {
                hash ^= (ulong)value;
                hash *= FnvPrime;
                return hash;
            }
        }

        private static ulong Mix(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (ulong)(uint)value;
                hash *= FnvPrime;
                return hash;
            }
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= FnvPrime;
                return hash;
            }
        }
    }
}
