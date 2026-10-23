using System.Collections.Generic;
using System.Linq;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class RescueCombatOrderingTests
    {
        private static WorldState RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step)
        {
            var world = new WorldState(new WorldConfigData(42, 16, 16, 3, 360, 2, new[] { 1 }),
                new WorldMap(16, 16), new DeterministicRng(42));
            var shooter = AddNpc(world, "Shooter", new GridPos(2, 5));
            patient = AddNpc(world, "Patient", new GridPos(8, 8));
            rescuer = AddNpc(world, "Rescuer", new GridPos(9, 7));
            shooter.Inventory.TryAdd(ItemKind.Arrow, 4);
            patient.Position = patient.Home.Offset(1, 0);
            patient.Needs.Health = 0;
            patient.Needs.Hunger = 1000;
            patient.Needs.Thirst = 1000;
            patient.Survival.Life = LifeState.Downed;
            patient.Survival.RescueDeadline = world.Tick + SurvivalRules.RescueWindow;
            patient.Survival.Rescuer = rescuer.Id;
            patient.Survival.DiseaseTicks = 100;
            rescuer.Inventory.TryAdd(ItemKind.Medicine, 2);
            rescuer.Inventory.TryAdd(ItemKind.Food, 1);
            rescuer.Inventory.TryAdd(ItemKind.Water, 1);
            step = new PlanStep(PlanStepType.Rescue, patient.Position, patient.Id.Value);
            rescuer.Plan.Start(NpcGoalType.Rescue, new List<PlanStep> { step });
            world.Environment.Temperature = 1800;
            world.Environment.Daylight = 10000;
            world.Materials.Initialize(world);
            return world;
        }

        private static NpcState AddNpc(WorldState world, string name, GridPos home)
        {
            var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity(name, "Builder", 25),
                new Personality(), home, (ulong)(42 + world.Entities.Npcs.Count));
            world.Entities.AddNpc(npc);
            return npc;
        }

        // Only the initial trajectory is specified; the real combat sweep resolves contact.
        private static void Arrow(WorldState world, int x, int y, int vx, int damage)
        {
            NpcState shooter = world.Entities.Npcs[0];
            Assert.That(shooter.Inventory.TryRemove(ItemKind.Arrow, 1), Is.True);
            var arrow = new ProjectileState(world.Combat.NextProjectileId++, shooter.Id)
            {
                AttackId = world.Combat.NextAttackId++, Phase = ProjectilePhase.Flying,
                X = x, Y = y, VelocityX = vx, VelocityY = 0,
                ReleasedTick = world.Tick - 1, LastAdvancedTick = world.Tick - 1,
                RemainingTicks = CombatState.ArrowLifetime, Damage = damage, Integrity = 1,
                LastWalkablePosition = ProjectileSystem.Cell(x, y), IgnoreInitialShooterOverlap = true
            };
            world.Combat.MutableProjectiles.Add(arrow);
            world.Combat.ShotsReleased++;
        }

        private static void Conserved(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString());
            }
        }

        [Test]
        public void Prepare_AdjacentDownedPatient_OnlyStartsActionAndMarksReady()
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            GridPos position = patient.Position, helperPosition = rescuer.Position;
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            Assert.That(rescuer.Plan.StepStarted, Is.True);
            Assert.That(rescuer.Action.Type, Is.EqualTo(NpcActionType.Work));
            Assert.That(rescuer.Action.EndTick, Is.EqualTo(world.Tick + SurvivalRules.RescueWindow));
            Assert.That(patient.Position, Is.EqualTo(position));
            Assert.That(rescuer.Position, Is.EqualTo(helperPosition));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Downed));
            Assert.That(patient.Needs.Health, Is.Zero);
            Assert.That(patient.Survival.DiseaseTicks, Is.EqualTo(100));
            Assert.That(patient.Inventory.TotalCount, Is.Zero);
            Assert.That(rescuer.Inventory.Count(ItemKind.Medicine), Is.EqualTo(2));
            Assert.That(world.Materials.Consumed(ItemKind.Medicine), Is.Zero);
            Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Combat_LethallyHitsReadyHelper_NoTreatmentOrBedsideFeeding(bool bedside)
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            if (bedside)
            {
                patient.Position = patient.Home;
                patient.Survival.Life = LifeState.Recovering;
                patient.Needs.Health = 1500;
                patient.Inventory.TryAdd(ItemKind.Seed, patient.Inventory.Capacity);
                rescuer.Position = patient.Home.Offset(0, -1);
                world.Materials.Initialize(world);
            }
            rescuer.Needs.Health = 10;
            int patientHealth = patient.Needs.Health;
            Arrow(world, 5 * 1024 + 512, 7 * 1024 + 512, 8 * 1024, 11);
            var events = new List<SimEvent>();
            CombatSystem.RecordPositions(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            CombatSystem.Update(world, events);
            Assert.That(world.Combat.GetLastDamage(rescuer.Id), Is.Not.Null);
            Assert.That(rescuer.Needs.Health, Is.Zero);
            // Life is still Active until the engine's health pass: health must also be checked.
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out bool complete), Is.False);
            Assert.That(complete, Is.False);
            HealthSystem.Update(world, rescuer, events);
            Assert.That(rescuer.Survival.Life, Is.EqualTo(LifeState.Downed));
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out complete), Is.False);
            Assert.That(patient.Needs.Health, Is.EqualTo(patientHealth));
            Assert.That(patient.Needs.Food, Is.EqualTo(1000));
            Assert.That(patient.Needs.Water, Is.EqualTo(1000));
            Assert.That(patient.Survival.DiseaseTicks, Is.EqualTo(100));
            Assert.That(rescuer.Inventory.Count(ItemKind.Medicine), Is.EqualTo(2));
            Assert.That(rescuer.Inventory.Food, Is.EqualTo(1));
            Assert.That(rescuer.Inventory.Water, Is.EqualTo(1));
            Assert.That(rescuer.Survival.Rescues, Is.Zero);
            Assert.That(events.Any(e => e.Kind == SimEventKind.NpcRescued), Is.False);
            Conserved(world);
        }

        [Test]
        public void Prepare_ApproachMovement_IsIncludedInCombatSweep()
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            rescuer.Position = new GridPos(9, 5);
            // Midway between the helper's old/new rows: neither stationary endpoint intersects.
            Arrow(world, 6 * 1024 + 512, 6 * 1024, 5 * 1024, 11);
            CombatSystem.RecordPositions(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.False);
            Assert.That(rescuer.Position, Is.EqualTo(new GridPos(9, 6)));
            CombatSystem.Update(world, new List<SimEvent>());
            Assert.That(rescuer.Needs.Health, Is.EqualTo(9989));
            Assert.That(world.Combat.TraceSegments.Single().HitId, Is.EqualTo(rescuer.Id));
            Assert.That(patient.Needs.Health, Is.Zero);
            Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Prepare_TransportSweepHitsPatient_CommitNeverReappliesStabilization(bool lethal)
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            patient.Position = patient.Home.Offset(0, -1);
            rescuer.Position = patient.Position.Offset(1, 0);
            var events = new List<SimEvent>();
            CombatSystem.RecordPositions(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool treatmentReady), Is.True);
            Assert.That(treatmentReady, Is.True);
            CombatSystem.Update(world, events);
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out bool treatedComplete), Is.True);
            Assert.That(treatedComplete, Is.False);
            Assert.That(patient.Needs.Health, Is.EqualTo(1500));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Recovering));
            world.Tick++;
            // Cross the actually treated patient half-way through its one-cell journey home.
            Arrow(world, 5 * 1024 + 512, 8 * 1024, 5 * 1024, lethal ? 2000 : 11);
            CombatSystem.RecordPositions(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            Assert.That(patient.Position, Is.EqualTo(patient.Home));
            Assert.That(rescuer.Position, Is.EqualTo(patient.Home));
            CombatSystem.Update(world, events);
            Assert.That(world.Combat.TraceSegments.Single().HitId, Is.EqualTo(patient.Id));
            Assert.That(patient.Needs.Health, Is.EqualTo(lethal ? 0 : 1489));
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out bool complete), Is.EqualTo(!lethal));
            Assert.That(complete, Is.EqualTo(!lethal));
            Assert.That(patient.Needs.Health, Is.EqualTo(lethal ? 0 : 1489));
            Assert.That(rescuer.Inventory.Count(ItemKind.Medicine), Is.EqualTo(1));
            Assert.That(world.Materials.Consumed(ItemKind.Medicine), Is.EqualTo(1));
            Assert.That(rescuer.Survival.Rescues, Is.EqualTo(lethal ? 0 : 1));
            if (lethal)
            {
                HealthSystem.Update(world, patient, events);
                Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Downed));
                Assert.That(patient.Survival.Rescuer.IsNone, Is.True);
                Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out complete), Is.False);
                Assert.That(patient.Needs.Health, Is.Zero);
            }
            Assert.That(events.Count(e => e.Kind == SimEventKind.NpcRescued), Is.EqualTo(lethal ? 0 : 1));
            Conserved(world);
        }

        [Test]
        public void Commit_StabilizesOnceWithoutPostCombatMovement_ThenTransportFinishesOnce()
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            patient.Position = patient.Home.Offset(3, 0);
            rescuer.Position = patient.Position;
            patient.Survival.BodyTemperature = 3000;
            patient.Survival.Wetness = 7000;
            GridPos position = patient.Position;
            var events = new List<SimEvent>();
            CombatSystem.RecordPositions(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            CombatSystem.Update(world, events);
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out bool complete), Is.True);
            Assert.That(complete, Is.False);
            Assert.That(patient.Position, Is.EqualTo(position));
            Assert.That(rescuer.Position, Is.EqualTo(position));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Recovering));
            Assert.That(patient.Needs.Health, Is.EqualTo(1500));
            Assert.That(patient.Survival.DiseaseTicks, Is.Zero);
            Assert.That(patient.Survival.BodyTemperature, Is.EqualTo(3000));
            Assert.That(patient.Survival.Wetness, Is.EqualTo(7000));
            Assert.That(patient.Inventory.Food, Is.EqualTo(1));
            Assert.That(patient.Inventory.Water, Is.EqualTo(1));
            Assert.That(world.Materials.Consumed(ItemKind.Medicine), Is.EqualTo(1));
            Assert.That(world.Materials.Consumed(ItemKind.Food), Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.Water), Is.Zero);
            Assert.That(rescuer.Survival.Rescues, Is.Zero);
            Assert.That(patient.Survival.Rescuer, Is.EqualTo(rescuer.Id));
            for (int i = 0; i < 3; i++)
            {
                world.Tick++;
                CombatSystem.RecordPositions(world);
                GridPos previous = patient.Position;
                Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out ready), Is.True);
                Assert.That(previous.ManhattanDistance(patient.Position), Is.EqualTo(1));
                Assert.That(ready, Is.EqualTo(i == 2));
                CombatSystem.Update(world, events);
                if (ready) { Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out complete), Is.True); }
                Conserved(world);
            }
            Assert.That(complete, Is.True);
            Assert.That(patient.Position, Is.EqualTo(patient.Home));
            Assert.That(patient.Survival.Rescuer.IsNone, Is.True);
            Assert.That(rescuer.Survival.Rescues, Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == SimEventKind.NpcRescued), Is.EqualTo(1));
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out complete), Is.False);
            Assert.That(complete, Is.False);
            Assert.That(world.Materials.Consumed(ItemKind.Medicine), Is.EqualTo(1));
            Assert.That(rescuer.Survival.Rescues, Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void Commit_WithoutMedicine_FailsWithoutAnyTreatmentOrTransfer()
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            rescuer.Inventory.TryRemove(ItemKind.Medicine, 2);
            world.Materials.Initialize(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, new List<SimEvent>(), out bool complete), Is.False);
            Assert.That(complete, Is.False);
            Assert.That(patient.Needs.Health, Is.Zero);
            Assert.That(patient.Survival.DiseaseTicks, Is.EqualTo(100));
            Assert.That(patient.Inventory.TotalCount, Is.Zero);
            Assert.That(rescuer.Inventory.Food, Is.EqualTo(1));
            Assert.That(rescuer.Inventory.Water, Is.EqualTo(1));
            Conserved(world);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Commit_RevalidatesClaimLifeAdjacencyAndAction(int change)
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            switch (change)
            {
                case 0: patient.Survival.Rescuer = world.Entities.Npcs[0].Id; break;
                case 1: patient.Survival.Life = LifeState.Dead; break;
                case 2: rescuer.Position = new GridPos(1, 1); break;
                case 3: rescuer.Survival.Life = LifeState.Recovering; break;
                case 4: world.Tick = rescuer.Action.EndTick; break;
                case 5: rescuer.Plan.StepStarted = false; break;
            }
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, new List<SimEvent>(), out bool complete), Is.False);
            Assert.That(complete, Is.False);
            Assert.That(patient.Needs.Health, Is.Zero);
            Assert.That(patient.Inventory.TotalCount, Is.Zero);
            Assert.That(rescuer.Inventory.Count(ItemKind.Medicine), Is.EqualTo(2));
            Assert.That(rescuer.Survival.Rescues, Is.Zero);
            Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Prepare_ExpiredActionOrBlockedRoute_FailsWithoutEffects(bool expired)
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            if (expired) { world.Tick = rescuer.Action.EndTick; }
            else
            {
                rescuer.Position = new GridPos(1, 1);
                world.Map.SetTile(new GridPos(0, 1), TileType.Rock);
                world.Map.SetTile(new GridPos(2, 1), TileType.Rock);
                world.Map.SetTile(new GridPos(1, 0), TileType.Rock);
                world.Map.SetTile(new GridPos(1, 2), TileType.Rock);
            }
            GridPos position = rescuer.Position;
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out ready), Is.False);
            Assert.That(ready, Is.False);
            Assert.That(rescuer.Position, Is.EqualTo(position));
            Assert.That(patient.Needs.Health, Is.Zero);
            Assert.That(rescuer.Inventory.Count(ItemKind.Medicine), Is.EqualTo(2));
            Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Commit_HomeCareUsesRealProvisions_ColdHomeCannotRestoreHealth(bool fullBag)
        {
            WorldState world = RescueWorld(out NpcState patient, out NpcState rescuer, out PlanStep step);
            patient.Position = patient.Home;
            rescuer.Position = patient.Home.Offset(0, -1);
            patient.Survival.BodyTemperature = 3400;
            patient.Survival.Wetness = 7000;
            if (fullBag) { patient.Inventory.TryAdd(ItemKind.Seed, patient.Inventory.Capacity); }
            var shelter = new ShelterState(world.Survival.NextId++, patient.Home.Offset(0, 1), patient.Home, 1);
            shelter.Temperature = 0;
            world.Map.SetTile(shelter.Position, TileType.House);
            world.Survival.MutableShelters.Add(shelter);
            patient.Survival.ShelterId = shelter.Id;
            world.Materials.Initialize(world);
            var events = new List<SimEvent>();
            CombatSystem.RecordPositions(world);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            CombatSystem.Update(world, events);
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, events, out bool complete), Is.True);
            Assert.That(complete, Is.True);
            Assert.That(patient.Needs.Health, Is.EqualTo(1500));
            Assert.That(patient.Survival.BodyTemperature, Is.EqualTo(3400));
            Assert.That(patient.Survival.Wetness, Is.EqualTo(7000));
            Assert.That(rescuer.Inventory.Food, Is.Zero);
            Assert.That(rescuer.Inventory.Water, Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.Food), Is.EqualTo(fullBag ? 1 : 0));
            Assert.That(world.Materials.Consumed(ItemKind.Water), Is.EqualTo(fullBag ? 1 : 0));
            Assert.That(HealthSystem.Update(world, patient, events), Is.False);
            Assert.That(patient.Needs.Food, Is.EqualTo(5500));
            Assert.That(patient.Needs.Water, Is.EqualTo(6500));
            Assert.That(patient.Needs.Health, Is.EqualTo(1500));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Recovering));
            Assert.That(world.Materials.Consumed(ItemKind.Food), Is.EqualTo(1));
            Assert.That(world.Materials.Consumed(ItemKind.Water), Is.EqualTo(1));
            ThermalSystem.UpdateNpc(world, patient);
            int coldHealth = patient.Needs.Health;
            Assert.That(coldHealth, Is.LessThan(1500));
            HealthSystem.Update(world, patient, events);
            Assert.That(patient.Needs.Health, Is.EqualTo(coldHealth));
            // Each warmth constraint is independent: a dry/warm body in a cold home,
            // a cold body in a warm home, and a wet body must all stay recovering.
            patient.Needs.Health = 4000;
            patient.Survival.BodyTemperature = 3700;
            patient.Survival.Wetness = 0;
            HealthSystem.Update(world, patient, events);
            Assert.That(patient.Needs.Health, Is.EqualTo(4000));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Recovering));
            shelter.Temperature = 1800;
            patient.Survival.BodyTemperature = 3550;
            HealthSystem.Update(world, patient, events);
            Assert.That(patient.Needs.Health, Is.EqualTo(4000));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Recovering));
            patient.Survival.BodyTemperature = 3700;
            patient.Survival.Wetness = 7000;
            HealthSystem.Update(world, patient, events);
            Assert.That(patient.Needs.Health, Is.EqualTo(4000));
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Recovering));
            patient.Survival.Wetness = 0;
            Assert.That(HealthSystem.Update(world, patient, events), Is.True);
            Assert.That(patient.Survival.Life, Is.EqualTo(LifeState.Active));
            Assert.That(patient.Needs.Health, Is.EqualTo(4012));
            Assert.That(rescuer.Survival.Rescues, Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == SimEventKind.NpcRescued), Is.EqualTo(1));
            Conserved(world);
        }
    }
}
