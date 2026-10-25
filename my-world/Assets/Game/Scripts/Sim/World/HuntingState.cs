using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum HuntStage
    {
        Gathering,
        Approaching,
        Engaging,
        Retrieving,
        Returning,
        Retreating
    }

    public enum HuntMotion { Walk, Sneak, Pursuit, Recover }

    public enum HuntRole
    {
        Archer,
        Spear,
        Flanker
    }

    public enum EncounterPhase
    {
        Calm,
        Alert,
        Fleeing,
        Defending,
        Recovering
    }

    public static class HuntingRules
    {
        public const int MaxMembers = 3;
        public const int RecruitDistance = 12;
        public const int GatherTicks = 90;
        public const int DepartureNeed = 6000;
        public const int ArrowsPerArcher = 4;
        public const int PursuitTicks = 120;
        public const int LostTicks = 12;
        public const int RetryTicks = 60;
        public const int RetreatHealth = 4000;
        public const int RetreatEnergy = 2500;
        public const int JoinHealth = 6000;
        public const int JoinEnergy = 4000;
        public const int MaxStamina = 10000;
        public const int SprintCost = 1250;
        public const int StaminaRecovery = 400;
        public const int PackRadius = 6;
        public const int CoverTicks = 3;
        public const int MaxPredictionCells = 2;
        public const int PathWaitTicks = 8;
        public static int RequiredMembers(AnimalSpecies species) => species == AnimalSpecies.Wolf ? 3 : species == AnimalSpecies.Boar || species == AnimalSpecies.Deer ? 2 : 1;
        public static bool NeedsSpear(AnimalSpecies species) => species == AnimalSpecies.Boar || species == AnimalSpecies.Wolf;
    }

    public sealed class AnimalEncounterState
    {
        public EncounterPhase Phase
        {
            get;
            internal set;
        }

        public EntityId ThreatId
        {
            get;
            internal set;
        }

        public bool ThreatIsAnimal
        {
            get;
            internal set;
        }

        public GridPos LastThreatPosition
        {
            get;
            internal set;
        }

        = GridPos.Invalid;
        public long ThreatUntilTick
        {
            get;
            internal set;
        }

        public long AlertTick
        {
            get;
            internal set;
        }

        public long NextStepTick
        {
            get;
            internal set;
        }

        public int Stamina
        {
            get;
            internal set;
        }

        = HuntingRules.MaxStamina;
    }

    public sealed class HuntMemberState
    {
        internal HuntMemberState(EntityId id, HuntRole role)
        {
            NpcId = id;
            Role = role;
        }

        public EntityId NpcId
        {
            get;
        }

        public HuntRole Role
        {
            get;
            internal set;
        }

        public GridPos WorkCell
        {
            get;
            internal set;
        }

        = GridPos.Invalid;
    }

    public sealed class HuntReturnState
    {
        internal HuntReturnState(EntityId id) { NpcId = id; }
        public EntityId NpcId { get; }
        public long ReturnedTick { get; internal set; } = -1;
        public GridPos ReturnedCell { get; internal set; } = GridPos.Invalid;
        public bool Returned { get; internal set; }
        public bool Dead { get; internal set; }
    }

    public sealed class HuntResultState
    {
        internal HuntResultState(int id, EntityId target, long tick, bool success, string reason, int[] items, List<EntityId> participants, List<long> returnTicks, List<GridPos> returnCells)
        { Id = id; TargetId = target; Tick = tick; Success = success; Reason = reason; Items = Array.AsReadOnly((int[])items.Clone()); Participants = participants.AsReadOnly(); ReturnTicks = returnTicks.AsReadOnly(); ReturnCells = returnCells.AsReadOnly(); }
        public int Id { get; }
        public EntityId TargetId { get; }
        public long Tick { get; }
        public bool Success { get; }
        public string Reason { get; }
        public IReadOnlyList<int> Items { get; }
        public IReadOnlyList<EntityId> Participants { get; }
        public IReadOnlyList<long> ReturnTicks { get; }
        public IReadOnlyList<GridPos> ReturnCells { get; }
    }

    public sealed class HuntTaskState
    {
        internal HuntTaskState(int id, EntityId target, EntityId leader, long tick)
        {
            Id = id;
            TargetId = target;
            LeaderId = leader;
            StartedTick = tick;
            DeadlineTick = tick + HuntingRules.GatherTicks;
        }

        internal readonly List<HuntReturnState> Returnees = new List<HuntReturnState>();
        internal readonly int[] Delivered = new int[(int)ItemKind.Count];
        public IReadOnlyList<HuntReturnState> Returns => Returnees.AsReadOnly();
        public IReadOnlyList<int> DeliveredItems => Array.AsReadOnly(Delivered);
        internal readonly List<HuntMemberState> MutableMembers = new List<HuntMemberState>();
        public int Id
        {
            get;
        }

        public EntityId TargetId
        {
            get;
        }

        public EntityId LeaderId
        {
            get;
            internal set;
        }

        public HuntStage Stage
        {
            get;
            internal set;
        }

        public long StartedTick
        {
            get;
        }

        public long DeadlineTick
        {
            get;
            internal set;
        }

        // -1 means the team is approaching a marked location and has not acquired sight yet.
        public long LastSeenTick
        {
            get;
            internal set;
        }

        public GridPos LastSeenPosition
        {
            get;
            internal set;
        }

        public GridPos PreviousSeenPosition
        {
            get;
            internal set;
        }

        public GridPos RallyCell
        {
            get;
            internal set;
        }

        public bool PlayerRequested
        {
            get;
            internal set;
        }

        public string Reason
        {
            get;
            internal set;
        }

        = "";
        public IReadOnlyList<HuntMemberState> Members => MutableMembers.AsReadOnly();
        public HuntMemberState Member(EntityId id) => MutableMembers.Find(m => m.NpcId == id);
    }

    public sealed class HuntingState
    {
        internal readonly List<HuntResultState> MutableResults = new List<HuntResultState>();
        public IReadOnlyList<HuntResultState> RecentResults => MutableResults.AsReadOnly();
        internal readonly List<HuntTaskState> MutableTasks = new List<HuntTaskState>();
        internal readonly SortedDictionary<int, long> Cooldowns = new SortedDictionary<int, long>();
        public int NextTaskId
        {
            get;
            internal set;
        }

        = 1;
        public IReadOnlyList<HuntTaskState> Tasks => MutableTasks.AsReadOnly();
        public HuntTaskState Find(int id) => MutableTasks.Find(t => t.Id == id);
        public HuntTaskState ForTarget(EntityId id) => MutableTasks.Find(t => t.TargetId == id);
        public HuntTaskState ForNpc(EntityId id) => MutableTasks.Find(t => t.Member(id) != null);
        public HuntTaskState ForReturn(EntityId id) => MutableTasks.Find(t => t.Returnees.Exists(r => r.NpcId == id && !r.Returned && !r.Dead));
        public long RetryAt(EntityId id) => Cooldowns.TryGetValue(id.Value, out long tick) ? tick : 0;
    }
}
