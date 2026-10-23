using System;

namespace MyAiWorld.Sim.World
{
    public sealed class WorldConfigData
    {
        private readonly int[] _speedMultipliers;

        public WorldConfigData(
            int seed,
            int width,
            int height,
            int npcCount,
            long startTick,
            int ticksPerSecondAt1x,
            int[] speedMultipliers,
            int challengeDays = 0)
        {
            if (width < 16 || height < 16)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The world must be at least 16x16.");
            }

            if (npcCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(npcCount));
            }

            if (startTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startTick));
            }

            if (challengeDays < 0 || startTick > long.MaxValue - (long)challengeDays * GameCalendar.TicksPerDay)
            {
                throw new ArgumentOutOfRangeException(nameof(challengeDays), "The challenge deadline must fit in the simulation clock.");
            }

            if (challengeDays > 0 && npcCount == 0)
            {
                throw new ArgumentException("A survival challenge requires at least one initial NPC.", nameof(npcCount));
            }

            if (ticksPerSecondAt1x <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecondAt1x));
            }

            if (speedMultipliers == null || speedMultipliers.Length == 0)
            {
                throw new ArgumentException("At least one speed multiplier is required.", nameof(speedMultipliers));
            }

            Seed = seed;
            Width = width;
            Height = height;
            NpcCount = npcCount;
            StartTick = startTick;
            ChallengeDays = challengeDays;
            TicksPerSecondAt1x = ticksPerSecondAt1x;
            _speedMultipliers = (int[])speedMultipliers.Clone();
            for (int i = 0; i < _speedMultipliers.Length; i++)
            {
                if (_speedMultipliers[i] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(speedMultipliers), "Speed multipliers must be positive.");
                }
            }
        }

        public int Seed { get; }
        public int Width { get; }
        public int Height { get; }
        public int NpcCount { get; }
        public long StartTick { get; }
        public int ChallengeDays { get; }
        public int TicksPerSecondAt1x { get; }
        public int[] SpeedMultipliers => (int[])_speedMultipliers.Clone();

        public static WorldConfigData CreateDefault()
        {
            return new WorldConfigData(
                20261020,
                64,
                64,
                20,
                GameCalendar.WinterStartTick,
                2,
                new[] { 1, 2, 4, 8 },
                GameCalendar.DefaultChallengeDays);
        }

        public static WorldConfigData CreateObservationDefault()
        {
            return new WorldConfigData(
                20261020,
                64,
                64,
                20,
                GameCalendar.StartTick,
                2,
                new[] { 1, 2, 4, 8 });
        }
    }
}
