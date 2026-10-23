using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class RegionalCombatTests
    {
        private static WorldState Create()
        {
            WorldState world = WorldFactory.Create(new WorldConfigData(42, 32, 32, 0, 360, 2, new[] { 1 }));
            world.Ecology.Regions.Enabled = true;
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                animal.Health = 0;
                animal.DeathCause = AnimalDeathCause.Injury;
            }
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            world.Environment.Temperature = 1500;
            world.Environment.Weather = WeatherKind.Clear;
            return world;
        }

        private static AnimalState Resident(WorldState world, AnimalSpecies species, int region = 0)
        {
            EcologyRegionState destination = world.Ecology.Regions.Regions[region];
            AnimalState animal = world.Ecology.Spawn(world, species, destination.Exit, AnimalDefinition.For(species).MaturityTicks);
            animal.MigrationDestination = region;
            MigrationSystem.Update(world, new List<SimEvent>());
            Assert.That(animal.Region, Is.EqualTo(region));
            Assert.That(animal.Position, Is.EqualTo(GridPos.Invalid));
            return animal;
        }

        private static List<SimEvent> Minute(WorldState world, bool ecology = false)
        {
            var events = new List<SimEvent>();
            CombatSystem.RecordPositions(world);
            if (ecology) { world.Ecology.Update(world, events); }
            CombatSystem.Update(world, events);
            world.Ecology.ResolveDeaths(world, events);
            world.Tick++;
            return events;
        }

        private static void NoLocalEffects(WorldState world)
        {
            Assert.That(world.Combat.CombatEvents, Is.Empty);
            Assert.That(world.Combat.TraceSegments, Is.Empty);
            Assert.That(world.Combat.Projectiles, Is.Empty);
            foreach (CombatAttackState attack in world.Combat.Attacks)
            {
                Assert.That(attack.FacingX, Is.Zero);
                Assert.That(attack.FacingY, Is.Zero);
            }
        }

        private static void Conserved(WorldState world)
        {
            Assert.That(world.Ecology.Balance.BiomassError(world.Ecology), Is.Zero);
            Assert.That(world.Ecology.Balance.WaterError(world.Ecology), Is.Zero);
            Assert.That(world.Ecology.Balance.MineralError(world.Ecology), Is.Zero);
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString());
            }
        }

        [TestCase(0, AnimalSpecies.Wolf, 24)]
        [TestCase(1, AnimalSpecies.Wolf, 24)]
        [TestCase(0, AnimalSpecies.Fox, 14)]
        [TestCase(1, AnimalSpecies.Boar, 18)]
        [TestCase(1, AnimalSpecies.Rabbit, 6)]
        public void ExternalMelee_UsesPersistentPreparationRecoveryCooldown_AndRealDamage(int region, AnimalSpecies species, int damage)
        {
            WorldState world = Create();
            AnimalState attacker = Resident(world, species, region);
            AnimalState target = Resident(world, AnimalSpecies.Deer, region);
            int health = target.Health;
            ulong combatRng = world.Combat.RngState, attackerRng = attacker.RngState, targetRng = target.RngState;
            long start = world.Tick;
            Assert.That(CombatSystem.TryPosition(world, attacker.Id, true, out GridPos _), Is.False);
            Assert.That(CombatSystem.RequestAnimalAttack(world, attacker, target, null), Is.True);
            CombatAttackState attack = world.Combat.GetAttack(attacker.Id);
            Assert.That(attack.Region, Is.EqualTo(region));
            Assert.That(attack.HabitatId, Is.EqualTo(attacker.HabitatId));
            Assert.That(attack.ReleaseTick, Is.EqualTo(start + 1));
            Assert.That(attack.RecoveryUntilTick, Is.EqualTo(start + 2));
            Assert.That(attack.CooldownUntilTick, Is.EqualTo(start + 4));
            Assert.That(CombatSystem.RequestAnimalAttack(world, attacker, target, null), Is.False);
            Minute(world);
            Assert.That(attack.Phase, Is.EqualTo(CombatAttackPhase.Preparing));
            Assert.That(target.Health, Is.EqualTo(health));
            Minute(world);
            Assert.That(attack.Phase, Is.EqualTo(CombatAttackPhase.Recovery));
            Assert.That(attack.ReleasedTick, Is.EqualTo(start + 1));
            Assert.That(target.Health, Is.EqualTo(health - damage));
            CombatDamageRecord record = world.Combat.GetLastDamage(target.Id).Value;
            Assert.That(record.Source, Is.EqualTo(attacker.Id));
            Assert.That(record.Target, Is.EqualTo(target.Id));
            Assert.That(record.SourceIsAnimal && record.TargetIsAnimal, Is.True);
            Assert.That(record.AttackId, Is.EqualTo(attack.Id));
            Assert.That(record.ProjectileId, Is.Zero);
            Assert.That(record.Damage, Is.EqualTo(damage));
            Assert.That(record.Tick, Is.EqualTo(start + 1));
            Assert.That(target.LastDamageSourceId, Is.EqualTo(attacker.Id));
            Assert.That(CombatSystem.RequestAnimalAttack(world, attacker, target, null), Is.False);
            CombatSystem.Update(world, null);
            Assert.That(world.Combat.Hits, Is.EqualTo(1), "A second update in this minute cannot deal damage again.");
            Minute(world);
            Assert.That(attack.Phase, Is.EqualTo(CombatAttackPhase.Cooldown));
            Assert.That(CombatSystem.RequestAnimalAttack(world, attacker, target, null), Is.False);
            Minute(world);
            Assert.That(CombatSystem.RequestAnimalAttack(world, attacker, target, null), Is.True);
            Assert.That(world.Combat.GetAttack(attacker.Id).Id, Is.GreaterThan(attack.Id));
            Assert.That(target.Health, Is.EqualTo(health - damage));
            Assert.That(world.Combat.RngState, Is.EqualTo(combatRng));
            Assert.That(attacker.RngState, Is.EqualTo(attackerRng));
            Assert.That(target.RngState, Is.EqualTo(targetRng));
            NoLocalEffects(world);
            Conserved(world);
            CombatSnapshot.Validate(world);
        }

        [Test]
        public void EcologyDecision_RequestsExternalPredation_ButOnlyCombatDealsDelayedDamage()
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            int hungryEnergy = wolf.Definition.EnergyCapacity / 3;
            world.Ecology.Balance.Respiration += wolf.Energy - hungryEnergy;
            wolf.Energy = hungryEnergy;
            world.Tick += 60;
            int health = rabbit.Health;
            var events = new List<SimEvent>();
            world.Ecology.Update(world, events);
            Assert.That(wolf.Behavior, Is.EqualTo(AnimalBehavior.Hunt));
            Assert.That(wolf.TargetAnimalId, Is.EqualTo(rabbit.Id));
            CombatAttackState attack = world.Combat.GetAttack(wolf.Id);
            Assert.That(attack, Is.Not.Null);
            Assert.That(attack.Region, Is.Zero);
            Assert.That(rabbit.Health, Is.EqualTo(health));
            Minute(world);
            Assert.That(rabbit.Health, Is.EqualTo(health));
            Minute(world);
            Assert.That(rabbit.Health, Is.EqualTo(health - 24));
            Assert.That(world.Combat.Hits, Is.EqualTo(1));
            NoLocalEffects(world);
            Conserved(world);
        }

        [TestCase("null-attacker")]
        [TestCase("null-target")]
        [TestCase("unregistered-attacker")]
        [TestCase("unregistered-target")]
        [TestCase("self")]
        [TestCase("dead-attacker")]
        [TestCase("dead-target")]
        [TestCase("processed-attacker")]
        [TestCase("processed-target")]
        [TestCase("cross-region")]
        [TestCase("local-target")]
        [TestCase("invalid-region")]
        [TestCase("negative-region")]
        [TestCase("different-habitat")]
        [TestCase("invalid-habitat")]
        [TestCase("local-habitat")]
        [TestCase("retained-local-position")]
        public void RegionalRequest_InvalidOrCrossRegionActorsRejectWithoutAuthority(string invalid)
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            switch (invalid)
            {
                case "null-attacker": wolf = null; break;
                case "null-target": rabbit = null; break;
                case "unregistered-attacker": wolf = new AnimalState(wolf.Id, wolf.Species, GridPos.Invalid, 1, 0) { Region = 0, HabitatId = wolf.HabitatId }; break;
                case "unregistered-target": rabbit = new AnimalState(rabbit.Id, rabbit.Species, GridPos.Invalid, 1, 0) { Region = 0, HabitatId = rabbit.HabitatId }; break;
                case "self": rabbit = wolf; break;
                case "dead-attacker": wolf.Health = 0; break;
                case "dead-target": rabbit.Health = 0; break;
                case "processed-attacker": wolf.DeadProcessed = true; break;
                case "processed-target": rabbit.DeadProcessed = true; break;
                case "cross-region": rabbit.Region = 1; rabbit.HabitatId = world.Ecology.Regions.Regions[1].HabitatId; break;
                case "local-target": rabbit.Region = -1; rabbit.Position = world.Center; break;
                case "invalid-region": wolf.Region = rabbit.Region = 999; break;
                case "negative-region": wolf.Region = rabbit.Region = -2; break;
                case "different-habitat": rabbit.HabitatId = world.Ecology.Regions.Regions[1].HabitatId; break;
                case "invalid-habitat": wolf.HabitatId = rabbit.HabitatId = 999; break;
                case "local-habitat": wolf.HabitatId = rabbit.HabitatId = 0; break;
                case "retained-local-position": rabbit.Position = world.Center; break;
            }
            byte[] before = CombatSnapshot.Capture(world);
            Assert.That(CombatSystem.RequestAnimalAttack(world, wolf, rabbit, null), Is.False);
            CollectionAssert.AreEqual(before, CombatSnapshot.Capture(world));
            NoLocalEffects(world);
        }

        [TestCase("attacker")]
        [TestCase("target")]
        [TestCase("both")]
        [TestCase("habitat")]
        public void PreparingRegionalAttack_MovementOfEitherOrBothActorsCancelsSavedAuthority(string movement)
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            int health = rabbit.Health;
            Assert.That(CombatSystem.RequestAnimalAttack(world, wolf, rabbit, null), Is.True);
            Minute(world);
            foreach (AnimalState animal in movement == "both" || movement == "habitat" ? new[] { wolf, rabbit } :
                new[] { movement == "attacker" ? wolf : rabbit })
            {
                if (movement == "habitat") { animal.HabitatId = 0; }
                else
                {
                    animal.Region = 1;
                    animal.HabitatId = world.Ecology.Regions.Regions[1].HabitatId;
                }
            }
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Validate(world));
            Minute(world);
            Assert.That(world.Combat.GetAttack(wolf.Id), Is.Null);
            Assert.That(rabbit.Health, Is.EqualTo(health));
            Assert.That(world.Combat.Hits, Is.Zero);
            NoLocalEffects(world);
            Conserved(world);
            CombatSnapshot.Validate(world);
        }

        [Test]
        public void PreparingRegionalAttack_RealMigrationReturnCancelsWithoutLocalContact()
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            int health = rabbit.Health;
            CombatSystem.RequestAnimalAttack(world, wolf, rabbit, null);
            Minute(world);
            rabbit.LastMigrationTick = -1;
            HabitatPatchState local = world.Ecology.HabitatAt(world.Ecology.Regions.Regions[0].Exit);
            local.SnowDepth = 0;
            world.Tick += 60;
            MigrationSystem.Update(world, new List<SimEvent>());
            Assert.That(rabbit.Region, Is.EqualTo(-1));
            Assert.That(world.Ecology.Find(rabbit.Id.Value), Is.SameAs(rabbit));
            CombatSystem.Update(world, null);
            Assert.That(world.Combat.GetAttack(wolf.Id), Is.Null);
            Assert.That(rabbit.Health, Is.EqualTo(health));
            Assert.That(world.Combat.Hits, Is.Zero);
            NoLocalEffects(world);
            Conserved(world);
        }

        [Test]
        public void LocalPreparation_BothActorsDepartTogether_NeverBecomesRegional()
        {
            WorldState world = Create();
            EcologyRegionState destination = world.Ecology.Regions.Regions[0];
            AnimalState wolf = world.Ecology.Spawn(world, AnimalSpecies.Wolf, destination.Exit, AnimalDefinition.Wolf.MaturityTicks);
            AnimalState rabbit = world.Ecology.Spawn(world, AnimalSpecies.Rabbit, destination.Exit, AnimalDefinition.Rabbit.MaturityTicks);
            int health = rabbit.Health;
            Assert.That(CombatSystem.RequestAnimalAttack(world, wolf, rabbit, null), Is.True);
            CombatAttackState attack = world.Combat.GetAttack(wolf.Id);
            Assert.That(attack.Region, Is.EqualTo(-1));
            Assert.That(attack.HabitatId, Is.EqualTo(-1));
            Minute(world);
            wolf.MigrationDestination = rabbit.MigrationDestination = 0;
            MigrationSystem.Update(world, new List<SimEvent>());
            Assert.That(wolf.Region, Is.Zero);
            Assert.That(rabbit.Region, Is.Zero);
            Assert.That(attack.Region, Is.EqualTo(-1));
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Validate(world));
            Minute(world);
            Assert.That(world.Combat.GetAttack(wolf.Id), Is.Null);
            Assert.That(rabbit.Health, Is.EqualTo(health));
            Assert.That(world.Combat.Hits, Is.Zero);
            Conserved(world);
            CombatSnapshot.Validate(world);
        }

        [TestCase("implicit-local")]
        [TestCase("cross-region")]
        [TestCase("both-moved")]
        [TestCase("habitat-changed")]
        [TestCase("projectile")]
        [TestCase("npc-source")]
        public void ApplyDamage_RegionalPathCannotBypassSavedRegionOrLocalProjectileBoundary(string invalid)
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            int health = rabbit.Health, habitat = wolf.HabitatId;
            if (invalid == "cross-region" || invalid == "both-moved") { rabbit.Region = 1; }
            if (invalid == "both-moved") { wolf.Region = 1; }
            if (invalid == "habitat-changed") { rabbit.HabitatId++; }
            CombatSystem.ApplyDamage(world, wolf.Id, invalid != "npc-source", rabbit.Id, true, 24, 1,
                invalid == "projectile" ? 1 : 0, CombatState.TimeUnitsPerTick, null,
                invalid == "implicit-local" ? -1 : 0, habitat);
            Assert.That(rabbit.Health, Is.EqualTo(health));
            Assert.That(rabbit.LastDamageSourceId.IsNone, Is.True);
            Assert.That(world.Combat.DamageRecords, Is.Empty);
            Assert.That(world.Combat.Hits, Is.Zero);
            NoLocalEffects(world);
        }

        [Test]
        public void RegionalLethalContact_PreservesIdentityAttributionAndBiomass_UniqueEcologyDeathOnly()
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState vole = Resident(world, AnimalSpecies.Vole);
            int carcasses = world.Ecology.Carcasses.Count, predation = world.Ecology.PredationDeaths;
            long biomass = EcologyBalance.CurrentBiomass(world.Ecology);
            ulong rng = vole.RngState;
            CombatSystem.RequestAnimalAttack(world, wolf, vole, null);
            Minute(world);
            var events = new List<SimEvent>();
            CombatSystem.Update(world, events);
            Assert.That(vole.Health, Is.Zero);
            Assert.That(vole.DeathCause, Is.EqualTo(AnimalDeathCause.Predation));
            Assert.That(vole.LastDamageSourceId, Is.EqualTo(wolf.Id));
            Assert.That(world.Ecology.Find(vole.Id.Value), Is.SameAs(vole));
            Assert.That(world.Ecology.FindCarcass(vole.Id.Value), Is.Null);
            Assert.That(world.Ecology.PredationDeaths, Is.EqualTo(predation));
            Assert.That(world.Combat.AnimalKills, Is.EqualTo(1));
            CombatDamageRecord record = world.Combat.GetLastDamage(vole.Id).Value;
            Assert.That(record.Damage, Is.EqualTo(AnimalDefinition.Vole.Health));
            Assert.That(record.HealthDepleted, Is.True);
            world.Ecology.ResolveDeaths(world, events);
            CarcassState carcass = world.Ecology.FindCarcass(vole.Id.Value);
            Assert.That(carcass.Id, Is.EqualTo(vole.Id));
            Assert.That(carcass.Region, Is.Zero);
            Assert.That(carcass.HabitatId, Is.EqualTo(vole.HabitatId));
            Assert.That(carcass.Position, Is.EqualTo(GridPos.Invalid));
            Assert.That(carcass.SourceId, Is.EqualTo(wolf.Id));
            Assert.That(carcass.DeathCause, Is.EqualTo(AnimalDeathCause.Predation));
            Assert.That(vole.RngState, Is.EqualTo(rng));
            CombatSystem.Update(world, events);
            world.Ecology.ResolveDeaths(world, events);
            Assert.That(world.Ecology.Carcasses.Count, Is.EqualTo(carcasses + 1));
            Assert.That(world.Ecology.PredationDeaths, Is.EqualTo(predation + 1));
            Assert.That(world.Ecology.HuntKills, Is.Zero);
            Assert.That(events.Count(e => e.Kind == SimEventKind.AnimalDied && e.Source == vole.Id), Is.EqualTo(1));
            Assert.That(EcologyBalance.CurrentBiomass(world.Ecology), Is.EqualTo(biomass));
            NoLocalEffects(world);
            Conserved(world);
            CombatSnapshot.Validate(world);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void FullSnapshot_RegionalPreparationRecoveryCooldown_ContinuesWithIdenticalDamageDeathsAndHashes(int elapsed)
        {
            WorldState original = Create();
            AnimalState wolf = Resident(original, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(original, AnimalSpecies.Rabbit);
            CombatSystem.RequestAnimalAttack(original, wolf, rabbit, null);
            Minute(original);
            for (int i = 0; i < elapsed; i++) { Minute(original); }
            CombatAttackState savedAttack = original.Combat.GetAttack(wolf.Id);
            Assert.That(savedAttack.Phase, Is.EqualTo(elapsed == 0 ? CombatAttackPhase.Preparing :
                elapsed == 1 ? CombatAttackPhase.Recovery : CombatAttackPhase.Cooldown));
            byte[] saved = WorldSnapshot.Capture(original);
            WorldState restored = WorldSnapshot.Restore(saved);
            CollectionAssert.AreEqual(saved, WorldSnapshot.Capture(restored));
            long hitsBefore = original.Combat.Hits;
            for (int i = 0; i < 10; i++)
            {
                foreach (WorldState branch in new[] { original, restored })
                {
                    AnimalState source = branch.Ecology.Find(wolf.Id.Value);
                    AnimalState target = branch.Ecology.Find(rabbit.Id.Value);
                    if (target != null) { CombatSystem.RequestAnimalAttack(branch, source, target, null); }
                    Minute(branch, ecology: true);
                    NoLocalEffects(branch);
                    Conserved(branch);
                }
                CollectionAssert.AreEqual(WorldSnapshot.Capture(original), WorldSnapshot.Capture(restored), "Continuation minute " + i);
                Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(original)));
            }
            Assert.That(original.Combat.Hits, Is.GreaterThan(hitsBefore));
            Assert.That(original.Ecology.Find(rabbit.Id.Value), Is.Null);
            Assert.That(restored.Ecology.FindCarcass(rabbit.Id.Value).SourceId, Is.EqualTo(wolf.Id));
            Assert.That(restored.Ecology.FindCarcass(rabbit.Id.Value).DeathCause, Is.EqualTo(AnimalDeathCause.Predation));
        }

        [TestCase("region")]
        [TestCase("habitat")]
        [TestCase("geometry")]
        [TestCase("timing")]
        [TestCase("target-dead")]
        [TestCase("target-missing")]
        [TestCase("actor-missing")]
        [TestCase("target-moved")]
        [TestCase("actor-moved")]
        [TestCase("target-type")]
        public void Snapshot_RegionalExecutableValidationIsAtomic(string invalid)
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            CombatSystem.RequestAnimalAttack(world, wolf, rabbit, null);
            CombatAttackState attack = world.Combat.GetAttack(wolf.Id);
            byte[] good = CombatSnapshot.Capture(world);
            switch (invalid)
            {
                case "region": attack.Region = 999; break;
                case "habitat": attack.HabitatId = 0; break;
                case "geometry": attack.FacingX = 1; break;
                case "timing": attack.ReleaseTick++; break;
                case "target-dead": rabbit.Health = 0; break;
                case "target-missing": world.Ecology.MutableAnimals.Remove(rabbit); break;
                case "actor-missing": world.Ecology.MutableAnimals.Remove(wolf); break;
                case "target-moved": rabbit.Region = 1; break;
                case "actor-moved": wolf.Region = 1; break;
                case "target-type": attack.TargetIsAnimal = false; break;
            }
            byte[] bad = CombatSnapshot.Capture(world);
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Validate(world));
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Restore(bad, world));
            CollectionAssert.AreEqual(bad, CombatSnapshot.Capture(world));
            if (invalid == "region" || invalid == "habitat" || invalid == "geometry" || invalid == "timing" || invalid == "target-type")
            {
                CombatSnapshot.Restore(good, world);
                CollectionAssert.AreEqual(good, CombatSnapshot.Capture(world));
            }
        }

        [Test]
        public void ExternalCounterattack_IsPreparedSeparately_WithNoSameMinuteDamageOrLocalEffects()
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState boar = Resident(world, AnimalSpecies.Boar);
            int health = wolf.Health;
            CombatSystem.RequestAnimalAttack(world, wolf, boar, null);
            Minute(world);
            Minute(world);
            CombatAttackState reaction = world.Combat.GetAttack(boar.Id);
            Assert.That(reaction.IsCounterattack, Is.True);
            Assert.That(reaction.Region, Is.Zero);
            Assert.That(reaction.HabitatId, Is.EqualTo(boar.HabitatId));
            Assert.That(reaction.Phase, Is.EqualTo(CombatAttackPhase.Preparing));
            Assert.That(wolf.Health, Is.EqualTo(health));
            Assert.That(world.Combat.Counterattacks, Is.EqualTo(1));
            CombatSnapshot.Validate(world);
            Minute(world);
            Assert.That(wolf.Health, Is.EqualTo(health - 18));
            Assert.That(world.Combat.GetLastDamage(wolf.Id).Value.Source, Is.EqualTo(boar.Id));
            Assert.That(world.Combat.Hits, Is.EqualTo(2));
            NoLocalEffects(world);
            Conserved(world);
        }

        [Test]
        public void LocalLethalPredation_KeepsHunterReservationUntilUniqueEcologyCarcassTransfer()
        {
            WorldState world = Create();
            NpcState hunter = WorldFactory.SpawnNpc(world, world.Center, true, new List<SimEvent>());
            AnimalState wolf = world.Ecology.Spawn(world, AnimalSpecies.Wolf, world.Center, AnimalDefinition.Wolf.MaturityTicks);
            AnimalState vole = world.Ecology.Spawn(world, AnimalSpecies.Vole, world.Center, AnimalDefinition.Vole.MaturityTicks);
            vole.ReservedBy = hunter.Id;
            CombatSystem.RequestAnimalAttack(world, wolf, vole, null);
            Minute(world);
            var events = new List<SimEvent>();
            CombatSystem.Update(world, events);
            Assert.That(vole.Health, Is.Zero);
            Assert.That(vole.ReservedBy, Is.EqualTo(hunter.Id));
            Assert.That(vole.LastDamageSourceId, Is.EqualTo(wolf.Id));
            Assert.That(vole.DeathCause, Is.EqualTo(AnimalDeathCause.Predation));
            world.Ecology.ResolveDeaths(world, events);
            Assert.That(world.Ecology.FindCarcass(vole.Id.Value).ReservedBy, Is.EqualTo(hunter.Id));
            Assert.That(vole.ReservedBy.IsNone, Is.True);
            Assert.That(hunter.HuntKills, Is.Zero);
            Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Snapshot_ReleasedRegionalAttackAllowsMigratedOrHistoricalActors_WithoutFurtherDamage(bool missing)
        {
            WorldState world = Create();
            AnimalState wolf = Resident(world, AnimalSpecies.Wolf);
            AnimalState rabbit = Resident(world, AnimalSpecies.Rabbit);
            CombatSystem.RequestAnimalAttack(world, wolf, rabbit, null);
            Minute(world);
            Minute(world);
            if (missing)
            {
                wolf.Health = rabbit.Health = 0;
                wolf.DeathCause = AnimalDeathCause.Injury;
                rabbit.DeathCause = AnimalDeathCause.Predation;
                world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            }
            else
            {
                wolf.Region = rabbit.Region = 1;
                wolf.HabitatId = rabbit.HabitatId = world.Ecology.Regions.Regions[1].HabitatId;
            }
            byte[] save = CombatSnapshot.Capture(world);
            Assert.DoesNotThrow(() => CombatSnapshot.Restore(save, world));
            long hits = world.Combat.Hits;
            for (int i = 0; i < 4; i++) { Minute(world); }
            Assert.That(world.Combat.Hits, Is.EqualTo(hits));
            Assert.That(world.Combat.Attacks, Is.Empty);
            NoLocalEffects(world);
            Conserved(world);
        }
    }
}
