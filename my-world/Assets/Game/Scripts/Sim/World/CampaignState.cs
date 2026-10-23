using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
    public enum CampaignPhase
    {
        SurvivalActive,
        SettlementLocked,
        Observation
    }

    public enum ReserveStatus { NoDemand, Exhausted, Tight, Sufficient }

    public sealed class CampaignResult
    {
        internal CampaignResult(long endedTick, string grade, int initialCount, int survivors,
            int active, int downed, int recovering, long foodReserve, long waterReserve,
            long fuelReserve, bool assisted, string reason, long foodMinutes, long waterMinutes,
            long fuelMinutes, int referenceConsumers, int occupiedShelters, long remainingHeatingMinutes,
            ReserveStatus foodStatus, ReserveStatus waterStatus, ReserveStatus fuelStatus,
            IReadOnlyList<int> deathCounts)
        {
            EndedTick = endedTick;
            Grade = grade;
            InitialCount = initialCount;
            Survivors = survivors;
            Active = active;
            Downed = downed;
            Recovering = recovering;
            FoodReserve = foodReserve;
            WaterReserve = waterReserve;
            FuelReserve = fuelReserve;
            Assisted = assisted;
            Reason = reason;
            FoodMinutes = foodMinutes;
            WaterMinutes = waterMinutes;
            FuelMinutes = fuelMinutes;
            ReferenceConsumers = referenceConsumers;
            OccupiedShelters = occupiedShelters;
            RemainingHeatingMinutes = remainingHeatingMinutes;
            FoodStatus = foodStatus;
            WaterStatus = waterStatus;
            FuelStatus = fuelStatus;
            DeathCounts = Array.AsReadOnly(new List<int>(deathCounts).ToArray());
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
        // Indexed by NpcDeathCause; None is always zero, unattributed deaths are Unknown.
        public IReadOnlyList<int> DeathCounts { get; }
    }

    public sealed class CampaignState
    {
        private const string DeadlineReason = "deadline_reached";
        private const string ExtinctionReason = "all_initial_npcs_dead";
        private readonly WorldConfigData _config;
        private readonly List<EntityId> _initialNpcs = new List<EntityId>();
        private bool _initialized;

        public CampaignState(WorldConfigData config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            StartTick = config.StartTick;
            // Observation worlds have no finite deadline, even when their roster is empty.
            DeadlineTick = config.ChallengeDays == 0 ? long.MaxValue :
                checked(StartTick + (long)config.ChallengeDays * GameCalendar.TicksPerDay);
            Phase = config.ChallengeDays == 0 ? CampaignPhase.Observation : CampaignPhase.SurvivalActive;
            InitialNpcs = _initialNpcs.AsReadOnly();
        }

        public CampaignPhase Phase { get; internal set; }
        public long DeadlineTick { get; }
        public long StartTick { get; }
        public IReadOnlyList<EntityId> InitialNpcs { get; }
        public bool Assisted { get; internal set; }
        public CampaignResult Result { get; private set; }
        public bool CanAdvance => Phase != CampaignPhase.SettlementLocked;

        internal void Initialize(WorldState world)
        {
            RequireConfiguration(world);
            if (_initialized)
            {
                throw new InvalidOperationException("The initial campaign roster is already locked.");
            }
            if (world.Tick != StartTick)
            {
                throw new InvalidOperationException("The campaign roster must be initialized before the first update.");
            }
            if (_config.ChallengeDays > 0 &&
                (world.Entities.Npcs.Count == 0 || world.Entities.Npcs.Count != _config.NpcCount))
            {
                throw new InvalidOperationException("A challenge must create every configured initial NPC before initialization.");
            }

            foreach (NpcState npc in world.Entities.Npcs)
            {
                _initialNpcs.Add(npc.Id);
            }
            _initialized = true;
        }

        // The engine calls this after every complete minute, including the deadline's last minute.
        // True means a new immutable result was created; the engine owns the settlement event.
        internal bool Evaluate(WorldState world)
        {
            RequireConfiguration(world);
            if (!_initialized)
            {
                throw new InvalidOperationException("Initialize the campaign after creating the initial NPCs.");
            }
            if (Phase != CampaignPhase.SurvivalActive || Result != null || world.Tick <= StartTick)
            {
                return false;
            }
            if (world.Tick > DeadlineTick)
            {
                throw new InvalidOperationException("The challenge advanced past its deadline without settlement.");
            }

            CountSurvivors(world, out int active, out int downed, out int recovering);
            int survivors = active + downed + recovering;
            if (survivors > 0 && world.Tick < DeadlineTick)
            {
                return false;
            }

            long food = CountReserve(world, ItemKind.Food);
            long water = CountReserve(world, ItemKind.Water);
            long fuel = CountReserve(world, ItemKind.Wood);
            int consumers = world.LivingCount;
            CountHeating(world, out int occupiedShelters, out long remainingHeatingMinutes);
            long foodMinutes = SupplyMinutes(food, consumers, 4500, 6);
            long waterMinutes = SupplyMinutes(water, consumers, 5500, 9);
            long fuelMinutes = SupplyMinutes(fuel, occupiedShelters, SurvivalRules.FuelTicksPerWood, 1, remainingHeatingMinutes);
            Result = new CampaignResult(world.Tick, GradeFor(_initialNpcs.Count, survivors),
                _initialNpcs.Count, survivors, active, downed, recovering, food, water, fuel, Assisted,
                survivors == 0 ? ExtinctionReason : DeadlineReason, foodMinutes, waterMinutes, fuelMinutes,
                consumers, occupiedShelters, remainingHeatingMinutes, Assess(foodMinutes, consumers),
                Assess(waterMinutes, consumers), Assess(fuelMinutes, occupiedShelters), CountDeaths(world));
            Phase = CampaignPhase.SettlementLocked;
            return true;
        }

        internal void Write(BinaryWriter w)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("An uninitialized campaign cannot be saved.");
            }
            w.Write(StartTick);
            w.Write(DeadlineTick);
            w.Write(_config.ChallengeDays);
            w.Write((int)Phase);
            w.Write(Assisted);
            w.Write(_initialNpcs.Count);
            foreach (EntityId id in _initialNpcs)
            {
                w.Write(id.Value);
            }
            w.Write(Result != null);
            if (Result == null)
            {
                return;
            }
            w.Write(Result.EndedTick);
            w.Write(Result.Grade);
            w.Write(Result.InitialCount);
            w.Write(Result.Survivors);
            w.Write(Result.Active);
            w.Write(Result.Downed);
            w.Write(Result.Recovering);
            w.Write(Result.FoodReserve);
            w.Write(Result.WaterReserve);
            w.Write(Result.FuelReserve);
            w.Write(Result.Assisted);
            w.Write(Result.Reason);
            // Append only: existing result corruption offsets rely on the prefix above.
            w.Write(Result.FoodMinutes);
            w.Write(Result.WaterMinutes);
            w.Write(Result.FuelMinutes);
            w.Write(Result.ReferenceConsumers);
            w.Write(Result.OccupiedShelters);
            w.Write(Result.RemainingHeatingMinutes);
            w.Write((int)Result.FoodStatus);
            w.Write((int)Result.WaterStatus);
            w.Write((int)Result.FuelStatus);
            w.Write(Result.DeathCounts.Count);
            foreach (int count in Result.DeathCounts) { w.Write(count); }
        }

        internal void Read(BinaryReader r, WorldState world)
        {
            RequireConfiguration(world);
            var candidate = new CampaignState(_config);
            WorldSnapshot.Require(r.ReadInt64() == StartTick);
            WorldSnapshot.Require(r.ReadInt64() == DeadlineTick);
            WorldSnapshot.Require(r.ReadInt32() == _config.ChallengeDays);
            candidate.Phase = (CampaignPhase)r.ReadInt32();
            candidate.Assisted = r.ReadBoolean();
            int count = WorldSnapshot.ReadCount(r, 10000);
            for (int i = 0; i < count; i++)
            {
                candidate._initialNpcs.Add(new EntityId(r.ReadInt32()));
            }
            if (r.ReadBoolean())
            {
                long endedTick = r.ReadInt64();
                string grade = r.ReadString();
                int initial = r.ReadInt32(), survivors = r.ReadInt32(), active = r.ReadInt32();
                int downed = r.ReadInt32(), recovering = r.ReadInt32();
                long food = r.ReadInt64(), water = r.ReadInt64(), fuel = r.ReadInt64();
                bool assisted = r.ReadBoolean();
                string reason = r.ReadString();
                long foodMinutes = r.ReadInt64(), waterMinutes = r.ReadInt64(), fuelMinutes = r.ReadInt64();
                int consumers = r.ReadInt32(), occupied = r.ReadInt32();
                long heating = r.ReadInt64();
                var foodStatus = (ReserveStatus)r.ReadInt32();
                var waterStatus = (ReserveStatus)r.ReadInt32();
                var fuelStatus = (ReserveStatus)r.ReadInt32();
                int deathCount = WorldSnapshot.ReadCount(r, (int)NpcDeathCause.Unknown + 1);
                WorldSnapshot.Require(deathCount == (int)NpcDeathCause.Unknown + 1);
                var deaths = new int[deathCount];
                for (int i = 0; i < deaths.Length; i++) { deaths[i] = r.ReadInt32(); }
                candidate.Result = new CampaignResult(endedTick, grade, initial, survivors, active,
                    downed, recovering, food, water, fuel, assisted, reason, foodMinutes, waterMinutes,
                    fuelMinutes, consumers, occupied, heating, foodStatus, waterStatus, fuelStatus, deaths);
            }
            candidate._initialized = true;
            candidate.Validate(world);

            _initialNpcs.Clear();
            _initialNpcs.AddRange(candidate._initialNpcs);
            Phase = candidate.Phase;
            Assisted = candidate.Assisted;
            Result = candidate.Result;
            _initialized = true;
        }

        internal void Validate(WorldState world)
        {
            RequireConfiguration(world);
            WorldSnapshot.Require(_initialized && world.Tick >= StartTick &&
                Enum.IsDefined(typeof(CampaignPhase), Phase));
            int previousId = 0;
            WorldSnapshot.Require(_initialNpcs.Count <= world.Entities.Npcs.Count);
            for (int i = 0; i < _initialNpcs.Count; i++)
            {
                EntityId id = _initialNpcs[i];
                WorldSnapshot.Require(id.Value > previousId && world.Entities.Npcs[i].Id == id &&
                    world.Entities.TryGetNpc(id, out NpcState npc) &&
                    Enum.IsDefined(typeof(LifeState), npc.Survival.Life));
                previousId = id.Value;
            }

            if (_config.ChallengeDays == 0)
            {
                WorldSnapshot.Require(Phase == CampaignPhase.Observation && Result == null && DeadlineTick == long.MaxValue);
                return;
            }
            WorldSnapshot.Require(_initialNpcs.Count > 0 && _initialNpcs.Count == _config.NpcCount);
            if (Phase == CampaignPhase.SurvivalActive)
            {
                CountSurvivors(world, out int active, out int downed, out int recovering);
                WorldSnapshot.Require(Result == null && world.Tick < DeadlineTick &&
                    (world.Tick == StartTick || active + downed + recovering > 0));
                return;
            }

            WorldSnapshot.Require(Result != null);
            WorldSnapshot.Require(Result.EndedTick > StartTick && Result.EndedTick <= DeadlineTick &&
                Result.EndedTick <= world.Tick && Result.InitialCount == _initialNpcs.Count &&
                Result.Survivors >= 0 && Result.Survivors <= Result.InitialCount &&
                Result.Active >= 0 && Result.Downed >= 0 && Result.Recovering >= 0 &&
                (long)Result.Active + Result.Downed + Result.Recovering == Result.Survivors &&
                Result.FoodReserve >= 0 && Result.WaterReserve >= 0 && Result.FuelReserve >= 0 &&
                Result.Grade == GradeFor(Result.InitialCount, Result.Survivors) &&
                (!Result.Assisted || Assisted));
            WorldSnapshot.Require(Result.Survivors == 0 ? Result.Reason == ExtinctionReason :
                Result.Reason == DeadlineReason && Result.EndedTick == DeadlineTick);
            // Validate frozen inputs and derived fields without consulting the continuing world.
            WorldSnapshot.Require(Result.ReferenceConsumers >= Result.Survivors && Result.ReferenceConsumers <= 10000 &&
                Result.OccupiedShelters >= 0 && Result.OccupiedShelters <= Result.ReferenceConsumers &&
                Result.RemainingHeatingMinutes >= 0 &&
                Result.RemainingHeatingMinutes <= (long)Result.OccupiedShelters * SurvivalRules.FuelTicksPerWood &&
                Result.FoodReserve <= long.MaxValue / 4500 && Result.WaterReserve <= long.MaxValue / 5500 &&
                Result.FuelReserve <= (long.MaxValue - Result.RemainingHeatingMinutes) / SurvivalRules.FuelTicksPerWood);
            ValidateSupply(Result.FoodReserve, Result.ReferenceConsumers, 4500, 6, 0, Result.FoodMinutes, Result.FoodStatus);
            ValidateSupply(Result.WaterReserve, Result.ReferenceConsumers, 5500, 9, 0, Result.WaterMinutes, Result.WaterStatus);
            ValidateSupply(Result.FuelReserve, Result.OccupiedShelters, SurvivalRules.FuelTicksPerWood, 1,
                Result.RemainingHeatingMinutes, Result.FuelMinutes, Result.FuelStatus);
            WorldSnapshot.Require(Result.DeathCounts.Count == (int)NpcDeathCause.Unknown + 1 &&
                Result.DeathCounts[(int)NpcDeathCause.None] == 0);
            long deaths = 0;
            foreach (int count in Result.DeathCounts)
            {
                WorldSnapshot.Require(count >= 0 && count <= Result.InitialCount);
                deaths += count;
            }
            WorldSnapshot.Require(deaths == Result.InitialCount - Result.Survivors);
            if (Phase == CampaignPhase.SettlementLocked)
            {
                CountSurvivors(world, out int active, out int downed, out int recovering);
                CountHeating(world, out int occupied, out long heating);
                WorldSnapshot.Require(world.Tick == Result.EndedTick && Result.Assisted == Assisted &&
                    Result.Active == active && Result.Downed == downed && Result.Recovering == recovering &&
                    Result.FoodReserve == CountReserve(world, ItemKind.Food) &&
                    Result.WaterReserve == CountReserve(world, ItemKind.Water) &&
                    Result.FuelReserve == CountReserve(world, ItemKind.Wood) &&
                    Result.ReferenceConsumers == world.LivingCount && Result.OccupiedShelters == occupied &&
                    Result.RemainingHeatingMinutes == heating);
                int[] currentDeaths = CountDeaths(world);
                for (int i = 0; i < currentDeaths.Length; i++)
                {
                    WorldSnapshot.Require(Result.DeathCounts[i] == currentDeaths[i]);
                }
            }
        }

        private static long SupplyMinutes(long reserve, int consumers, int perUnit, int demand, long heating = 0)
        {
            return consumers == 0 ? 0 : checked(reserve * perUnit + heating) / ((long)consumers * demand);
        }

        private static ReserveStatus Assess(long minutes, int consumers)
        {
            return consumers == 0 ? ReserveStatus.NoDemand : minutes == 0 ? ReserveStatus.Exhausted :
                minutes < GameCalendar.TicksPerDay ? ReserveStatus.Tight : ReserveStatus.Sufficient;
        }

        private static void ValidateSupply(long reserve, int consumers, int perUnit, int demand,
            long heating, long minutes, ReserveStatus status)
        {
            WorldSnapshot.Require(minutes >= 0 && Enum.IsDefined(typeof(ReserveStatus), status) &&
                minutes == SupplyMinutes(reserve, consumers, perUnit, demand, heating) && status == Assess(minutes, consumers));
        }

        private int[] CountDeaths(WorldState world)
        {
            var counts = new int[(int)NpcDeathCause.Unknown + 1];
            foreach (EntityId id in _initialNpcs)
            {
                WorldSnapshot.Require(world.Entities.TryGetNpc(id, out NpcState npc));
                if (npc.Survival.Life != LifeState.Dead) { continue; }
                NpcDeathCause cause = npc.Survival.DeathCause;
                WorldSnapshot.Require(Enum.IsDefined(typeof(NpcDeathCause), cause));
                counts[(int)(cause == NpcDeathCause.None ? NpcDeathCause.Unknown : cause)]++;
            }
            return counts;
        }

        private static void CountHeating(WorldState world, out int occupied, out long heating)
        {
            occupied = 0;
            heating = 0;
            var seen = new HashSet<int>();
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                if (!seen.Add(shelter.Id) || !world.Map.IsInside(shelter.Position) ||
                    world.Map.GetTile(shelter.Position) != TileType.House || !world.Map.IsWalkable(shelter.Entrance) ||
                    shelter.Position.ManhattanDistance(shelter.Entrance) != 1)
                {
                    continue;
                }
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    if (npc.Survival.Life == LifeState.Dead || npc.Survival.ShelterId != shelter.Id) { continue; }
                    occupied++;
                    // Heat already spent on melting snow cannot also supply future heating.
                    heating += Math.Max(0, Math.Min(shelter.FuelTicks, shelter.HeatBudget / SurvivalRules.HeatPerTick));
                    break;
                }
            }
        }

        private void RequireConfiguration(WorldState world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            WorldSnapshot.Require(world.Config.StartTick == StartTick &&
                world.Config.ChallengeDays == _config.ChallengeDays && world.Config.NpcCount == _config.NpcCount);
        }

        private void CountSurvivors(WorldState world, out int active, out int downed, out int recovering)
        {
            active = 0;
            downed = 0;
            recovering = 0;
            foreach (EntityId id in _initialNpcs)
            {
                WorldSnapshot.Require(world.Entities.TryGetNpc(id, out NpcState npc));
                switch (npc.Survival.Life)
                {
                    case LifeState.Active: active++; break;
                    case LifeState.Downed: downed++; break;
                    case LifeState.Recovering: recovering++; break;
                    case LifeState.Dead: break;
                    default: throw new InvalidDataException("Invalid campaign NPC life state.");
                }
            }
        }

        private static string GradeFor(int initial, int survivors)
        {
            if (survivors == 0)
            {
                return "F";
            }
            if (survivors == initial)
            {
                return "S";
            }
            if ((long)survivors * 100 >= (long)initial * 80)
            {
                return "A";
            }
            if ((long)survivors * 100 >= (long)initial * 50)
            {
                return "B";
            }
            return "C";
        }

        private static long CountReserve(WorldState world, ItemKind item)
        {
            // Count materialized stores, not renewable nodes, standing crops, animals or
            // production counters. Raw water (and any future raw meat) is not ready supply.
            // Home supplies currently live in the resident's bag or a ground stock.
            var inventories = new HashSet<InventoryState>();
            long total = AddReserve(inventories, world.Resources.Stockpile, item) +
                AddReserve(inventories, world.Market.Inventory, item);
            foreach (FacilityState facility in world.Survival.Facilities)
            {
                total += AddReserve(inventories, facility.Inventory, item);
            }
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                total += AddReserve(inventories, shelter.Inventory, item);
            }
            foreach (GroundStockState ground in world.Survival.Ground)
            {
                total += AddReserve(inventories, ground.Inventory, item);
            }
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Survival.Life != LifeState.Dead)
                {
                    total += AddReserve(inventories, npc.Inventory, item);
                }
            }
            return total;
        }

        private static long AddReserve(HashSet<InventoryState> inventories, InventoryState inventory, ItemKind item)
        {
            return inventories.Add(inventory) ? inventory.Count(item) : 0;
        }
    }
}
