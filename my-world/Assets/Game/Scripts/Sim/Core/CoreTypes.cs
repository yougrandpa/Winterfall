using System;

namespace MyAiWorld.Sim.Core
{
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public static readonly EntityId None = new EntityId(0);

        public EntityId(int value)
        {
            Value = value;
        }

        public int Value { get; }
        public bool IsNone => Value == 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "E" + Value;

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }

    public readonly struct GridPos : IEquatable<GridPos>
    {
        public static readonly GridPos Invalid = new GridPos(-1, -1);
        public static readonly GridPos Zero = new GridPos(0, 0);

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
        public bool IsValid => X >= 0 && Y >= 0;

        public int ManhattanDistance(GridPos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public GridPos Offset(int dx, int dy) => new GridPos(X + dx, Y + dy);

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPos other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public override string ToString() => "(" + X + "," + Y + ")";

        public static bool operator ==(GridPos left, GridPos right) => left.Equals(right);
        public static bool operator !=(GridPos left, GridPos right) => !left.Equals(right);
    }

    public struct DeterministicRng
    {
        private const ulong FallbackSeed = 0x9E3779B97F4A7C15UL;

        public ulong State;

        public DeterministicRng(ulong seed)
        {
            State = seed == 0UL ? FallbackSeed : seed;
        }

        public uint NextUInt()
        {
            ulong x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            unchecked
            {
                return (uint)((x * 2685821657736338717UL) >> 32);
            }
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public bool Chance(int percent)
        {
            if (percent <= 0)
            {
                return false;
            }

            if (percent >= 100)
            {
                return true;
            }

            return NextInt(0, 100) < percent;
        }

        public static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdUL;
                value ^= value >> 33;
                value *= 0xc4ceb9fe1a85ec53UL;
                value ^= value >> 33;
                return value == 0UL ? FallbackSeed : value;
            }
        }

        public DeterministicRng Fork(ulong salt)
        {
            return new DeterministicRng(Mix(State ^ Mix(salt)));
        }
    }

    public enum SimEventKind
    {
        NpcDowned, NpcRescued, NpcDied,
        WorldInitialized,
        AnimalBorn,
        AnimalDied,
        AnimalHunted,
        HunterInjured,
        TradeFilled,
        TradeRejected,
        NpcSpawned,
        NpcDecision,
        NpcActionStarted,
        NpcActionCompleted,
        NpcPathFailed,
        CommandRejected,
        DayStarted,
        SeasonStarted,
        PlanStarted,
        PlanFailed,
        PlanInterrupted,
        ResourceGathered,
        CropPlanted,
        CropWatered,
        CropHarvested,
        NeedCritical,
        Greeting,
        FoodGift,
        CampaignEnded
    }

    public sealed class SimEvent
    {
        public SimEvent(SimEventKind kind, long tick, EntityId source, string message)
        {
            Kind = kind;
            Tick = tick;
            Source = source;
            Message = message;
        }

        public long Sequence { get; set; }
        public SimEventKind Kind { get; }
        public long Tick { get; }
        public EntityId Source { get; }
        public string Message { get; }

        public override string ToString()
        {
            return "[" + Tick + "] " + Kind + ": " + Message;
        }
    }
}
