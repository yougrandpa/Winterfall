using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.App;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class CampaignTests
    {
        private static WorldConfigData Config(int count = 5, int days = GameCalendar.DefaultChallengeDays,
            long startTick = GameCalendar.WinterStartTick)
        {
            return new WorldConfigData(42, 32, 32, count, startTick, 2, new[] { 1, 2, 4, 8 }, days);
        }

        private static WorldState CreateWorld(int count = 5, int days = GameCalendar.DefaultChallengeDays,
            bool initialize = true)
        {
            var world = new WorldState(Config(count, days), new WorldMap(32, 32), new DeterministicRng(42));
            for (int i = 0; i < count; i++)
            {
                AddNpc(world);
            }
            world.Materials.Initialize(world);
            if (initialize)
            {
                world.Campaign.Initialize(world);
            }
            return world;
        }

        private static NpcState AddNpc(WorldState world)
        {
            var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("Test", "Builder", 25),
                new Personality(), new GridPos(16, 16), 42);
            npc.Action.SetIdle("Controlled campaign test", long.MaxValue);
            world.Entities.AddNpc(npc);
            world.Market.IssuedCurrency += npc.Coins;
            return npc;
        }

        private static byte[] CaptureCampaign(CampaignState campaign)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                campaign.Write(writer);
                return stream.ToArray();
            }
        }

        private static void RestoreCampaign(byte[] bytes, CampaignState campaign, WorldState world)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                campaign.Read(reader, world);
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            }
        }

        private static void AssertResultEqual(CampaignResult expected, CampaignResult actual)
        {
            Assert.That(actual.EndedTick, Is.EqualTo(expected.EndedTick));
            Assert.That(actual.Grade, Is.EqualTo(expected.Grade));
            Assert.That(actual.InitialCount, Is.EqualTo(expected.InitialCount));
            Assert.That(actual.Survivors, Is.EqualTo(expected.Survivors));
            Assert.That(actual.Active, Is.EqualTo(expected.Active));
            Assert.That(actual.Downed, Is.EqualTo(expected.Downed));
            Assert.That(actual.Recovering, Is.EqualTo(expected.Recovering));
            Assert.That(actual.FoodReserve, Is.EqualTo(expected.FoodReserve));
            Assert.That(actual.WaterReserve, Is.EqualTo(expected.WaterReserve));
            Assert.That(actual.FuelReserve, Is.EqualTo(expected.FuelReserve));
            Assert.That(actual.Assisted, Is.EqualTo(expected.Assisted));
            Assert.That(actual.Reason, Is.EqualTo(expected.Reason));
        }

        [Test]
        public void Defaults_SeparateWinterChallengeFromSpringObservation()
        {
            WorldConfigData challenge = WorldConfigData.CreateDefault();
            WorldConfigData observation = WorldConfigData.CreateObservationDefault();
            var campaign = new CampaignState(challenge);
            Assert.That(challenge.StartTick, Is.EqualTo(121320));
            Assert.That(challenge.ChallengeDays, Is.EqualTo(14));
            Assert.That(campaign.DeadlineTick, Is.EqualTo(141480));
            Assert.That(campaign.DeadlineTick - campaign.StartTick, Is.EqualTo(20160));
            Assert.That(campaign.Phase, Is.EqualTo(CampaignPhase.SurvivalActive));
            GameDateTime start = GameCalendar.FromTick(challenge.StartTick);
            Assert.That(start.Season, Is.EqualTo(Season.Winter));
            Assert.That(start.DayOfSeason, Is.EqualTo(1));
            Assert.That(start.ClockText, Is.EqualTo("06:00"));
            Assert.That(observation.StartTick, Is.EqualTo(GameCalendar.StartTick));
            Assert.That(observation.StartTick, Is.EqualTo(360));
            Assert.That(observation.ChallengeDays, Is.Zero);
            Assert.That(new CampaignState(observation).Phase, Is.EqualTo(CampaignPhase.Observation));
            Assert.That(new CampaignState(observation).DeadlineTick, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void SevenArgumentConfig_IsExplicitlyUnlimitedWithoutChangingSurvivalRules()
        {
            var config = new WorldConfigData(42, 32, 32, 0, GameCalendar.StartTick, 2, new[] { 1 });
            Assert.That(config.ChallengeDays, Is.Zero);
            var world = new WorldState(config, new WorldMap(32, 32), new DeterministicRng(42));
            world.Campaign.Initialize(world);
            world.Tick += GameCalendar.DaysPerYear * GameCalendar.TicksPerDay;
            Assert.That(world.Campaign.Evaluate(world), Is.False);
            Assert.That(world.Campaign.InitialNpcs.Count, Is.Zero);
            Assert.That(world.Campaign.CanAdvance, Is.True);
            Assert.That(world.Campaign.Result, Is.Null);
            Assert.DoesNotThrow(() => world.Campaign.Validate(world));
        }

        [Test]
        public void Config_RejectsEmptyChallengeNegativeDurationAndClockOverflow()
        {
            Assert.Throws<ArgumentException>(() => Config(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(1, 14, long.MaxValue - 100));
            Assert.DoesNotThrow(() => Config(0, 0));
        }

        [Test]
        public void ConfigAsset_UsesSerializedStartAndDurationAndRejectsEmptyChallenge()
        {
            var asset = ScriptableObject.CreateInstance<WorldConfigAsset>();
            try
            {
                Assert.That(asset.ToData().StartTick, Is.EqualTo(GameCalendar.WinterStartTick));
                Assert.That(asset.ToData().ChallengeDays, Is.EqualTo(GameCalendar.DefaultChallengeDays));
                asset.startTick = GameCalendar.StartTick + 42;
                asset.challengeDays = 0;
                asset.npcCount = 0;
                Assert.That(asset.ToData().StartTick, Is.EqualTo(asset.startTick));
                Assert.That(asset.ToData().ChallengeDays, Is.Zero);
                asset.challengeDays = 14;
                Assert.Throws<ArgumentException>(() => asset.ToData());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Factory_InitializesCampaignOnlyAfterAllInitialNpcsExist()
        {
            WorldState world = WorldFactory.Create(WorldConfigData.CreateDefault());
            Assert.That(world.Campaign.InitialNpcs.Count, Is.EqualTo(world.Config.NpcCount));
            for (int i = 0; i < world.Config.NpcCount; i++)
            {
                Assert.That(world.Campaign.InitialNpcs[i], Is.EqualTo(world.Entities.Npcs[i].Id));
            }
            Assert.DoesNotThrow(() => world.Campaign.Validate(world));
        }

        [Test]
        public void Initialize_RejectsMissingRosterAndCannotReplaceLockedInitialNpcs()
        {
            var missing = new WorldState(Config(1), new WorldMap(32, 32), new DeterministicRng(42));
            Assert.Throws<InvalidOperationException>(() => missing.Campaign.Initialize(missing));
            var world = CreateWorld(1);
            EntityId original = world.Campaign.InitialNpcs[0];
            NpcState newcomer = AddNpc(world);
            Assert.Throws<InvalidOperationException>(() => world.Campaign.Initialize(world));
            Assert.That(world.Campaign.InitialNpcs.Count, Is.EqualTo(1));
            Assert.That(world.Campaign.InitialNpcs[0], Is.EqualTo(original));
            Assert.That(world.Campaign.InitialNpcs, Has.No.Member(newcomer.Id));
            var mutableView = world.Campaign.InitialNpcs as IList<EntityId>;
            Assert.That(mutableView, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => mutableView[0] = newcomer.Id);
        }

        [TestCase(5, 5, "S")]
        [TestCase(5, 4, "A")]
        [TestCase(5, 3, "B")]
        [TestCase(5, 2, "C")]
        [TestCase(5, 1, "C")]
        [TestCase(5, 0, "F")]
        [TestCase(20, 16, "A")]
        [TestCase(20, 15, "B")]
        [TestCase(20, 10, "B")]
        [TestCase(20, 9, "C")]
        [TestCase(3, 2, "B")]
        public void Settlement_GradesOnlyOriginalSurvivalCount(int initial, int survivors, string grade)
        {
            var world = CreateWorld(initial);
            for (int i = survivors; i < initial; i++)
            {
                world.Entities.Npcs[i].Survival.Life = LifeState.Dead;
            }
            AddNpc(world);
            world.Campaign.Assisted = true;
            world.Tick = world.Campaign.DeadlineTick;
            Assert.That(world.Campaign.Evaluate(world), Is.True);
            Assert.That(world.Campaign.Result.InitialCount, Is.EqualTo(initial));
            Assert.That(world.Campaign.Result.Survivors, Is.EqualTo(survivors));
            Assert.That(world.Campaign.Result.Grade, Is.EqualTo(grade));
            Assert.That(world.Campaign.Result.Assisted, Is.True);
            Assert.That(world.Campaign.CanAdvance, Is.False);
            Assert.DoesNotThrow(() => world.Campaign.Validate(world));
        }

        [Test]
        public void Settlement_DownedAndRecoveringCountAsAlive_AndDoesNotEndBeforeLastMinute()
        {
            var world = CreateWorld(3);
            world.Entities.Npcs[1].Survival.Life = LifeState.Downed;
            world.Entities.Npcs[2].Survival.Life = LifeState.Recovering;
            world.Tick = world.Campaign.DeadlineTick - 1;
            Assert.That(world.Campaign.Evaluate(world), Is.False);
            Assert.That(world.Campaign.Result, Is.Null);
            world.Tick++;
            Assert.That(world.Campaign.Evaluate(world), Is.True);
            CampaignResult result = world.Campaign.Result;
            Assert.That(result.Grade, Is.EqualTo("S"));
            Assert.That(result.Active, Is.EqualTo(1));
            Assert.That(result.Downed, Is.EqualTo(1));
            Assert.That(result.Recovering, Is.EqualTo(1));
            Assert.That(result.EndedTick, Is.EqualTo(141480));
            Assert.That(result.Reason, Is.EqualTo("deadline_reached"));
            Assert.That(world.Campaign.Evaluate(world), Is.False);
            Assert.That(world.Campaign.Result, Is.SameAs(result));
        }

        [Test]
        public void Extinction_EndsEarlyEvenWithLivingDebugNewcomer()
        {
            var world = CreateWorld(2);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Survival.Life = LifeState.Dead;
            }
            AddNpc(world);
            world.Tick++;
            Assert.That(world.LivingCount, Is.EqualTo(1));
            Assert.That(world.Campaign.Evaluate(world), Is.True);
            Assert.That(world.Campaign.Result.Grade, Is.EqualTo("F"));
            Assert.That(world.Campaign.Result.Survivors, Is.Zero);
            Assert.That(world.Campaign.Result.EndedTick, Is.EqualTo(world.Config.StartTick + 1));
            Assert.That(world.Campaign.Result.Reason, Is.EqualTo("all_initial_npcs_dead"));
        }

        [Test]
        public void Reserves_CountMaterializedStoresOnce_ExcludeRawWaterNodesAndUnharvestedCrops()
        {
            var world = CreateWorld(1);
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 2);
            world.Resources.Stockpile.TryAdd(ItemKind.Water, 3);
            world.Resources.Stockpile.TryAdd(ItemKind.Wood, 4);
            world.Resources.Stockpile.TryAdd(ItemKind.RawWater, 80);
            world.Market.Inventory.TryAdd(ItemKind.Food, 5);
            world.Market.Inventory.TryAdd(ItemKind.Water, 6);
            var facility = new FacilityState(1, FacilityKind.Purifier, new GridPos(2, 2), new GridPos(2, 3));
            facility.Inventory.TryAdd(ItemKind.Food, 7);
            facility.Inventory.TryAdd(ItemKind.Water, 8);
            facility.Inventory.TryAdd(ItemKind.Wood, 9);
            world.Survival.MutableFacilities.Add(facility);
            // Aliases must not inflate stored supply.
            world.Survival.MutableFacilities.Add(facility);
            var homeStock = world.Survival.Drop(world.Entities.Npcs[0].Home);
            homeStock.Inventory.TryAdd(ItemKind.Food, 10);
            homeStock.Inventory.TryAdd(ItemKind.Water, 11);
            homeStock.Inventory.TryAdd(ItemKind.Wood, 12);
            world.Entities.Npcs[0].Inventory.TryAdd(ItemKind.Food, 1);
            world.Entities.Npcs[0].Inventory.TryAdd(ItemKind.Water, 2);
            world.Entities.Npcs[0].Inventory.TryAdd(ItemKind.Wood, 3);
            world.Resources.AddNode(new ResourceNodeState(1, new GridPos(3, 3), ItemKind.Food, 100, 1));
            world.Resources.AddNode(new ResourceNodeState(2, new GridPos(4, 3), ItemKind.Water, 100, 1));
            world.Resources.AddNode(new ResourceNodeState(3, new GridPos(5, 3), ItemKind.Wood, 100, 1));
            world.Resources.AddPlot(new FarmPlotState(1, new GridPos(6, 3))
            {
                Crop = CropType.Wheat,
                GrowthTicks = CropDefinition.Wheat.GrowthTicks
            });
            world.Tick = world.Campaign.DeadlineTick;
            world.Campaign.Evaluate(world);
            Assert.That(world.Campaign.Result.FoodReserve, Is.EqualTo(25));
            Assert.That(world.Campaign.Result.WaterReserve, Is.EqualTo(30));
            Assert.That(world.Campaign.Result.FuelReserve, Is.EqualTo(28));
            Assert.That(world.Campaign.Result.Grade, Is.EqualTo("S"));
        }

        [Test]
        public void ContinueObservation_OnlyUnlocksAtCommandBoundary_WithoutChangingTickOrResult()
        {
            var world = CreateWorld(1);
            var command = new ContinueObservationCommand();
            var events = new List<SimEvent>();
            Assert.That(command.Apply(world, new CommandContext(world.Tick, events)), Is.False);
            Assert.That(events[0].Kind, Is.EqualTo(SimEventKind.CommandRejected));
            world.Tick = world.Campaign.DeadlineTick;
            world.Campaign.Evaluate(world);
            CampaignResult result = world.Campaign.Result;
            long tick = world.Tick;
            Assert.That(command.Describe(), Does.Contain("no time advance"));
            Assert.That(command.Apply(world, new CommandContext(world.Tick, events)), Is.True);
            Assert.That(world.Tick, Is.EqualTo(tick));
            Assert.That(world.Campaign.Phase, Is.EqualTo(CampaignPhase.Observation));
            Assert.That(world.Campaign.CanAdvance, Is.True);
            world.Entities.Npcs[0].Survival.Life = LifeState.Dead;
            world.Campaign.Assisted = true;
            world.Tick += GameCalendar.TicksPerDay;
            Assert.That(world.Campaign.Evaluate(world), Is.False);
            Assert.That(world.Campaign.Result, Is.SameAs(result));
            Assert.That(result.Assisted, Is.False);
            Assert.That(command.Apply(world, new CommandContext(world.Tick, events)), Is.False);
            Assert.DoesNotThrow(() => world.Campaign.Validate(world));
        }

        [TestCase(CampaignPhase.SurvivalActive)]
        [TestCase(CampaignPhase.SettlementLocked)]
        [TestCase(CampaignPhase.Observation)]
        public void CampaignWire_RoundTripsEveryChallengePhaseAndAllResultFields(CampaignPhase phase)
        {
            var world = CreateWorld(3);
            world.Campaign.Assisted = true;
            if (phase != CampaignPhase.SurvivalActive)
            {
                world.Entities.Npcs[1].Survival.Life = LifeState.Downed;
                world.Entities.Npcs[2].Survival.Life = LifeState.Recovering;
                world.Resources.Stockpile.TryAdd(ItemKind.Food, 3);
                world.Resources.Stockpile.TryAdd(ItemKind.Water, 4);
                world.Resources.Stockpile.TryAdd(ItemKind.Wood, 5);
                world.Tick = world.Campaign.DeadlineTick;
                world.Campaign.Evaluate(world);
                if (phase == CampaignPhase.Observation)
                {
                    new ContinueObservationCommand().Apply(world, new CommandContext(world.Tick, new List<SimEvent>()));
                    world.Tick += 20;
                    world.Entities.Npcs[0].Survival.Life = LifeState.Dead;
                }
            }
            byte[] bytes = CaptureCampaign(world.Campaign);
            var restored = new CampaignState(world.Config);
            RestoreCampaign(bytes, restored, world);
            Assert.That(restored.Phase, Is.EqualTo(phase));
            Assert.That(restored.StartTick, Is.EqualTo(world.Campaign.StartTick));
            Assert.That(restored.DeadlineTick, Is.EqualTo(world.Campaign.DeadlineTick));
            Assert.That(restored.InitialNpcs, Is.EqualTo(world.Campaign.InitialNpcs));
            Assert.That(restored.Assisted, Is.True);
            Assert.That(restored.CanAdvance, Is.EqualTo(world.Campaign.CanAdvance));
            if (phase == CampaignPhase.SurvivalActive)
            {
                Assert.That(restored.Result, Is.Null);
            }
            else
            {
                AssertResultEqual(world.Campaign.Result, restored.Result);
            }
            Assert.That(CaptureCampaign(restored), Is.EqualTo(bytes));
        }

        [Test]
        public void CampaignWire_EmptyObservationAndEarlyExtinctionRoundTrip()
        {
            var empty = CreateWorld(0, 0);
            var restored = new CampaignState(empty.Config);
            RestoreCampaign(CaptureCampaign(empty.Campaign), restored, empty);
            Assert.That(restored.Phase, Is.EqualTo(CampaignPhase.Observation));
            Assert.That(restored.Result, Is.Null);
            var extinct = CreateWorld(1);
            extinct.Entities.Npcs[0].Survival.Life = LifeState.Dead;
            extinct.Tick++;
            extinct.Campaign.Evaluate(extinct);
            restored = new CampaignState(extinct.Config);
            RestoreCampaign(CaptureCampaign(extinct.Campaign), restored, extinct);
            AssertResultEqual(extinct.Campaign.Result, restored.Result);
        }

        [Test]
        public void CampaignWire_RejectsMismatchedDeadlineAndLeavesExistingStateUntouched()
        {
            var world = CreateWorld(1);
            byte[] bytes = CaptureCampaign(world.Campaign);
            byte[] corrupt = (byte[])bytes.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes(world.Campaign.DeadlineTick + 1), 0, corrupt, sizeof(long), sizeof(long));
            Assert.Throws<InvalidDataException>(() => RestoreCampaign(corrupt, world.Campaign, world));
            Assert.That(CaptureCampaign(world.Campaign), Is.EqualTo(bytes));
            var other = CreateWorld(1, 7);
            Assert.Throws<InvalidDataException>(() => RestoreCampaign(bytes, other.Campaign, other));
        }

        [TestCase("ended_tick")]
        [TestCase("grade")]
        [TestCase("initial_count")]
        [TestCase("survivors")]
        [TestCase("life_counts")]
        [TestCase("reserve")]
        [TestCase("assisted")]
        [TestCase("reason")]
        public void CampaignWire_RejectsInvalidResultFields(string corruption)
        {
            var world = CreateWorld(1);
            world.Campaign.Assisted = true;
            world.Tick = world.Campaign.DeadlineTick;
            world.Campaign.Evaluate(world);
            byte[] bytes = CaptureCampaign(world.Campaign);
            using (var stream = new MemoryStream(bytes, true))
            using (var reader = new BinaryReader(stream))
            using (var writer = new BinaryWriter(stream))
            {
                reader.ReadInt64();
                reader.ReadInt64();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadBoolean();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    reader.ReadInt32();
                }
                Assert.That(reader.ReadBoolean(), Is.True);
                long endedOffset = stream.Position;
                reader.ReadInt64();
                long gradeOffset = stream.Position;
                reader.ReadString();
                long countOffset = stream.Position;
                for (int i = 0; i < 5; i++)
                {
                    reader.ReadInt32();
                }
                long reserveOffset = stream.Position;
                for (int i = 0; i < 3; i++)
                {
                    reader.ReadInt64();
                }
                long assistedOffset = stream.Position;
                reader.ReadBoolean();
                long reasonOffset = stream.Position;
                switch (corruption)
                {
                    case "ended_tick": stream.Position = endedOffset; writer.Write(world.Tick + 1); break;
                    case "grade": stream.Position = gradeOffset; writer.Write("X"); break;
                    case "initial_count": stream.Position = countOffset; writer.Write(2); break;
                    case "survivors": stream.Position = countOffset + sizeof(int); writer.Write(2); break;
                    case "life_counts": stream.Position = countOffset + 2 * sizeof(int); writer.Write(2); break;
                    case "reserve": stream.Position = reserveOffset; writer.Write(-1L); break;
                    case "assisted": stream.Position = assistedOffset; writer.Write(false); break;
                    case "reason": stream.Position = reasonOffset; writer.Write("deadline_changed"); break;
                }
            }
            Assert.Throws<InvalidDataException>(() => RestoreCampaign(bytes, new CampaignState(world.Config), world));
        }

        [TestCase("duplicate")]
        [TestCase("missing")]
        [TestCase("debug_newcomer")]
        [TestCase("negative_count")]
        public void CampaignWire_RejectsInvalidInitialRoster(string corruption)
        {
            var world = CreateWorld(2);
            NpcState newcomer = AddNpc(world);
            byte[] bytes = CaptureCampaign(world.Campaign);
            int rosterOffset = 2 * sizeof(long) + 3 * sizeof(int) + sizeof(byte);
            int offset = corruption == "negative_count" ? rosterOffset - sizeof(int) : rosterOffset + sizeof(int);
            int value = corruption == "duplicate" ? world.Campaign.InitialNpcs[0].Value :
                corruption == "debug_newcomer" ? newcomer.Id.Value : -1;
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, sizeof(int));
            Assert.Throws<InvalidDataException>(() => RestoreCampaign(bytes, new CampaignState(world.Config), world));
        }

        [Test]
        public void CampaignValidation_RejectsInvalidPhaseMissingResultAndLateActiveClock()
        {
            var world = CreateWorld(1);
            world.Campaign.Phase = (CampaignPhase)99;
            Assert.Throws<InvalidDataException>(() => world.Campaign.Validate(world));
            world.Campaign.Phase = CampaignPhase.SettlementLocked;
            Assert.Throws<InvalidDataException>(() => world.Campaign.Validate(world));
            world.Campaign.Phase = CampaignPhase.Observation;
            Assert.Throws<InvalidDataException>(() => world.Campaign.Validate(world));
            world.Campaign.Phase = CampaignPhase.SurvivalActive;
            world.Tick = world.Campaign.DeadlineTick;
            Assert.Throws<InvalidDataException>(() => world.Campaign.Validate(world));
            world.Tick++;
            Assert.Throws<InvalidOperationException>(() => world.Campaign.Evaluate(world));
        }

        [Test]
        public void Engine_FourteenDays_PerformsExactly20160UpdatesBeforeLockAndContinuationIsMinuteFree()
        {
            var world = CreateWorld(1);
            world.Materials.Initialize(world);
            var engine = new SimulationEngine();
            for (int i = 0; i < 20160; i++)
            {
                // This controlled clock test isolates the tick gate, not autonomous survival balance.
                NpcState npc = world.Entities.Npcs[0];
                npc.Needs.Hunger = NeedsState.MaxValue;
                npc.Needs.Thirst = NeedsState.MaxValue;
                npc.Needs.Fatigue = NeedsState.MaxValue;
                npc.Needs.Health = NeedsState.MaxValue;
                npc.Survival.BodyTemperature = 3700;
                npc.Survival.Wetness = 0;
                Assert.That(world.Campaign.CanAdvance, Is.True, "Update " + i);
                engine.Tick(world, null);
                Assert.That(world.Tick, Is.EqualTo(GameCalendar.WinterStartTick + i + 1));
            }
            Assert.That(world.Tick, Is.EqualTo(141480));
            Assert.That(world.Campaign.Phase, Is.EqualTo(CampaignPhase.SettlementLocked));
            CampaignResult result = world.Campaign.Result;
            ulong lockedHash = StateHasher.Compute(world);
            Assert.That(engine.Tick(world, null).StateHash, Is.EqualTo(lockedHash));
            Assert.That(world.Tick, Is.EqualTo(result.EndedTick));
            engine.Tick(world, new IWorldCommand[] { new ContinueObservationCommand() });
            Assert.That(world.Tick, Is.EqualTo(result.EndedTick));
            Assert.That(world.Campaign.Phase, Is.EqualTo(CampaignPhase.Observation));
            engine.Tick(world, null);
            Assert.That(world.Tick, Is.EqualTo(result.EndedTick + 1));
            Assert.That(world.Campaign.Result, Is.SameAs(result));
        }
    }
}
