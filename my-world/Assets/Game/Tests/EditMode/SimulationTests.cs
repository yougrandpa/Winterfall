using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class SimulationTests
    {
        private static WorldConfigData TestConfig(int seed = 12345, int npcCount = 8)
        {
            return new WorldConfigData(
                seed,
                32,
                32,
                npcCount,
                GameCalendar.StartTick,
                2,
                new[] { 1, 2, 4, 8 });
        }

        [Test]
        public void Calendar_CrossesMidnightAndSeasons()
        {
            GameDateTime start = GameCalendar.FromTick(GameCalendar.StartTick);
            Assert.That(start.Year, Is.EqualTo(1));
            Assert.That(start.Season, Is.EqualTo(Season.Spring));
            Assert.That(start.DayOfSeason, Is.EqualTo(1));
            Assert.That(start.ClockText, Is.EqualTo("06:00"));

            GameDateTime midnight = GameCalendar.FromTick(GameCalendar.TicksPerDay);
            Assert.That(midnight.DayOfSeason, Is.EqualTo(2));
            Assert.That(midnight.ClockText, Is.EqualTo("00:00"));

            GameDateTime summer = GameCalendar.FromTick(GameCalendar.TicksPerDay * GameCalendar.DaysPerSeason);
            Assert.That(summer.Season, Is.EqualTo(Season.Summer));
            Assert.That(summer.DayOfSeason, Is.EqualTo(1));

            GameDateTime nextYear = GameCalendar.FromTick(GameCalendar.TicksPerDay * GameCalendar.DaysPerYear);
            Assert.That(nextYear.Year, Is.EqualTo(2));
            Assert.That(nextYear.Season, Is.EqualTo(Season.Spring));
        }

        [Test]
        public void DeterministicRng_SameSeed_SameSequence()
        {
            var first = new DeterministicRng(42UL);
            var second = new DeterministicRng(42UL);
            var different = new DeterministicRng(43UL);

            bool foundDifference = false;
            for (int i = 0; i < 64; i++)
            {
                uint a = first.NextUInt();
                uint b = second.NextUInt();
                Assert.That(a, Is.EqualTo(b));
                if (a != different.NextUInt())
                {
                    foundDifference = true;
                }
            }

            Assert.That(foundDifference, Is.True);
        }

        [Test]
        public void Pathfinder_AvoidsBlockedTiles()
        {
            var map = new WorldMap(7, 7);
            for (int y = 0; y < 6; y++)
            {
                map.SetTile(new GridPos(3, y), TileType.Rock);
            }

            var finder = new GridPathfinder();
            PathResult result = finder.FindPath(map, new GridPos(0, 0), new GridPos(6, 0));

            Assert.That(result.Found, Is.True);
            Assert.That(result.Path.Count, Is.GreaterThan(6));
            foreach (GridPos step in result.Path)
            {
                Assert.That(map.IsWalkable(step), Is.True);
            }

            Assert.That(result.Path[result.Path.Count - 1], Is.EqualTo(new GridPos(6, 0)));
        }

        [Test]
        public void Pathfinder_ReportsUnreachableGoal()
        {
            var map = new WorldMap(5, 5);
            map.SetTile(new GridPos(2, 0), TileType.Rock);
            map.SetTile(new GridPos(2, 1), TileType.Rock);
            map.SetTile(new GridPos(2, 2), TileType.Rock);
            map.SetTile(new GridPos(2, 3), TileType.Rock);
            map.SetTile(new GridPos(2, 4), TileType.Rock);

            var finder = new GridPathfinder();
            PathResult result = finder.FindPath(map, new GridPos(0, 0), new GridPos(4, 0));
            Assert.That(result.Found, Is.False);
        }

        [Test]
        public void WorldGeneration_IsDeterministicAndReachable()
        {
            WorldState first = WorldFactory.Create(TestConfig());
            WorldState second = WorldFactory.Create(TestConfig());

            Assert.That(first.Entities.Npcs.Count, Is.EqualTo(8));
            Assert.That(second.Entities.Npcs.Count, Is.EqualTo(8));

            for (int i = 0; i < first.Entities.Npcs.Count; i++)
            {
                NpcState a = first.Entities.Npcs[i];
                NpcState b = second.Entities.Npcs[i];
                Assert.That(a.Id, Is.EqualTo(b.Id));
                Assert.That(a.Position, Is.EqualTo(b.Position));
                Assert.That(first.Map.IsWalkable(a.Position), Is.True);
                Assert.That(first.Map.IsReachable(first.Center, a.Position), Is.True);
            }

            for (int y = 0; y < first.Map.Height; y++)
            {
                for (int x = 0; x < first.Map.Width; x++)
                {
                    Assert.That(first.Map.GetTile(x, y), Is.EqualTo(second.Map.GetTile(x, y)));
                }
            }
        }

        [Test]
        public void Simulation_SameSeedAndCommands_ProducesSameHash()
        {
            WorldState first = WorldFactory.Create(TestConfig(777, 10));
            WorldState second = WorldFactory.Create(TestConfig(777, 10));
            var engineA = new SimulationEngine();
            var engineB = new SimulationEngine();
            int appliedCommands = 0;
            int rejectedCommands = 0;

            for (int i = 0; i < 400; i++)
            {
                IReadOnlyList<IWorldCommand> commands = null;
                if (i == 10 || i == 150)
                {
                    commands = new IWorldCommand[] { new SpawnNpcCommand() };
                }
                else if (i == 80)
                {
                    commands = new IWorldCommand[] { new SpawnNpcCommand(new GridPos(0, 0)) };
                }

                TickResult a = engineA.Tick(first, commands);
                TickResult b = engineB.Tick(second, commands);
                Assert.That(a.StateHash, Is.EqualTo(b.StateHash), "Hash diverged at tick " + i);
                Assert.That(a.AppliedCommands, Is.EqualTo(b.AppliedCommands));
                appliedCommands += a.AppliedCommands;
                rejectedCommands += a.Events.FindAll(evt => evt.Kind == SimEventKind.CommandRejected).Count;
            }

            Assert.That(appliedCommands, Is.EqualTo(2), "Replay must exercise successful commands.");
            Assert.That(rejectedCommands, Is.EqualTo(1), "Replay must exercise a rejected command.");
            Assert.That(first.Entities.Npcs.Count, Is.EqualTo(12));
            Assert.That(StateHasher.Compute(first), Is.EqualTo(StateHasher.Compute(second)));
        }

        [Test]
        public void Command_SpawnsNpcOnlyAtTickBoundary()
        {
            WorldState world = WorldFactory.Create(TestConfig(99, 3));
            int before = world.Entities.Npcs.Count;
            var engine = new SimulationEngine();
            var command = new SpawnNpcCommand();

            TickResult result = engine.Tick(world, new List<IWorldCommand> { command });

            Assert.That(result.AppliedCommands, Is.EqualTo(1));
            Assert.That(world.Entities.Npcs.Count, Is.EqualTo(before + 1));
        }

        [Test]
        public void Command_RejectsBlockedSpawnCell()
        {
            WorldState world = WorldFactory.Create(TestConfig(5, 2));
            var engine = new SimulationEngine();
            var command = new SpawnNpcCommand(new GridPos(0, 0));

            TickResult result = engine.Tick(world, new List<IWorldCommand> { command });

            Assert.That(result.AppliedCommands, Is.EqualTo(0));
            Assert.That(result.Events.Exists(evt => evt.Kind == SimEventKind.CommandRejected), Is.True);
        }

        [Test]
        public void Simulation_LongRunKeepsEntitiesOnWalkableCells()
        {
            WorldConfigData config = new WorldConfigData(
                24680,
                64,
                64,
                20,
                GameCalendar.StartTick,
                2,
                new[] { 1, 2, 4, 8 });
            WorldState world = WorldFactory.Create(config);
            var engine = new SimulationEngine();
            int actionEvents = 0;

            for (int i = 0; i < 3000; i++)
            {
                TickResult result = engine.Tick(world, null);
                actionEvents += result.Events.FindAll(evt => evt.Kind == SimEventKind.NpcActionStarted).Count;
            }

            Assert.That(world.Entities.Npcs.Count, Is.EqualTo(20));
            Assert.That(actionEvents, Is.GreaterThan(0));
            for (int i = 0; i < world.Entities.Npcs.Count; i++)
            {
                NpcState npc = world.Entities.Npcs[i];
                Assert.That(world.Map.IsWalkable(npc.Position), Is.True);
            }
        }

    }
}
