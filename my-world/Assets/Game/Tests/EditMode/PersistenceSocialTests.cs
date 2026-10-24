using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MyAiWorld.App;
using MyAiWorld.App.Observability;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class PersistenceSocialTests
    {
        private static WorldState Create(int seed = 20261020)
        {
            List<SimEvent> events;
            return WorldFactory.Create(new WorldConfigData(seed, 64, 64, 20, GameCalendar.StartTick, 2, new[] { 1, 2, 4, 8 }), out events);
        }

        [TestCase(20261020)]
        [TestCase(42)]
        [TestCase(1)]
        [TestCase(12345)]
        public void Snapshot_MidActionAndReservations_ContinueWithIdenticalHashes(int seed)
        {
            WorldState world = Create(seed);
            var engine = new SimulationEngine();
            bool moving = false, working = false, reserved = false, social = false;
            for (int sample = 0; sample < 8; sample++)
            {
                for (int i = 0; i < 137; i++) { engine.Tick(world, null); }
                moving |= world.Entities.Npcs.Any(n => n.Action.Type == NpcActionType.Move && n.Action.PathIndex > 0);
                working |= world.Entities.Npcs.Any(n => n.Plan.StepStarted && n.Action.Type != NpcActionType.Move);
                reserved |= world.Resources.Reservations.Count > 0;
                social |= world.Social.Greetings > 0;
                WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
                Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(world)));
                for (int i = 0; i < 211; i++)
                {
                    Assert.That(engine.Tick(restored, null).StateHash, Is.EqualTo(engine.Tick(world, null).StateHash), "Seed " + seed + " sample " + sample + " tick " + i);
                }
            }
            Assert.That(moving && working && reserved && social, Is.True, "Exercise movement, timed work, reservations and social state.");
        }

        [Test]
        public void Snapshot_LegacyVersions_AreRejectedWithoutMigration()
        {
            byte[] bytes = WorldSnapshot.Capture(Create());
            foreach (byte version in new byte[] { 1, 2 })
            { bytes[0] = version; Assert.Throws<InvalidDataException>(() => WorldSnapshot.Restore(bytes)); }
            Assert.Throws<InvalidDataException>(() => WorldSnapshot.Capture(Create(), 1));
        }

        [Test]
        public void Snapshot_TruncatedOrFutureData_IsRejected()
        {
            byte[] bytes = WorldSnapshot.Capture(Create());
            Assert.Throws<EndOfStreamException>(() => WorldSnapshot.Restore(bytes.Take(bytes.Length - 5).ToArray()));
            bytes[0] = 99;
            Assert.Throws<InvalidDataException>(() => WorldSnapshot.Restore(bytes));
        }

        [Test]
        public void Social_ReceivedHelp_ChangesTrustAndActualFutureHelpTarget()
        {
            WorldState world = SocialWorld();
            NpcState a = world.Entities.Npcs[0], b = world.Entities.Npcs[1], c = world.Entities.Npcs[2];
            a.Inventory.TryAdd(ItemKind.Food, 3); b.Needs.Hunger = 5500;
            world.Materials.Initialize(world);
            a.Action.SetIdle("ready", world.Tick);
            var engine = new SimulationEngine();
            for (int i = 0; i < 5; i++) { engine.Tick(world, null); }
            Assert.That(world.Social.Gifts, Is.EqualTo(1));
            Assert.That(b.Social.TrustIn(a.Id), Is.EqualTo(12));
            Assert.That(a.Social.TrustIn(b.Id), Is.Zero, "Trust is directional.");
            long firstGift = b.Social.Memories.First(evt => evt.Kind == SocialKind.FoodGift).Sequence;
            // Give B a surplus through a conserved transfer, then make A and C equally needy.
            b.Inventory.TryRemove(ItemKind.Food, 1); world.Resources.Stockpile.TryAdd(ItemKind.Food, 1);
            a.Inventory.TryRemove(ItemKind.Food, 2); b.Inventory.TryAdd(ItemKind.Food, 2);
            a.Needs.Hunger = c.Needs.Hunger = 5500; b.Needs.Hunger = 9000;
            a.Action.SetIdle("wait", world.Tick + 10000); b.Action.SetIdle("ready", world.Tick);
            Assert.That(SocialSystem.ChoosePartner(world, b, SocialKind.FoodGift).Id, Is.EqualTo(a.Id));
            for (int i = 0; i < 5; i++) { engine.Tick(world, null); }
            SocialEvent returned = world.Social.Timeline.Last(evt => evt.Kind == SocialKind.FoodGift);
            Assert.That(returned.Actor, Is.EqualTo(b.Id)); Assert.That(returned.Target, Is.EqualTo(a.Id));
            Assert.That(returned.Reason, Is.EqualTo("reciprocate_help")); Assert.That(returned.CauseSequence, Is.EqualTo(firstGift));
            Assert.That(a.Inventory.Food, Is.EqualTo(1)); Assert.That(c.Inventory.Food, Is.Zero);
            Assert.That(world.Materials.Error(world, ItemKind.Food), Is.Zero);
            Assert.That(a.Social.TrustIn(b.Id), Is.EqualTo(12));
        }

        [TestCase("full_bag")]
        [TestCase("no_surplus")]
        [TestCase("critical_need")]
        [TestCase("distance")]
        public void Social_InvalidGift_IsAtomicAndDoesNotCreateMemory(string failure)
        {
            WorldState world = SocialWorld(); NpcState a = world.Entities.Npcs[0], b = world.Entities.Npcs[1];
            a.Inventory.TryAdd(ItemKind.Food, 2); b.Needs.Hunger = 5500;
            if (failure == "full_bag") { b.Inventory.TryAdd(ItemKind.Water, 12); }
            if (failure == "no_surplus") { a.Inventory.TryRemove(ItemKind.Food, 1); }
            if (failure == "critical_need") { a.Needs.Thirst = 1000; }
            if (failure == "distance") { b.Position = new GridPos(12, 12); }
            ulong before = StateHasher.Compute(world);
            Assert.That(SocialSystem.Interact(world, a, b, SocialKind.FoodGift, new List<SimEvent>()), Is.False);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(before));
        }

        [Test]
        public void Social_UrgentThirst_InterruptsSocialPlanAndReleasesClaims()
        {
            WorldState world = SocialWorld(); NpcState a = world.Entities.Npcs[0], b = world.Entities.Npcs[1];
            a.Plan.Start(NpcGoalType.Greet, new List<PlanStep> { new PlanStep(PlanStepType.Greet, b.Position, b.Id.Value) });
            a.Needs.Thirst = 1000;
            a.Inventory.TryAdd(ItemKind.Water, 1);
            new SimulationEngine().Tick(world, null);
            Assert.That(world.Social.Greetings, Is.Zero);
            Assert.That(a.Plan.Goal, Is.EqualTo(NpcGoalType.Drink));
        }

        [Test]
        public void Social_BoundedMemories_RetainImportantGiftsAndAffectHash()
        {
            WorldState world = SocialWorld(); NpcState a = world.Entities.Npcs[0], b = world.Entities.Npcs[1];
            var events = new List<SimEvent>(); a.Inventory.TryAdd(ItemKind.Food, 2); b.Needs.Hunger = 5500;
            SocialSystem.Interact(world, a, b, SocialKind.FoodGift, events);
            for (int i = 0; i < 300; i++)
            {
                world.Tick += 180; a.Social.NextInteractionTick = world.Tick;
                SocialSystem.Interact(world, a, b, SocialKind.Greeting, events);
            }
            Assert.That(a.Social.Memories.Count, Is.EqualTo(32)); Assert.That(world.Social.Timeline.Count, Is.EqualTo(256));
            Assert.That(b.Social.HelpReceivedFrom(a.Id, b.Id), Is.GreaterThan(0));
            ulong before = StateHasher.Compute(world); a.Social.SetTrust(b.Id, 50);
            Assert.That(StateHasher.Compute(world), Is.Not.EqualTo(before));
            before = StateHasher.Compute(world); a.Social.NextInteractionTick++;
            Assert.That(StateHasher.Compute(world), Is.Not.EqualTo(before));
        }

        [Test]
        public void Session_SaveLoad_RestoresQueuedCommandsAndProtectsWorldFromCorruption()
        {
            string folder = Path.Combine(Path.GetTempPath(), "MyAiWorld-M4-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var session = new GameSession(WorldConfigData.CreateDefault()))
                {
                    session.SaveDirectory = folder;
                    for (int i = 0; i < 100; i++) { session.Tick(); }
                    session.SpawnDebugNpc();
                    session.EnqueueCommand(new TradeCommand(new TradeOrder(session.World.Market.NextOrderId, session.World.Entities.Npcs[0].Id, TradeSide.Buy, ItemKind.Food, 1, session.World.Market.Price(ItemKind.Food))));
                    Assert.That(session.Save(), Is.True, session.SaveStatus);
                    ulong expected = session.Tick().StateHash;
                    Assert.That(session.Load(), Is.True, session.SaveStatus);
                    Assert.That(session.QueuedCommandCount, Is.EqualTo(2));
                    Assert.That(session.Tick().StateHash, Is.EqualTo(expected));
                    Assert.That(session.Save(), Is.True);
                    Assert.That(File.Exists(session.ManualSavePath + ".bak"), Is.True);
                    ulong current = session.LastStateHash; WorldState currentWorld = session.World;
                    File.WriteAllText(session.ManualSavePath, "damaged");
                    Assert.That(session.Load(), Is.False); Assert.That(session.World, Is.SameAs(currentWorld));
                    Assert.That(session.LastStateHash, Is.EqualTo(current));
                    Assert.That(session.Load(session.ManualSavePath + ".bak"), Is.True, session.SaveStatus);
                    Assert.That(session.Save(), Is.True); // A corrupt primary must not overwrite the good backup.
                    Assert.That(session.Load(session.ManualSavePath + ".bak"), Is.True);
                    Assert.That(Directory.GetFiles(folder, "*.tmp").Length, Is.Zero);
                }
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [Test]
        public void Session_AutosaveAtMidnight_BackupAndWriteFailureDoNotStopSimulation()
        {
            string folder = Path.Combine(Path.GetTempPath(), "MyAiWorld-M4-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var session = new GameSession(new WorldConfigData(42, 64, 64, 20, 1439, 2, new[] { 1 })))
                {
                    session.SaveDirectory = folder; session.Tick();
                    Assert.That(File.Exists(session.AutoSavePath), Is.True, session.SaveStatus);
                    Assert.That(session.Save(session.AutoSavePath), Is.True);
                    Assert.That(File.Exists(session.AutoSavePath + ".bak"), Is.True);
                    string blocker = Path.Combine(folder, "not-a-directory"); File.WriteAllText(blocker, "test");
                    ulong hash = session.LastStateHash;
                    Assert.That(session.Save(Path.Combine(blocker, "save.json")), Is.False);
                    Assert.That(session.SaveStatus, Does.StartWith("存档失败"));
                    Assert.That(session.LastStateHash, Is.EqualTo(hash)); session.Tick();
                    Assert.That(session.LastTick, Is.EqualTo(1441));
                }
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [Test]
        public void Snapshot_NaturalDeathsAndBirthsInSameTick_RestoresTransientDeadEntities()
        {
            var world = new WorldState(new WorldConfigData(42, 16, 16, 0, 360, 2, new[] { 1 }), new WorldMap(16, 16), new DeterministicRng(42));
            world.Ecology.Initialize(world, world.Map.CollectReachable(world.Center));
            world.Campaign.Initialize(world);
            world.Materials.Initialize(world);
            int initial = world.Ecology.Animals.Count;
            AnimalState mother = null, father = null;
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (mother == null && animal.Sex == AnimalSex.Female && animal.LifeStage == AnimalLifeStage.Adult)
                {
                    mother = animal;
                    continue;
                }
                if (mother != null && animal.Sex == AnimalSex.Male && animal.Species == mother.Species) { father = animal; break; }
            }
            Assert.That(mother, Is.Not.Null); Assert.That(father, Is.Not.Null);
            mother.Health = mother.Definition.Health;
            father.Health = father.Definition.Health;
            mother.PregnancyFatherId = father.Id;
            mother.PregnancyLitter = 1;
            mother.PregnancyTicks = mother.Definition.GestationTicks - 1;
            mother.LastNeedsTick = world.Tick - 60;
            int deaths = 0;
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal == mother || animal == father || animal.Health <= 0) { continue; }
                animal.Health = 0;
                if (++deaths == 3) { break; }
            }
            world.Ecology.BirthProgress = 239;
            var engine = new SimulationEngine(); engine.Tick(world, null);
            Assert.That(world.Ecology.Births, Is.GreaterThan(0));
            Assert.That(world.Ecology.NaturalDeaths, Is.GreaterThanOrEqualTo(1));
            Assert.That(world.Ecology.Animals.All(a => a.Health > 0), Is.True);
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(world)));
            Assert.That(engine.Tick(restored, null).StateHash, Is.EqualTo(engine.Tick(world, null).StateHash));
        }

        [Test]
        public void Session_LegacyEnvelope_IsRejectedAndPreserved()
        {
            string folder = Path.Combine(Path.GetTempPath(), "MyAiWorld-Legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "legacy.json");
                using (var session = new GameSession(WorldConfigData.CreateDefault()))
                {
                    Assert.That(session.Save(path), Is.True);
                    string legacy = File.ReadAllText(path).Replace("\"Version\":" + SaveStore.CurrentVersion, "\"Version\":3"); File.WriteAllText(path, legacy);
                    WorldState current = session.World;
                    Assert.That(session.Load(path), Is.False); Assert.That(session.World, Is.SameAs(current));
                    Assert.That(File.ReadAllText(path), Is.EqualTo(legacy));
                    Assert.That(session.SaveDirectory, Does.EndWith("saves-winter-ecology-v4"));
                }
            }
            finally { Directory.Delete(folder, true); }
        }

        private static WorldState SocialWorld()
        {
            var world = new WorldState(new WorldConfigData(42, 16, 16, 0, 360, 2, new[] { 1 }), new WorldMap(16, 16), new DeterministicRng(42));
            for (int i = 0; i < 3; i++)
            {
                var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("NPC" + i, "Builder", 25), new Personality(), new GridPos(5 + i, 5), (ulong)i + 1);
                npc.Needs.Hunger = npc.Needs.Thirst = npc.Needs.Fatigue = 9000;
                npc.Action.SetIdle("waiting", 100000); world.Entities.AddNpc(npc); world.Market.IssuedCurrency += npc.Coins;
            }
            world.Campaign.Initialize(world);
            world.Materials.Initialize(world);
            EnvironmentSystem.Update(world);
            world.Environment.Temperature = 2500;
            world.Environment.Wind = 0;
            return world;
        }
    }
}
