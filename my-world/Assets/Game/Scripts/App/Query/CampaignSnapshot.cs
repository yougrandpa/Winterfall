using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class CampaignResultSnapshot
    {
        internal CampaignResultSnapshot(CampaignResult result)
        {
            EndedTick = result.EndedTick;
            Grade = result.Grade;
            InitialCount = result.InitialCount;
            Survivors = result.Survivors;
            Active = result.Active;
            Downed = result.Downed;
            Recovering = result.Recovering;
            FoodReserve = result.FoodReserve;
            WaterReserve = result.WaterReserve;
            FuelReserve = result.FuelReserve;
            Assisted = result.Assisted;
            Reason = result.Reason;
            FoodMinutes = result.FoodMinutes;
            WaterMinutes = result.WaterMinutes;
            FuelMinutes = result.FuelMinutes;
            ReferenceConsumers = result.ReferenceConsumers;
            OccupiedShelters = result.OccupiedShelters;
            RemainingHeatingMinutes = result.RemainingHeatingMinutes;
            FoodStatus = result.FoodStatus;
            WaterStatus = result.WaterStatus;
            FuelStatus = result.FuelStatus;
            DeathCounts = Array.AsReadOnly(new List<int>(result.DeathCounts).ToArray());
        }

        public long EndedTick { get; }
        public string Grade { get; }
        public int InitialCount { get; }
        public int Survivors { get; }
        public int Active { get; }
        public int Downed { get; }
        public int Recovering { get; }
        public long FoodReserve { get; }
        public long WaterReserve { get; }
        public long FuelReserve { get; }
        public bool Assisted { get; }
        public string Reason { get; }
        public long FoodMinutes { get; }
        public long WaterMinutes { get; }
        public long FuelMinutes { get; }
        public int ReferenceConsumers { get; }
        public int OccupiedShelters { get; }
        public long RemainingHeatingMinutes { get; }
        public ReserveStatus FoodStatus { get; }
        public ReserveStatus WaterStatus { get; }
        public ReserveStatus FuelStatus { get; }
        public IReadOnlyList<int> DeathCounts { get; }
    }

    public sealed class CampaignSnapshot
    {
        internal CampaignSnapshot(WorldState world)
        {
            CampaignState campaign = world.Campaign;
            Phase = campaign.Phase;
            StartTick = campaign.StartTick;
            DeadlineTick = campaign.DeadlineTick;
            ChallengeDays = world.Config.ChallengeDays;
            Assisted = campaign.Assisted;
            InitialNpcs = Array.AsReadOnly(new List<EntityId>(campaign.InitialNpcs).ToArray());
            Result = campaign.Result == null ? null : new CampaignResultSnapshot(campaign.Result);
            // The deadline closes day N; earlier settlement retains its actual challenge day.
            long displayedTick = Result == null ? world.Tick : Result.EndedTick;
            ChallengeDay = IsUnlimited ? 0 :
                (int)Math.Min(ChallengeDays, Math.Max(0, displayedTick - StartTick) / GameCalendar.TicksPerDay + 1);
            RemainingMinutes = IsUnlimited || Result != null ? 0 : Math.Max(0, DeadlineTick - world.Tick);
            foreach (EntityId id in InitialNpcs)
            {
                if (!world.Entities.TryGetNpc(id, out var npc)) { continue; }
                switch (npc.Survival.Life)
                {
                    case LifeState.Active: Active++; break;
                    case LifeState.Downed: Downed++; break;
                    case LifeState.Recovering: Recovering++; break;
                }
            }
        }

        public CampaignPhase Phase { get; }
        public long StartTick { get; }
        public long DeadlineTick { get; }
        public int ChallengeDays { get; }
        public int ChallengeDay { get; }
        public long RemainingMinutes { get; }
        public IReadOnlyList<EntityId> InitialNpcs { get; }
        public int InitialCount => InitialNpcs.Count;
        public int Active { get; }
        public int Downed { get; }
        public int Recovering { get; }
        public int Survivors => Active + Downed + Recovering;
        public bool Assisted { get; }
        public CampaignResultSnapshot Result { get; }
        public bool IsUnlimited => ChallengeDays == 0;
        public bool IsLocked => Phase == CampaignPhase.SettlementLocked;
    }
}
