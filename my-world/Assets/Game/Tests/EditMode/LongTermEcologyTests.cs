using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class LongTermEcologyTests
    {
        private static WorldState Create(int seed = 42, bool connected = false, long start = GameCalendar.StartTick)
        {
            WorldState world = WorldFactory.Create(new WorldConfigData(seed, 64, 64, 0, start, 2, new[] { 1, 2, 4, 8 }));
            world.Ecology.Regions.Enabled = connected;
            return world;
        }

        private static void Tick(WorldState world, List<SimEvent> events, bool climate = true, bool combat = true)
        {
            events.Clear();
            if (climate)
            {
                EnvironmentSystem.Update(world);
            }
            if (combat)
            {
                CombatSystem.RecordPositions(world);
            }
            world.Ecology.UpdateResources(world);
            world.Ecology.Update(world, events);
            if (combat)
            {
                CombatSystem.Update(world, events);
                world.Ecology.ResolveDeaths(world, events);
            }
            world.Tick++;
        }

        private static void KillAll(WorldState world)
        {
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                animal.Health = 0;
                animal.DeathCause = AnimalDeathCause.Injury;
                animal.LastDamageSourceId = EntityId.None;
            }
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
        }

        private static void AssertBalance(WorldState world)
        {
            Assert.That(world.Ecology.Balance.BiomassError(world.Ecology), Is.Zero, "Biomass at " + world.Tick);
            Assert.That(world.Ecology.Balance.WaterError(world.Ecology), Is.Zero, "Water at " + world.Tick);
            Assert.That(world.Ecology.Balance.MineralError(world.Ecology), Is.Zero, "Mineral at " + world.Tick);
            for (int item = 0; item < (int)ItemKind.Count; item++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)item), Is.Zero, "Material " + (ItemKind)item + " at " + world.Tick);
            }
        }

        [Test]
        public void Definitions_StableSpeciesIds_YearsLifespans_AndVariedInitialDemographics()
        {
            Assert.That((int)AnimalSpecies.Rabbit, Is.Zero);
            Assert.That((int)AnimalSpecies.Boar, Is.EqualTo(1));
            Assert.That((int)AnimalSpecies.Deer, Is.EqualTo(2));
            WorldState world = Create();
            for (int species = 0; species <= (int)AnimalSpecies.Raven; species++)
            {
                AnimalDefinition definition = AnimalDefinition.For((AnimalSpecies)species);
                Assert.That(definition.Lifespan, Is.GreaterThanOrEqualTo(2 * GameCalendar.DaysPerYear * GameCalendar.TicksPerDay));
                Assert.That(definition.MaturityTicks, Is.GreaterThanOrEqualTo(7 * GameCalendar.TicksPerDay));
                Assert.That(world.Ecology.Count((AnimalSpecies)species), Is.GreaterThan(0));
                var ages = new HashSet<int>();
                var lifespans = new HashSet<int>();
                var reserves = new HashSet<int>();
                foreach (AnimalState animal in world.Ecology.Animals)
                {
                    if ((int)animal.Species == species)
                    {
                        ages.Add(animal.AgeTicks);
                        lifespans.Add(animal.MaxAgeTicks);
                        reserves.Add(animal.Energy);
                    }
                }
                Assert.That(ages.Count, Is.GreaterThan(1));
                Assert.That(lifespans.Count, Is.GreaterThan(1));
                Assert.That(reserves.Count, Is.GreaterThan(1));
            }
            AssertBalance(world);
            world.Ecology.Validate(world);
        }

        [Test]
        public void Extinction_IsLegal_LastAnimalIsHuntable_AndDeathCreatesOneOriginalIdCarcass()
        {
            WorldState world = Create();
            KillAll(world);
            AnimalState rabbit = world.Ecology.Spawn(world, AnimalSpecies.Rabbit, world.Center, AnimalDefinition.Rabbit.MaturityTicks);
            Assert.That(world.Ecology.Count(AnimalSpecies.Rabbit), Is.EqualTo(1));
            Assert.That(world.Ecology.CanHunt(rabbit), Is.True);
            int carcasses = world.Ecology.Carcasses.Count;
            rabbit.Health = 0;
            rabbit.DeathCause = AnimalDeathCause.Injury;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            Assert.That(world.Ecology.Find(rabbit.Id.Value), Is.Null);
            Assert.That(world.Ecology.FindCarcass(rabbit.Id.Value), Is.Not.Null);
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            Assert.That(world.Ecology.Carcasses.Count, Is.EqualTo(carcasses + 1));
            int births = world.Ecology.Births;
            var events = new List<SimEvent>();
            for (int i = 0; i < 3 * GameCalendar.TicksPerDay; i++)
            {
                Tick(world, events);
            }
            Assert.That(world.Ecology.Animals.Count, Is.Zero);
            Assert.That(world.Ecology.Births, Is.EqualTo(births));
            AssertBalance(world);
            world.Ecology.Validate(world);
        }

        [Test]
        public void Carcass_BatchedHarvest_ExportsMeatAndHideOnlyOnce_AndHonorsBagCapacity()
        {
            WorldState world = Create();
            AnimalState boar = world.Ecology.Spawn(world, AnimalSpecies.Boar, world.Center, AnimalDefinition.Boar.MaturityTicks);
            NpcState npc = WorldFactory.SpawnNpc(world, world.Center, true, new List<SimEvent>());
            Assert.That(npc, Is.Not.Null);
            npc.Inventory.TryAdd(ItemKind.Seed, npc.Inventory.Capacity - 1);
            world.Materials.Produce(ItemKind.Seed, npc.Inventory.Capacity - 1);
            boar.ReservedBy = npc.Id;
            boar.LastDamageSourceId = npc.Id;
            boar.DeathCause = AnimalDeathCause.Hunt;
            boar.Health = 0;
            var events = new List<SimEvent>();
            world.Ecology.ResolveDeaths(world, events);
            CarcassState carcass = world.Ecology.FindCarcass(boar.Id.Value);
            int meat = carcass.MeatRemaining;
            int hide = carcass.HideRemaining;
            long foodBefore = world.Materials.Produced(ItemKind.Food);
            Assert.That(world.Ecology.HarvestCarcass(world, npc, carcass, events), Is.True);
            Assert.That(npc.Inventory.Count(ItemKind.RawMeat), Is.EqualTo(1));
            Assert.That(carcass.MeatRemaining, Is.EqualTo(meat - 1));
            Assert.That(world.Ecology.HarvestCarcass(world, npc, carcass, events), Is.False);
            for (int batch = 0; batch < meat + hide + 2; batch++)
            {
                foreach (ItemKind item in new[] { ItemKind.RawMeat, ItemKind.Hide })
                {
                    int quantity = npc.Inventory.Count(item);
                    if (quantity > 0)
                    {
                        npc.Inventory.TryRemove(item, quantity);
                        world.Materials.Consume(item, quantity);
                    }
                }
                world.Ecology.HarvestCarcass(world, npc, carcass, events);
            }
            Assert.That(carcass.MeatRemaining + carcass.HideRemaining, Is.Zero);
            Assert.That(world.Ecology.Balance.CarcassProduced(ItemKind.RawMeat), Is.EqualTo(meat));
            Assert.That(world.Ecology.Balance.CarcassProduced(ItemKind.Hide), Is.EqualTo(hide));
            Assert.That(world.Materials.Produced(ItemKind.Food), Is.EqualTo(foodBefore));
            Assert.That(npc.HuntKills, Is.EqualTo(1));
            Assert.That(world.Ecology.HuntKills, Is.EqualTo(1));
            AssertBalance(world);
        }

        [Test]
        public void Scavenging_ConsumesEcologicalMeat_NotMaterialFood_AndDecayReturnsLitter()
        {
            WorldState world = Create();
            KillAll(world);
            AnimalState deer = world.Ecology.Spawn(world, AnimalSpecies.Deer, world.Center, AnimalDefinition.Deer.MaturityTicks);
            deer.Health = 0;
            deer.DeathCause = AnimalDeathCause.Injury;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            CarcassState carcass = world.Ecology.FindCarcass(deer.Id.Value);
            AnimalState fox = world.Ecology.Spawn(world, AnimalSpecies.Fox, world.Center, AnimalDefinition.Fox.MaturityTicks);
            world.Ecology.Balance.Respiration += fox.Energy;
            fox.Energy = 0;
            long materialFood = world.Materials.Produced(ItemKind.Food);
            int meat = carcass.MeatRemaining;
            world.Tick += 60;
            world.Ecology.Update(world, new List<SimEvent>());
            Assert.That(carcass.MeatRemaining, Is.LessThan(meat));
            Assert.That(fox.Energy, Is.GreaterThan(0));
            Assert.That(world.Ecology.Balance.CarrionEaten, Is.GreaterThan(0));
            Assert.That(world.Materials.Produced(ItemKind.Food), Is.EqualTo(materialFood));
            fox.Health = 0;
            fox.DeathCause = AnimalDeathCause.Injury;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            var events = new List<SimEvent>();
            world.Environment.Temperature = 2000;
            world.Environment.Weather = WeatherKind.Clear;
            for (int i = 0; i < 12 * GameCalendar.TicksPerDay; i++)
            {
                Tick(world, events, climate: false, combat: false);
            }
            Assert.That(world.Ecology.Balance.Decomposed, Is.GreaterThan(0));
            Assert.That(world.Ecology.FindCarcass(deer.Id.Value), Is.Null);
            AssertBalance(world);
        }

        [Test]
        public void Nodes_WithoutSolarOrRain_ExhaustFinitePlantWaterAndMineralPools()
        {
            WorldState world = Create(start: GameCalendar.WinterStartTick);
            KillAll(world);
            world.Environment.Temperature = 800;
            world.Environment.Daylight = 0;
            world.Environment.Weather = WeatherKind.Clear;
            long initial = EcologyBalance.CurrentBiomass(world.Ecology);
            long initialMineral = world.Ecology.Balance.InitialMineral;
            for (int minute = 0; minute < 20 * GameCalendar.TicksPerDay; minute++)
            {
                foreach (ResourceNodeState node in world.Resources.Nodes)
                {
                    if (node.Amount > 0)
                    {
                        world.Materials.Consume(node.Item, node.Amount);
                        node.Amount = 0;
                    }
                }
                world.Ecology.UpdateResources(world);
                world.Tick++;
            }
            Assert.That(world.Ecology.Balance.Photosynthesis, Is.Zero);
            Assert.That(world.Ecology.Balance.Precipitation, Is.Zero);
            Assert.That(world.Ecology.Balance.MaterialBiomassProduced, Is.GreaterThan(0).And.LessThanOrEqualTo(initial));
            Assert.That(world.Ecology.Balance.MineralProduced, Is.GreaterThan(0).And.LessThanOrEqualTo(initialMineral));
            Assert.That(world.Ecology.Balance.MaterialWaterProduced, Is.GreaterThan(0));
            AssertBalance(world);
        }

        private static AnimalState Pair(WorldState world, AnimalSex sex)
        {
            AnimalState animal = world.Ecology.Spawn(world, AnimalSpecies.Rabbit, world.Center, AnimalDefinition.Rabbit.MaturityTicks);
            animal.Sex = sex;
            animal.ReproductionCooldown = 0;
            world.Ecology.Balance.IntroducedBiomass += animal.Definition.EnergyCapacity - animal.Energy;
            animal.Energy = animal.Definition.EnergyCapacity;
            return animal;
        }

        [Test]
        public void Reproduction_RequiresNearbyMateAndSeason_GestationAndNursingConsumeRealReserves()
        {
            WorldState world = Create();
            KillAll(world);
            AnimalState mother = Pair(world, AnimalSex.Female);
            AnimalState father = Pair(world, AnimalSex.Male);
            var events = new List<SimEvent>();
            for (int i = 0; i < 240; i++)
            {
                Tick(world, events, combat: false);
            }
            Assert.That(world.Ecology.Births, Is.Zero, "No four-hour spontaneous birth.");
            Assert.That(mother.PregnancyLitter, Is.GreaterThan(0));
            for (int i = 240; i < 6 * GameCalendar.TicksPerDay; i++)
            {
                Tick(world, events, combat: false);
            }
            Assert.That(world.Ecology.Births, Is.GreaterThan(0));
            Assert.That(world.Ecology.Balance.BirthTransferred, Is.GreaterThan(0));
            Assert.That(world.Ecology.Balance.NursingTransferred, Is.GreaterThan(0));
            foreach (AnimalState child in world.Ecology.Animals)
            {
                if (child.Id == mother.Id || child.Id == father.Id)
                {
                    continue;
                }
                Assert.That(child.MotherId, Is.EqualTo(mother.Id));
                Assert.That(child.FatherId, Is.EqualTo(father.Id));
                Assert.That(child.LifeStage, Is.Not.EqualTo(AnimalLifeStage.Adult));
            }
            AssertBalance(world);
            world.Ecology.Validate(world);
            WorldState winter = Create(start: GameCalendar.WinterStartTick);
            KillAll(winter);
            Pair(winter, AnimalSex.Female);
            Pair(winter, AnimalSex.Male);
            for (int i = 0; i < 8 * GameCalendar.TicksPerDay; i++)
            {
                Tick(winter, events, combat: false);
            }
            Assert.That(winter.Ecology.Births, Is.Zero);
            AssertBalance(winter);
        }

        [Test]
        public void Migration_ReachableBoundaryCrossing_PreservesObjectIdRng_AndReturnsNoNewEntity()
        {
            WorldState world = Create(connected: true, start: GameCalendar.WinterStartTick);
            KillAll(world);
            EcologyRegionState region = world.Ecology.Regions.Regions[0];
            Assert.That(world.Map.IsReachable(world.Center, region.Exit), Is.True);
            AnimalState deer = world.Ecology.Spawn(world, AnimalSpecies.Deer, region.Exit, AnimalDefinition.Deer.MaturityTicks);
            deer.MigrationDestination = region.Id;
            ulong rng = deer.RngState;
            int nextId = world.NextEntityId;
            long biomass = EcologyBalance.CurrentBiomass(world.Ecology);
            var events = new List<SimEvent>();
            MigrationSystem.Update(world, events);
            Assert.That(deer.Region, Is.EqualTo(region.Id));
            Assert.That(deer.Position, Is.EqualTo(GridPos.Invalid));
            Assert.That(world.Ecology.Find(deer.Id.Value), Is.SameAs(deer));
            Assert.That(deer.RngState, Is.EqualTo(rng));
            Assert.That(world.Ecology.Count(AnimalSpecies.Deer), Is.Zero);
            Assert.That(world.Ecology.Regions.Departures, Is.EqualTo(1));
            world.Tick = GameCalendar.DaysPerYear * GameCalendar.TicksPerDay + GameCalendar.StartTick;
            MigrationSystem.Update(world, events);
            world.Ecology.ResolveDeaths(world, events);
            Assert.That(deer.Region, Is.EqualTo(-1));
            Assert.That(deer.Position, Is.EqualTo(region.Exit));
            Assert.That(deer.RngState, Is.EqualTo(rng));
            Assert.That(world.NextEntityId, Is.EqualTo(nextId));
            Assert.That(EcologyBalance.CurrentBiomass(world.Ecology), Is.EqualTo(biomass));
            Assert.That(world.Ecology.Regions.Returns, Is.EqualTo(1));
            AssertBalance(world);
            world.Ecology.Validate(world);
        }

        [Test]
        public void Isolation_DisablesCrossings_NotResidentMetabolismOrLegitimateExtinction()
        {
            WorldState world = Create(connected: false, start: GameCalendar.WinterStartTick);
            KillAll(world);
            EcologyRegionState region = world.Ecology.Regions.Regions[0];
            AnimalState deer = world.Ecology.Spawn(world, AnimalSpecies.Deer, region.Exit, AnimalDefinition.Deer.MaturityTicks);
            deer.MigrationDestination = 0;
            MigrationSystem.Update(world, new List<SimEvent>());
            Assert.That(deer.Region, Is.EqualTo(-1));
            Assert.That(deer.MigrationDestination, Is.EqualTo(-1));
            Assert.That(world.Ecology.Regions.Departures, Is.Zero);
            AssertBalance(world);
        }

        [Test]
        public void ExternalResidents_AgeFeedReproduceAndDie_InIsolation_WithoutPhantomRecolonization()
        {
            WorldState world = Create(connected: true);
            KillAll(world);
            AnimalState mother = Pair(world, AnimalSex.Female);
            AnimalState father = Pair(world, AnimalSex.Male);
            EcologyRegionState region = world.Ecology.Regions.Regions[0];
            foreach (AnimalState animal in new[] { mother, father })
            {
                animal.Position = region.Exit;
                animal.HabitatId = world.Ecology.HabitatAt(region.Exit).Id;
                animal.MigrationDestination = region.Id;
            }
            MigrationSystem.Update(world, new List<SimEvent>());
            world.Ecology.Regions.Enabled = false;
            int age = mother.AgeTicks;
            long consumed = world.Ecology.Balance.PlantEaten;
            var events = new List<SimEvent>();
            for (int i = 0; i < 6 * GameCalendar.TicksPerDay; i++)
            {
                Tick(world, events, combat: false);
            }
            Assert.That(mother.AgeTicks, Is.GreaterThan(age));
            Assert.That(world.Ecology.Balance.PlantEaten, Is.GreaterThan(consumed));
            Assert.That(region.Births, Is.GreaterThan(0));
            Assert.That(world.Ecology.Count(AnimalSpecies.Rabbit), Is.Zero);
            AssertBalance(world);
            world.Ecology.Validate(world);
            KillAll(world);
            Assert.That(region.Deaths, Is.EqualTo(region.Immigration + region.Births));
            for (int i = 0; i < GameCalendar.DaysPerYear * GameCalendar.TicksPerDay; i++)
            {
                Tick(world, events, combat: false);
                if (i % GameCalendar.TicksPerDay == 0)
                {
                    Assert.That(world.Ecology.Animals.Count, Is.Zero);
                    AssertBalance(world);
                }
            }
            world.Ecology.Regions.Enabled = true;
            MigrationSystem.Update(world, events);
            Assert.That(world.Ecology.Animals.Count, Is.Zero);
            Assert.That(world.Ecology.Regions.Returns, Is.Zero);
            world.Ecology.Validate(world);
        }

        private static WorldState CloneEcology(WorldState source)
        {
            WorldState candidate = WorldFactory.Create(source.Config);
            candidate.Tick = source.Tick;
            candidate.NextEntityId = source.NextEntityId;
            using (var reader = new BinaryReader(new MemoryStream(source.Ecology.Capture())))
            {
                candidate.Ecology.ReadSnapshot(reader);
            }
            EnvironmentState from = source.Environment;
            EnvironmentState to = candidate.Environment;
            to.Temperature = from.Temperature;
            to.Wind = from.Wind;
            to.Daylight = from.Daylight;
            to.SnowDepth = from.SnowDepth;
            to.Weather = from.Weather;
            to.NextWeatherTick = from.NextWeatherTick;
            to.RngState = from.RngState;
            foreach (ResourceNodeState node in source.Resources.Nodes)
            {
                ResourceNodeState copy = candidate.Resources.FindNode(node.Id);
                copy.Amount = node.Amount;
                copy.RegenerationProgress = node.RegenerationProgress;
                copy.ReservedBy = node.ReservedBy;
            }
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    source.Materials.WriteSnapshot(writer);
                }
                stream.Position = 0;
                using (var reader = new BinaryReader(stream))
                {
                    candidate.Materials.ReadSnapshot(reader);
                }
            }
            return candidate;
        }

        [Test]
        public void Snapshot_RoundTripAllEcologyState_ResumeEveryMinute_RejectDanglingTargetAndWrongAccounting()
        {
            WorldState world = Create();
            // Combat has its own snapshot. This fixture isolates ecological resume
            // determinism from in-flight combat by excluding all predators.
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Definition.IsPredator)
                {
                    animal.Health = 0;
                    animal.DeathCause = AnimalDeathCause.Injury;
                }
            }
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            var firstEvents = new List<SimEvent>();
            var secondEvents = new List<SimEvent>();
            for (int i = 0; i < 3 * GameCalendar.TicksPerDay; i++)
            {
                Tick(world, firstEvents, combat: false);
            }
            WorldState restored = CloneEcology(world);
            CollectionAssert.AreEqual(world.Ecology.Capture(), restored.Ecology.Capture());
            restored.Ecology.Validate(restored);
            for (int minute = 0; minute < 2 * GameCalendar.TicksPerDay; minute++)
            {
                Tick(world, firstEvents, combat: false);
                Tick(restored, secondEvents, combat: false);
                CollectionAssert.AreEqual(world.Ecology.Capture(), restored.Ecology.Capture(), "Resume minute " + minute);
                Assert.That(firstEvents.Count, Is.EqualTo(secondEvents.Count));
            }
            AssertBalance(restored);
            AnimalState targetOwner = restored.Ecology.Animals[0];
            targetOwner.TargetAnimalId = new EntityId(restored.NextEntityId + 100);
            Assert.Throws<InvalidDataException>(() => restored.Ecology.Validate(restored));
            targetOwner.TargetAnimalId = EntityId.None;
            restored.Ecology.Balance.Respiration++;
            Assert.Throws<InvalidDataException>(() => restored.Ecology.Validate(restored));
        }

        [TestCase(42, false, 112)]
        [TestCase(12345, true, 112)]
        [TestCase(2026, false, 112)]
        [TestCase(31415, true, 112)]
        [TestCase(42, true, 448)]
        [TestCase(12345, false, 448)]
        [TestCase(2026, true, 448)]
        [TestCase(31415, false, 448)]
        public void LongRun_FourSeedsAcrossSeasonsAndYears_ConserveFiniteEcologyAndMaterials(int seed, bool connected, int days)
        {
            // Connected cases exercise winter migration; isolated cases start in
            // spring so multi-year reproduction is not vacuously tested after a
            // legitimate severe first-winter extinction.
            WorldState world = Create(seed, connected, connected ? GameCalendar.WinterStartTick : GameCalendar.StartTick);
            var events = new List<SimEvent>();
            var seenSeasons = new HashSet<Season>();
            int snapshots = 0;
            int maxSnapshotBytes = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int minute = 0; minute < days * GameCalendar.TicksPerDay; minute++)
            {
                Tick(world, events);
                // Every update checks the physical ledgers; full reference validation
                // is sampled daily because it also checks map reachability.
                AssertBalance(world);
                if (minute % GameCalendar.TicksPerDay == 0)
                {
                    world.Ecology.Validate(world);
                    seenSeasons.Add(world.Date.Season);
                    byte[] bytes = world.Ecology.Capture();
                    maxSnapshotBytes = Math.Max(maxSnapshotBytes, bytes.Length);
                    WorldState candidate = CloneEcology(world);
                    CollectionAssert.AreEqual(bytes, candidate.Ecology.Capture());
                    candidate.Ecology.Validate(candidate);
                    snapshots++;
                }
            }
            watch.Stop();
            Assert.That(seenSeasons.Count, Is.EqualTo(4));
            Assert.That(world.Ecology.BirthProgress, Is.EqualTo((long)days * GameCalendar.TicksPerDay));
            Assert.That(world.Ecology.Balance.Respiration, Is.GreaterThan(0));
            Assert.That(world.Ecology.Balance.Photosynthesis, Is.GreaterThan(0));
            if (!connected)
            {
                Assert.That(world.Ecology.Births, Is.GreaterThan(0), "Spring reproduction must actually execute before any legal extinction.");
            }
            Assert.That(world.Ecology.Animals.Count, Is.LessThanOrEqualTo(2048));
            Assert.That(maxSnapshotBytes, Is.LessThan(4 * 1024 * 1024));
            Console.WriteLine("Ecology seed=" + seed + " connected=" + connected + " days=" + days +
                " alive=" + world.Ecology.Animals.Count + " births=" + world.Ecology.Births +
                " deaths=" + world.Ecology.TotalDeaths + " migrations=" + world.Ecology.Regions.Departures +
                "/" + world.Ecology.Regions.Returns + " snapshots=" + snapshots + " maxBytes=" + maxSnapshotBytes +
                " elapsedMs=" + watch.ElapsedMilliseconds);
        }
    }
}
