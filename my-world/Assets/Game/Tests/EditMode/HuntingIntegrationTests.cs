using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class HuntingIntegrationTests
    {
        private static WorldState Create(int count = 1)
        {
            WorldState world = WorldFactory.Create(new WorldConfigData(42, 32, 32, count, 720, 2, new[] { 1 }));
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Position = world.Center;
                npc.Action.SetIdle("Controlled hunting", long.MaxValue);
            }
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                animal.NextDecisionTick = long.MaxValue;
                animal.NextMoveTick = long.MaxValue;
            }
            return world;
        }

        private static AnimalState Prey(WorldState world, int distance)
        {
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Species != AnimalSpecies.Rabbit) { continue; }
                animal.Position = world.Center.Offset(distance, 0);
                animal.HabitatId = world.Ecology.HabitatAt(animal.Position).Id;
                animal.ReservedBy = world.Entities.Npcs[0].Id;
                return animal;
            }
            throw new InvalidOperationException("Missing controlled rabbit.");
        }

        private static CarcassState Corpse(WorldState world, int distance)
        {
            AnimalState animal = Prey(world, distance);
            animal.Health = 0;
            animal.DeathCause = AnimalDeathCause.Hunt;
            animal.LastDamageSourceId = world.Entities.Npcs[0].Id;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            return world.Ecology.FindCarcass(animal.Id.Value);
        }

        private static void Hunt(WorldState world, int id, GridPos target)
        {
            world.Entities.Npcs[0].Plan.Start(NpcGoalType.Hunt,
                new List<PlanStep> { new PlanStep(PlanStepType.Hunt, target, id, durationTicks: 120) });
        }

        private static void Conserved(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString());
            }
            Assert.That(world.Market.CurrencyError(world), Is.Zero);
            Assert.That(world.Ecology.Balance.BiomassError(world.Ecology), Is.Zero);
        }

        [Test]
        public void LethalCombat_AttributesOneUniqueCarcassWithoutProducingReadyFood()
        {
            WorldState world = Create();
            NpcState hunter = world.Entities.Npcs[0];
            AnimalState animal = Prey(world, 1);
            int kills = hunter.HuntKills;
            long food = world.Materials.Produced(ItemKind.Food);
            CombatSystem.ApplyDamage(world, hunter.Id, false, animal.Id, true, 10000, 1, 0, 65536, new List<SimEvent>());
            Assert.That(world.Ecology.FindCarcass(animal.Id.Value), Is.Null);
            Assert.That(animal.LastDamageSourceId, Is.EqualTo(hunter.Id));
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            CarcassState carcass = world.Ecology.FindCarcass(animal.Id.Value);
            Assert.That(carcass.SourceId, Is.EqualTo(hunter.Id));
            Assert.That(carcass.DeathCause, Is.EqualTo(AnimalDeathCause.Hunt));
            Assert.That(carcass.ReservedBy, Is.EqualTo(hunter.Id));
            Assert.That(hunter.HuntKills, Is.EqualTo(kills + 1));
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            Assert.That(hunter.HuntKills, Is.EqualTo(kills + 1));
            Assert.That(world.Materials.Produced(ItemKind.Food), Is.EqualTo(food));
            Conserved(world);
        }

        [Test]
        public void Retrieval_CollectsActualMeatHideAndGroundedAmmo_NotExpectedFoodYield()
        {
            WorldState world = Create();
            NpcState hunter = world.Entities.Npcs[0];
            CarcassState carcass = Corpse(world, 1);
            int meat = carcass.MeatRemaining, hide = carcass.HideRemaining;
            world.Survival.Drop(carcass.Position).Inventory.TryAdd(ItemKind.Arrow, 1);
            world.Materials.Initialize(world);
            long food = world.Materials.Produced(ItemKind.Food);
            Hunt(world, carcass.Id.Value, carcass.Position);
            new SimulationEngine().Tick(world, null);
            Assert.That(hunter.Inventory.Count(ItemKind.RawMeat), Is.EqualTo(meat));
            Assert.That(hunter.Inventory.Count(ItemKind.Hide), Is.EqualTo(hide));
            Assert.That(hunter.Inventory.Count(ItemKind.Arrow), Is.EqualTo(1));
            Assert.That(carcass.MeatRemaining + carcass.HideRemaining, Is.Zero);
            Assert.That(hunter.Plan.IsActive, Is.False);
            Assert.That(hunter.Survival.HuntDropId, Is.Zero);
            Assert.That(world.Materials.Produced(ItemKind.Food), Is.EqualTo(food));
            Conserved(world);
        }

        [Test]
        public void Retrieval_OneFreeSlotHarvestsOneUnitAndLeavesRealRemainderForNextTrip()
        {
            WorldState world = Create();
            NpcState hunter = world.Entities.Npcs[0];
            CarcassState carcass = Corpse(world, 1);
            int meat = carcass.MeatRemaining;
            hunter.Inventory.TryAdd(ItemKind.Arrow, hunter.Inventory.Capacity - hunter.Inventory.TotalCount - 1);
            world.Materials.Initialize(world);
            Hunt(world, carcass.Id.Value, carcass.Position);
            new SimulationEngine().Tick(world, null);
            Assert.That(hunter.Inventory.Count(ItemKind.RawMeat), Is.EqualTo(1));
            Assert.That(carcass.MeatRemaining, Is.EqualTo(meat - 1));
            Assert.That(carcass.ReservedBy.IsNone, Is.True);
            Assert.That(hunter.Plan.IsActive, Is.False);
            Conserved(world);
        }

        [Test]
        public void Retrieval_HunterDownedDuringCombatCannotCommitCarcassWork()
        {
            WorldState world = Create(2);
            NpcState hunter = world.Entities.Npcs[0], shooter = world.Entities.Npcs[1];
            shooter.Position = world.Center.Offset(-5, 0);
            CarcassState carcass = Corpse(world, 1);
            int meat = carcass.MeatRemaining, hide = carcass.HideRemaining;
            var arrow = new ProjectileState(world.Combat.NextProjectileId++, shooter.Id)
            {
                AttackId = world.Combat.NextAttackId++, Phase = ProjectilePhase.Flying,
                X = (world.Center.X - 1) * 1024 + 512, Y = world.Center.Y * 1024 + 512,
                VelocityX = 2048, VelocityY = 0, ReleasedTick = world.Tick - 1,
                LastAdvancedTick = world.Tick - 1, RemainingTicks = 4, Damage = 10000, Integrity = 1,
                LastWalkablePosition = world.Center.Offset(-1, 0)
            };
            world.Combat.MutableProjectiles.Add(arrow);
            world.Materials.Initialize(world);
            Hunt(world, carcass.Id.Value, carcass.Position);
            new SimulationEngine().Tick(world, null);
            Assert.That(hunter.Survival.Life, Is.EqualTo(LifeState.Downed));
            Assert.That(hunter.Inventory.Count(ItemKind.RawMeat), Is.Zero);
            Assert.That(carcass.MeatRemaining, Is.EqualTo(meat));
            Assert.That(carcass.HideRemaining, Is.EqualTo(hide));
            Assert.That(carcass.ReservedBy.IsNone, Is.True);
            Conserved(world);
        }

        [Test]
        public void Retrieval_SaveMidRouteRestoresClaimAndContinuesWithIdenticalHashes()
        {
            WorldState world = Create();
            CarcassState carcass = Corpse(world, 4);
            Hunt(world, carcass.Id.Value, carcass.Position);
            var engine = new SimulationEngine();
            engine.Tick(world, null);
            Assert.That(world.Entities.Npcs[0].Survival.HuntDropId, Is.EqualTo(carcass.Id.Value));
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            var branch = new SimulationEngine();
            for (int i = 0; i < 8; i++)
            {
                Assert.That(branch.Tick(restored, null).StateHash, Is.EqualTo(engine.Tick(world, null).StateHash));
                Conserved(world);
                Conserved(restored);
            }
        }

        [Test]
        public void UnseenPrey_TracksLastObservedCellInsteadOfFollowingHiddenCurrentPosition()
        {
            WorldState world = Create();
            NpcState hunter = world.Entities.Npcs[0];
            AnimalState animal = Prey(world, 3);
            GridPos lastSeen = animal.Position;
            Hunt(world, animal.Id.Value, lastSeen);
            hunter.Survival.SetTool(ItemKind.Bow, 100);
            hunter.Inventory.TryAdd(ItemKind.Arrow, 2);
            hunter.Plan.StepStarted = true;
            hunter.Action.SetTimed(NpcActionType.Work, "Hunt: track", lastSeen, world.Tick + 120);
            animal.Position = new GridPos(1, 1);
            animal.HabitatId = world.Ecology.HabitatAt(animal.Position).Id;
            world.Materials.Initialize(world);
            new SimulationEngine().Tick(world, null);
            Assert.That(hunter.Action.Target, Is.EqualTo(lastSeen));
            Assert.That(hunter.Position, Is.EqualTo(world.Center.Offset(1, 0)));
            Assert.That(world.Combat.GetAttack(hunter.Id), Is.Null);
            Conserved(world);
        }
    }
}
