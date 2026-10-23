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
    public sealed class CombatProjectileTests
    {
        private static WorldState Empty(int seed = 42)
        {
            var world = new WorldState(new WorldConfigData(seed, 32, 32, 1, 360, 2, new[] { 1 }),
                new WorldMap(32, 32), new DeterministicRng((ulong)seed));
            var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("Hunter", "Hunter", 25),
                new Personality(), new GridPos(5, 5), (ulong)seed);
            npc.Needs.Fatigue = 10000;
            npc.HuntingSkill = 100;
            npc.Survival.SetTool(ItemKind.Bow, SurvivalRules.ToolDurability);
            npc.Survival.SetTool(ItemKind.Spear, SurvivalRules.ToolDurability);
            npc.Inventory.TryAdd(ItemKind.Arrow, 6);
            world.Entities.AddNpc(npc);
            world.Environment.Daylight = 10000;
            world.Environment.Temperature = 1000;
            world.Materials.Initialize(world);
            return world;
        }
        private static AnimalState Prey(WorldState world, int x, int y, AnimalSpecies species = AnimalSpecies.Rabbit)
        {
            AnimalState animal = world.Ecology.Spawn(world, species, new GridPos(x, y), 1500);
            animal.ReservedBy = world.Entities.Npcs[0].Id;
            return animal;
        }
        private static void Minute(WorldState world, Action movement = null)
        {
            CombatSystem.RecordPositions(world);
            movement?.Invoke();
            CombatSystem.Update(world, new List<SimEvent>());
            world.Tick++;
        }
        private static ProjectileState Release(WorldState world, AnimalState prey)
        {
            Assert.That(CombatSystem.TryStartHunt(world, world.Entities.Npcs[0], prey, new List<SimEvent>()), Is.True);
            Minute(world); Minute(world); Minute(world);
            Assert.That(world.Combat.Projectiles.Count, Is.EqualTo(1));
            return world.Combat.Projectiles[0];
        }
        // Inject a specified initial trajectory, not a hit result: all contact/damage and
        // material transfer still run through the real production sweep implementation.
        private static ProjectileState Arrow(WorldState world, GridPos from, int vx, int vy = 0, int damage = 11, int integrity = 1)
        {
            NpcState shooter = world.Entities.Npcs[0];
            Assert.That(shooter.Inventory.TryRemove(ItemKind.Arrow, 1), Is.True);
            var arrow = new ProjectileState(world.Combat.NextProjectileId++, shooter.Id)
            {
                AttackId = world.Combat.NextAttackId++, Phase = ProjectilePhase.Flying,
                X = from.X * 1024 + 512, Y = from.Y * 1024 + 512, VelocityX = vx, VelocityY = vy,
                ReleasedTick = world.Tick - 1, LastAdvancedTick = world.Tick - 1,
                RemainingTicks = CombatState.ArrowLifetime, Damage = damage, Integrity = integrity,
                LastWalkablePosition = from, IgnoreInitialShooterOverlap = true
            };
            world.Combat.MutableProjectiles.Add(arrow);
            world.Combat.ShotsReleased++;
            return arrow;
        }
        private static void Conserved(WorldState world)
        {
            foreach (ItemKind item in Enum.GetValues(typeof(ItemKind)))
            {
                if (item != ItemKind.Count) { Assert.That(world.Materials.Error(world, item), Is.Zero, item.ToString()); }
            }
        }
        private static void Depart(WorldState world, AnimalState animal)
        {
            animal.MigrationDestination = world.Ecology.Regions.Regions[1].Id;
            MigrationSystem.Update(world, new List<SimEvent>());
            Assert.That(animal.Region, Is.EqualTo(1)); Assert.That(animal.Position, Is.EqualTo(GridPos.Invalid));
        }

        [Test]
        public void Hunt_RequestCreatesPersistentAim_DelaysAmmoEnergyWearAndDamage()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); NpcState npc = world.Entities.Npcs[0];
            int health = prey.Health, energy = npc.Needs.Energy, arrows = npc.Inventory.Count(ItemKind.Arrow);
            ulong rng = world.Combat.RngState, npcRng = npc.RngState, animalRng = prey.RngState;
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.True);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.False);
            CombatAttackState attack = world.Combat.GetAttack(npc.Id);
            Assert.That(attack.Phase, Is.EqualTo(CombatAttackPhase.Preparing));
            Assert.That(attack.FacingX, Is.EqualTo(4096));
            npc.Action.SetIdle("Unrelated timer", 0);
            Minute(world); Minute(world);
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(arrows));
            Assert.That(npc.Needs.Energy, Is.EqualTo(energy)); Assert.That(world.Combat.RngState, Is.EqualTo(rng));
            Minute(world);
            Assert.That(attack.Phase, Is.EqualTo(CombatAttackPhase.Recovery)); Assert.That(attack.ReleasedTick, Is.EqualTo(362));
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(arrows - 1));
            Assert.That(npc.Survival.Durability(ItemKind.Bow), Is.EqualTo(SurvivalRules.ToolDurability - 1));
            Assert.That(npc.Needs.Energy, Is.EqualTo(energy - 100)); Assert.That(world.Combat.Count(ItemKind.Arrow), Is.EqualTo(1));
            Assert.That(npc.RngState, Is.EqualTo(npcRng)); Assert.That(prey.RngState, Is.EqualTo(animalRng)); Conserved(world);
        }

        [Test]
        public void BowWithoutAmmo_UsesSpearOrRejects_AndCannotRequestDistantHunt()
        {
            WorldState world = Empty(); NpcState npc = world.Entities.Npcs[0]; AnimalState prey = Prey(world, 9, 5);
            npc.Inventory.TryRemove(ItemKind.Arrow, 6);
            Assert.That(HuntingSystem.Weapon(npc), Is.EqualTo(ItemKind.Spear));
            Assert.That(HuntingSystem.CanStrike(world, npc, prey), Is.False);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.False);
            prey.Position = new GridPos(6, 5);
            Assert.That(HuntingSystem.CanStrike(world, npc, prey), Is.True);
            npc.Survival.SetTool(ItemKind.Spear, 0);
            Assert.That(HuntingSystem.CanStrike(world, npc, prey), Is.False);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.False);
        }

        [TestCase(1799, 10000)]
        [TestCase(10000, 2999)]
        public void Hunt_ExhaustedOrInjured_RejectsBeforeCreatingAttack(int energy, int health)
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 6, 5); NpcState npc = world.Entities.Npcs[0];
            npc.Needs.Fatigue = energy; npc.Needs.Health = health;
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.False);
            Assert.That(world.Combat.Attacks, Is.Empty);
        }

        [Test]
        public void CancelBeforeRelease_LeavesAmmoAndRngIntact()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); NpcState npc = world.Entities.Npcs[0];
            ulong rng = world.Combat.RngState;
            CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>());
            Minute(world); CombatSystem.CancelUnreleased(world, npc.Id);
            for (int i = 0; i < 5; i++) { Minute(world); }
            Assert.That(world.Combat.Attacks, Is.Empty); Assert.That(world.Combat.Projectiles, Is.Empty);
            Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(6)); Assert.That(world.Combat.RngState, Is.EqualTo(rng)); Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReleasedArrow_CancelOrShooterDeath_DoesNotDeleteFlight(bool dead)
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); NpcState npc = world.Entities.Npcs[0];
            ProjectileState arrow = Release(world, prey); int x = arrow.X;
            if (dead) { npc.Needs.Health = 0; npc.Survival.Life = LifeState.Dead; }
            CombatSystem.CancelUnreleased(world, npc.Id);
            Minute(world);
            Assert.That(arrow.X, Is.Not.EqualTo(x)); Assert.That(world.Combat.TraceSegments.Any(t => t.ProjectileId == arrow.Id), Is.True);
            for (int i = 0; i < 5; i++) { Minute(world); }
            Assert.That(world.Combat.Projectiles.All(a => a.Phase != ProjectilePhase.Flying), Is.True); Conserved(world);
        }

        [Test]
        public void ReleasedArrow_TargetMovesOrDies_DoesNotHomeOrDisappear()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); ProjectileState arrow = Release(world, prey);
            int vx = arrow.VelocityX, vy = arrow.VelocityY, x = arrow.X, y = arrow.Y;
            Minute(world, () => prey.Position = new GridPos(9, 12));
            Assert.That(arrow.VelocityX, Is.EqualTo(vx)); Assert.That(arrow.VelocityY, Is.EqualTo(vy));
            Assert.That(arrow.X, Is.EqualTo(x + vx)); Assert.That(arrow.Y, Is.EqualTo(y + vy));
            prey.Health = 0; Minute(world);
            Assert.That(arrow.LastAdvancedTick, Is.EqualTo(world.Tick - 1));
            Assert.That(world.Combat.Hits, Is.Zero); Conserved(world);
        }

        [Test]
        public void MigrationDepartureMinute_DoesNotSweepExitToInvalidPointOrDeleteReleasedArrow()
        {
            WorldState world = Empty(); MigrationSystem.Initialize(world);
            GridPos exit = world.Ecology.Regions.Regions[1].Exit;
            AnimalState prey = Prey(world, exit.X, exit.Y); int health = prey.Health;
            // This intersects the old fictitious exit -> (-1,-1) body trajectory.
            ProjectileState arrow = Arrow(world, new GridPos(exit.X / 2 - 1, exit.Y / 2 - 1), 1024, 1024);
            int x = arrow.X, y = arrow.Y; ulong rng = world.Combat.RngState;
            Minute(world, () => Depart(world, prey));
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(world.Combat.Hits, Is.Zero);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Flying)); Assert.That(arrow.X, Is.EqualTo(x + 1024)); Assert.That(arrow.Y, Is.EqualTo(y + 1024));
            Assert.That(world.Combat.TraceSegments.Single().HitId.IsNone, Is.True);
            Assert.That(world.Combat.RngState, Is.EqualTo(rng)); CombatSnapshot.Validate(world); Conserved(world);
        }

        [TestCase(0, 8, 5)]
        [TestCase(-1, -1, -1)]
        [TestCase(-1, 32, 5)]
        public void NonlocalOrInvalidAnimalPosition_IsNotALocalCombatBody(int region, int x, int y)
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 8, 5); int health = prey.Health;
            prey.Region = region; prey.Position = new GridPos(x, y);
            Assert.That(CombatSystem.TryPosition(world, prey.Id, true, out GridPos _), Is.False);
            Assert.That(CombatSystem.TryStartHunt(world, world.Entities.Npcs[0], prey, new List<SimEvent>()), Is.False);
            Arrow(world, new GridPos(5, 5), 6 * 1024); Minute(world);
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(world.Combat.Hits, Is.Zero); Conserved(world);
        }

        [Test]
        public void MigrationReturnMinute_UsesOnlyCurrentLocalBody_NotInvalidStartSweep()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 31, 16); int health = prey.Health;
            prey.Region = 1; prey.Position = GridPos.Invalid;
            ProjectileState arrow = Arrow(world, new GridPos(14, 7), 1024, 1024);
            Minute(world, () => { prey.Region = -1; prey.Position = new GridPos(31, 16); });
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(world.Combat.Hits, Is.Zero);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Flying)); Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MigrationDuringPreparation_CancelsHunterOrAnimalAttack_AndSaveRejectsExecutableNonlocalActor(bool animalAttacker)
        {
            WorldState world = Empty(); MigrationSystem.Initialize(world);
            GridPos exit = world.Ecology.Regions.Regions[1].Exit;
            AnimalState animal = Prey(world, exit.X, exit.Y, animalAttacker ? AnimalSpecies.Wolf : AnimalSpecies.Rabbit);
            NpcState npc = world.Entities.Npcs[0]; npc.Position = exit.Offset(-1, 0);
            Assert.That(animalAttacker ? CombatSystem.RequestNpcAttack(world, animal, npc, new List<SimEvent>()) :
                CombatSystem.TryStartHunt(world, npc, animal, new List<SimEvent>()), Is.True);
            EntityId actor = animalAttacker ? animal.Id : npc.Id;
            ulong rng = world.Combat.RngState; int energy = npc.Needs.Energy;
            Minute(world);
            Minute(world, () =>
            {
                Depart(world, animal);
                byte[] pending = CombatSnapshot.Capture(world);
                Assert.Throws<InvalidDataException>(() => CombatSnapshot.Validate(world));
                Assert.Throws<InvalidDataException>(() => CombatSnapshot.Restore(pending, world));
                CollectionAssert.AreEqual(pending, CombatSnapshot.Capture(world));
            });
            Assert.That(world.Combat.GetAttack(actor), Is.Null); Assert.That(world.Combat.Projectiles, Is.Empty);
            Assert.That(world.Combat.CombatEvents.Count(e => e.State == CombatEventKind.Cancelled && e.Shooter == actor), Is.EqualTo(1));
            Assert.That(world.Combat.ShotsReleased, Is.Zero); Assert.That(npc.Needs.Health, Is.EqualTo(10000));
            Assert.That(npc.Needs.Energy, Is.EqualTo(energy)); Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(6));
            Assert.That(npc.Survival.Durability(ItemKind.Bow), Is.EqualTo(SurvivalRules.ToolDurability));
            Assert.That(world.Combat.RngState, Is.EqualTo(rng)); CombatSnapshot.Validate(world); Conserved(world);
            Minute(world); Assert.That(world.Combat.CombatEvents.Any(e => e.State == CombatEventKind.Cancelled), Is.False);
        }

        [Test]
        public void NonlocalAnimalAttacker_WithRetainedValidCell_CannotStartLocalAttack()
        {
            WorldState world = Empty(); AnimalState wolf = Prey(world, 6, 5, AnimalSpecies.Wolf);
            wolf.Region = 0;
            Assert.That(CombatSystem.RequestNpcAttack(world, wolf, world.Entities.Npcs[0], new List<SimEvent>()), Is.False);
            Assert.That(world.Combat.Attacks, Is.Empty);
        }

        [Test]
        public void Spear_TargetLeavesDuringPreparation_MissesAtActualContactRange()
        {
            WorldState world = Empty(); NpcState npc = world.Entities.Npcs[0]; AnimalState prey = Prey(world, 6, 5);
            npc.Inventory.TryRemove(ItemKind.Arrow, 6); world.Materials.Initialize(world); int health = prey.Health;
            CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()); Minute(world);
            Minute(world, () => prey.Position = new GridPos(8, 5));
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(world.Combat.Misses, Is.EqualTo(1));
            Assert.That(world.Combat.CombatEvents.Any(e => e.State == CombatEventKind.Missed), Is.True);
            Assert.That(npc.Survival.Durability(ItemKind.Spear), Is.EqualTo(SurvivalRules.ToolDurability - 1));
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.False); Conserved(world);
        }

        [Test]
        public void WolfBite_RequiresPreparationAndRealRange_NotProximityRandomDamage()
        {
            WorldState world = Empty(); AnimalState wolf = Prey(world, 6, 5, AnimalSpecies.Wolf); NpcState npc = world.Entities.Npcs[0];
            Assert.That(CombatSystem.RequestNpcAttack(world, wolf, npc, new List<SimEvent>()), Is.True);
            Assert.That(npc.Needs.Health, Is.EqualTo(10000)); Minute(world);
            Minute(world, () => npc.Position = new GridPos(3, 5));
            Assert.That(npc.Needs.Health, Is.EqualTo(10000)); Assert.That(world.Combat.Misses, Is.EqualTo(1));
        }

        [Test]
        public void BoarContact_QueuesReadableCounterattack_ThenDamagesHunterOnce()
        {
            WorldState world = Empty(); NpcState npc = world.Entities.Npcs[0]; AnimalState boar = Prey(world, 6, 5, AnimalSpecies.Boar);
            npc.Inventory.TryRemove(ItemKind.Arrow, 6); world.Materials.Initialize(world);
            CombatSystem.TryStartHunt(world, npc, boar, new List<SimEvent>()); Minute(world); Minute(world);
            CombatAttackState reaction = world.Combat.GetAttack(boar.Id);
            Assert.That(reaction, Is.Not.Null); Assert.That(reaction.IsCounterattack, Is.True);
            Assert.That(world.Combat.Counterattacks, Is.EqualTo(1)); Assert.That(npc.Needs.Health, Is.EqualTo(10000));
            Minute(world);
            Assert.That(npc.Needs.Health, Is.EqualTo(10000 - boar.Definition.InjuryDamage));
            Assert.That(world.Combat.GetLastDamage(npc.Id).Value.Source, Is.EqualTo(boar.Id));
            CombatSystem.Update(world, new List<SimEvent>());
            Assert.That(world.Combat.NpcHits, Is.EqualTo(1));
        }

        [Test]
        public void HighSpeedSweep_HitsBetweenEndpoints_AndDoesNotRollRng()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 8, 5); int health = prey.Health;
            Arrow(world, new GridPos(5, 5), 12 * 1024); ulong rng = world.Combat.RngState;
            Minute(world);
            Assert.That(prey.Health, Is.EqualTo(health - 11)); Assert.That(world.Combat.RngState, Is.EqualTo(rng));
            CombatTraceSegment trace = world.Combat.TraceSegments.Single();
            Assert.That(trace.HitId, Is.EqualTo(prey.Id)); Assert.That(trace.ToTime, Is.LessThan(65536));
            Assert.That(trace.ToX, Is.LessThan(9 * 1024)); Conserved(world);
        }

        [Test]
        public void MovingBodySweep_CrossesArrowBetweenStartAndEndCells()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 8, 3); int health = prey.Health;
            Arrow(world, new GridPos(5, 5), 6 * 1024);
            Minute(world, () => prey.Position = new GridPos(8, 7));
            Assert.That(prey.Health, Is.EqualTo(health - 11));
            Assert.That(world.Combat.TraceSegments.Single().HitId, Is.EqualTo(prey.Id)); Conserved(world);
        }

        [Test]
        public void DiagonalSweep_DetectsBodyAndTerrainWithoutEndpointOverlap()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 8, 8); int health = prey.Health;
            Arrow(world, new GridPos(5, 5), 6 * 1024, 6 * 1024); Minute(world);
            Assert.That(prey.Health, Is.EqualTo(health - 11)); Conserved(world);
            world = Empty(); prey = Prey(world, 8, 8); health = prey.Health;
            world.Map.SetTile(new GridPos(7, 7), TileType.Forest);
            Arrow(world, new GridPos(5, 5), 6 * 1024, 6 * 1024); Minute(world);
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(world.Combat.TraceSegments.Single().State, Is.EqualTo(CombatEventKind.Grounded)); Conserved(world);
        }

        [TestCase(1448, 1448, 23173, 6144)]
        [TestCase(16383, 16382, 2049, 6143)]
        public void FractionalTerrainContact_CornerOrSubTimeUnitOverlap_IsNotRoundedAway(int vx, int vy, int time, int y)
        {
            WorldState world = Empty(); world.Map.SetTile(new GridPos(6, 5), TileType.Rock);
            Arrow(world, new GridPos(5, 5), vx, vy); ulong rng = world.Combat.RngState;
            // Corner: enter == exit == 4194304/181 time units. The second sweep
            // overlaps for about 0.125 time units, containing no integral time.
            Minute(world);
            CombatTraceSegment trace = world.Combat.TraceSegments.Single();
            Assert.That(trace.State, Is.EqualTo(CombatEventKind.Broken));
            Assert.That(trace.ToTime, Is.EqualTo(time)); Assert.That(trace.ToX, Is.EqualTo(6144)); Assert.That(trace.ToY, Is.EqualTo(y));
            Assert.That(world.Combat.Projectiles, Is.Empty); Assert.That(world.Combat.RngState, Is.EqualTo(rng));
            Assert.That(world.Materials.Consumed(ItemKind.Arrow), Is.EqualTo(1)); Conserved(world);
        }

        [Test]
        public void FractionalCorner_ReverseDirectionHits_ButExactNearMissDoesNot()
        {
            WorldState world = Empty(); world.Map.SetTile(new GridPos(6, 5), TileType.Rock);
            Arrow(world, new GridPos(6, 6), -1448, -1448); Minute(world);
            Assert.That(world.Combat.TraceSegments.Single().State, Is.EqualTo(CombatEventKind.Broken));
            Assert.That(world.Combat.TraceSegments.Single().ToX, Is.EqualTo(6144)); Conserved(world);
            world = Empty(); world.Map.SetTile(new GridPos(6, 5), TileType.Rock);
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 16382, 16383); Minute(world);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Flying));
            Assert.That(world.Combat.TraceSegments.Single().State, Is.EqualTo(CombatEventKind.Flying)); Conserved(world);
        }

        [Test]
        public void SubTimeUnitWalkableContact_IsRetainedForGroundTransferWithoutSampling()
        {
            WorldState world = Empty();
            for (int y = 0; y < world.Map.Height; y++)
            {
                for (int x = 0; x < world.Map.Width; x++) { world.Map.SetTile(new GridPos(x, y), TileType.Water); }
            }
            world.Map.SetTile(new GridPos(6, 5), TileType.Grass);
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 16383, 16382);
            arrow.RemainingTicks = 1; arrow.ReleasedTick = world.Tick - CombatState.ArrowLifetime;
            Minute(world);
            Assert.That(world.Combat.Projectiles, Is.Empty); Assert.That(world.Combat.ArrowsLost, Is.Zero);
            Assert.That(world.Survival.Ground.Single().Position, Is.EqualTo(new GridPos(6, 5))); Conserved(world);
        }

        [Test]
        public void ExactBodyTimes_SharingQuantizedTime_ChooseEarlierBodyBeforeIdTieBreak()
        {
            WorldState world = Empty(); AnimalState later = Prey(world, 7, 5, AnimalSpecies.Vole);
            AnimalState earlier = Prey(world, 8, 5, AnimalSpecies.Vole);
            int health = later.Health, earlierHealth = earlier.Health;
            Arrow(world, new GridPos(5, 5), 16383);
            // 2944/23551 < 1920/15359 ticks; both ceil to time unit 8193.
            Minute(world, () => { later.Position = new GridPos(8, 5); earlier.Position = new GridPos(1, 5); });
            Assert.That(later.Health, Is.EqualTo(health)); Assert.That(earlier.Health, Is.EqualTo(earlierHealth - 11));
            Assert.That(world.Combat.TraceSegments.Single().HitId, Is.EqualTo(earlier.Id));
            Assert.That(world.Combat.TraceSegments.Single().ToTime, Is.EqualTo(8193)); Conserved(world);
        }

        [Test]
        public void ExactArrowTimes_SharingQuantizedTime_AttributeLethalHitToEarlierArrow()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 6, 5); prey.Health = 1;
            ProjectileState slower = Arrow(world, new GridPos(5, 5), 16382);
            ProjectileState faster = Arrow(world, new GridPos(5, 5), 16383); Minute(world);
            Assert.That(world.Combat.GetLastDamage(prey.Id).Value.ProjectileId, Is.EqualTo(faster.Id));
            Assert.That(world.Combat.TraceSegments[0].ProjectileId, Is.EqualTo(faster.Id));
            Assert.That(world.Combat.TraceSegments[1].ProjectileId, Is.EqualTo(slower.Id));
            Assert.That(world.Combat.TraceSegments.All(t => t.ToTime == 3277), Is.True);
            Assert.That(world.Combat.AnimalKills, Is.EqualTo(1)); Assert.That(world.Survival.Count(ItemKind.Arrow), Is.EqualTo(2)); Conserved(world);
        }

        [TestCase(TileType.Forest)]
        [TestCase(TileType.Rock)]
        [TestCase(TileType.House)]
        public void TerrainStopsArrowBeforeAnimal_AndAccountsForGroundOrBreak(TileType terrain)
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); int health = prey.Health;
            world.Map.SetTile(new GridPos(7, 5), terrain); Arrow(world, new GridPos(5, 5), 8 * 1024); Minute(world);
            Assert.That(prey.Health, Is.EqualTo(health)); Assert.That(world.Combat.Projectiles, Is.Empty);
            if (terrain == TileType.Forest)
            {
                Assert.That(world.Survival.Count(ItemKind.Arrow), Is.EqualTo(1));
                Assert.That(world.Survival.Ground.Single().Position, Is.EqualTo(new GridPos(7, 5)));
            }
            else { Assert.That(world.Materials.Consumed(ItemKind.Arrow), Is.EqualTo(1)); }
            for (int i = 0; i < 3; i++) { Minute(world); }
            Conserved(world);
        }

        [Test]
        public void FlightExpiresOverWater_DropsAtLastWalkableTrajectoryCell()
        {
            WorldState world = Empty();
            for (int x = 7; x <= 20; x++) { world.Map.SetTile(new GridPos(x, 5), TileType.Water); }
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 1024);
            for (int i = 0; i < 4; i++) { Minute(world); }
            Assert.That(world.Combat.Projectiles, Is.Empty); Assert.That(world.Survival.Count(ItemKind.Arrow), Is.EqualTo(1));
            Assert.That(world.Survival.Ground.Single().Position, Is.EqualTo(new GridPos(6, 5))); Conserved(world);
        }

        [Test]
        public void EarliestNonTargetAndStableIdTie_StopArrowWithoutPenetration()
        {
            WorldState world = Empty(); AnimalState near = Prey(world, 7, 5); AnimalState far = Prey(world, 9, 5);
            Arrow(world, new GridPos(5, 5), 8 * 1024); Minute(world);
            Assert.That(world.Combat.GetLastDamage(near.Id), Is.Not.Null); Assert.That(world.Combat.GetLastDamage(far.Id), Is.Null);
            world = Empty(); AnimalState low = Prey(world, 8, 5); AnimalState high = Prey(world, 8, 5);
            Arrow(world, new GridPos(5, 5), 8 * 1024); Minute(world);
            Assert.That(world.Combat.GetLastDamage(low.Id), Is.Not.Null); Assert.That(world.Combat.GetLastDamage(high.Id), Is.Null); Conserved(world);
        }

        [Test]
        public void FriendlyNpcBlocksArrow_AndSeparatedShooterCanBeHit()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5);
            var friend = new NpcState(world.AllocateEntityId(), new NpcIdentity("Friend", "Farmer", 30), new Personality(), new GridPos(7, 5), 1);
            world.Entities.AddNpc(friend); world.Materials.Initialize(world);
            Arrow(world, new GridPos(5, 5), 8 * 1024); Minute(world);
            Assert.That(friend.Needs.Health, Is.EqualTo(9989)); Assert.That(world.Combat.GetLastDamage(prey.Id), Is.Null); Conserved(world);
            world = Empty(); NpcState shooter = world.Entities.Npcs[0];
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 1024); Minute(world);
            Assert.That(shooter.Needs.Health, Is.EqualTo(10000)); Assert.That(arrow.IgnoreInitialShooterOverlap, Is.False);
            Minute(world, () => shooter.Position = new GridPos(7, 5));
            Assert.That(shooter.Needs.Health, Is.EqualTo(9989)); Conserved(world);
        }

        [Test]
        public void EmbeddedArrow_FollowsVictim_ThenBecomesSingleGroundArrowOnDeath()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 7, 5);
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 4 * 1024); Minute(world);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Embedded)); Assert.That(arrow.EmbeddedTarget, Is.EqualTo(prey.Id));
            Minute(world, () => prey.Position = new GridPos(8, 6));
            Assert.That(ProjectileSystem.Cell(arrow.X, arrow.Y), Is.EqualTo(prey.Position));
            prey.Health = 0; Minute(world); Minute(world);
            Assert.That(world.Combat.Projectiles, Is.Empty); Assert.That(world.Survival.Count(ItemKind.Arrow), Is.EqualTo(1));
            Assert.That(world.Survival.Ground.Single().Position, Is.EqualTo(prey.Position));
            NpcState npc = world.Entities.Npcs[0]; npc.Position = prey.Position;
            Assert.That(CombatSystem.RecoverArrows(world, npc, prey.Position), Is.EqualTo(1));
            Assert.That(CombatSystem.RecoverArrows(world, npc, prey.Position), Is.Zero); Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(6)); Conserved(world);
        }

        [Test]
        public void EmbeddedMigration_PendingSaveRetainsLocalPoint_ThenTransfersOneConservedGroundArrow()
        {
            WorldState world = Empty(); MigrationSystem.Initialize(world); AnimalState prey = Prey(world, 7, 5);
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 4 * 1024); Minute(world);
            GridPos exit = world.Ecology.Regions.Regions[1].Exit;
            Minute(world, () => prey.Position = exit);
            Assert.That(arrow.LastWalkablePosition, Is.EqualTo(exit));
            CombatSystem.RecordPositions(world); Depart(world, prey);
            Assert.That(ProjectileSystem.Cell(arrow.X, arrow.Y), Is.EqualTo(exit));
            CombatSnapshot.Validate(world); byte[] pending = CombatSnapshot.Capture(world);
            // Invalid embedded coordinates still reject atomically, even for an external target.
            int x = arrow.X; arrow.X = -512; byte[] invalid = CombatSnapshot.Capture(world); arrow.X = x;
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Restore(invalid, world));
            CollectionAssert.AreEqual(pending, CombatSnapshot.Capture(world));
            WorldState copy = Empty(); MigrationSystem.Initialize(copy); AnimalState other = Prey(copy, 7, 5);
            other.Region = prey.Region; other.Position = GridPos.Invalid; other.Health = prey.Health; copy.Tick = world.Tick;
            copy.Entities.Npcs[0].Inventory.TryRemove(ItemKind.Arrow, 1);
            CombatSnapshot.Restore(pending, copy); CollectionAssert.AreEqual(pending, CombatSnapshot.Capture(copy));
            foreach (WorldState branch in new[] { world, copy })
            {
                ProjectileSystem.UpdateEmbedded(branch); ProjectileSystem.UpdateEmbedded(branch); Minute(branch);
                Assert.That(branch.Combat.Projectiles, Is.Empty); Assert.That(branch.Survival.Count(ItemKind.Arrow), Is.EqualTo(1));
                Assert.That(branch.Survival.Ground.Single().Position, Is.EqualTo(exit));
                Assert.That(branch.Combat.ArrowsLost, Is.Zero); CombatSnapshot.Validate(branch); Conserved(branch);
                NpcState npc = branch.Entities.Npcs[0]; npc.Position = exit;
                Assert.That(CombatSystem.RecoverArrows(branch, npc, exit), Is.EqualTo(1));
                Assert.That(CombatSystem.RecoverArrows(branch, npc, exit), Is.Zero); Conserved(branch);
            }
            CollectionAssert.AreEqual(CombatSnapshot.Capture(world), CombatSnapshot.Capture(copy));
        }

        [Test]
        public void EmbeddedDeparture_DoesNotFollowForeignValidCellOrUnsavedMovementRecord()
        {
            WorldState world = Empty(); MigrationSystem.Initialize(world); AnimalState prey = Prey(world, 7, 5);
            ProjectileState arrow = Arrow(world, new GridPos(5, 5), 4 * 1024); Minute(world);
            GridPos lastLocal = arrow.LastWalkablePosition;
            prey.Position = world.Ecology.Regions.Regions[1].Exit; CombatSystem.RecordPositions(world); Depart(world, prey);
            // A foreign resident with a stale but valid map position is still nonlocal.
            prey.Position = new GridPos(20, 20);
            ProjectileSystem.UpdateEmbedded(world); ProjectileSystem.UpdateEmbedded(world);
            Assert.That(world.Survival.Ground.Single().Position, Is.EqualTo(lastLocal));
            Assert.That(ProjectileSystem.Cell(arrow.X, arrow.Y), Is.EqualTo(lastLocal));
            Assert.That(world.Combat.CombatEvents.Count(e => e.State == CombatEventKind.Grounded), Is.EqualTo(1));
            Assert.That(world.Survival.Count(ItemKind.Arrow), Is.EqualTo(1)); Conserved(world);
        }

        [Test]
        public void RecoverEmbeddedArrow_FullBagLeavesItAuthoritativeUntilSpaceAvailable()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 7, 5); NpcState npc = world.Entities.Npcs[0];
            Arrow(world, new GridPos(5, 5), 4 * 1024); Minute(world); npc.Position = prey.Position;
            npc.Inventory.TryAdd(ItemKind.Seed, 7); world.Materials.Initialize(world);
            Assert.That(CombatSystem.RecoverArrows(world, npc, prey.Position), Is.Zero); Assert.That(world.Combat.Count(ItemKind.Arrow), Is.EqualTo(1));
            npc.Inventory.TryRemove(ItemKind.Seed, 1); world.Materials.Consume(ItemKind.Seed, 1);
            Assert.That(CombatSystem.RecoverArrows(world, npc, prey.Position), Is.EqualTo(1)); Assert.That(world.Combat.Projectiles, Is.Empty); Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BrokenOrOutOfBoundsArrow_IsConsumedOnlyOnce(bool boundary)
        {
            WorldState world = Empty();
            if (boundary) { Arrow(world, new GridPos(30, 5), 4 * 1024); }
            else { Arrow(world, new GridPos(5, 5), 1024, integrity: 0); }
            for (int i = 0; i < 8; i++) { Minute(world); Conserved(world); }
            Assert.That(world.Materials.Consumed(ItemKind.Arrow), Is.EqualTo(1)); Assert.That(world.Combat.ArrowsLost, Is.EqualTo(1));
            Assert.That(world.Combat.Projectiles, Is.Empty);
        }

        [Test]
        public void TwoSweptHits_SameTick_ApplyOneLethalTransitionAndNoFoodProduction()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 7, 5); prey.Health = 1;
            Arrow(world, new GridPos(5, 5), 4 * 1024); Arrow(world, new GridPos(5, 5), 4 * 1024); Minute(world);
            Assert.That(prey.Health, Is.Zero); Assert.That(world.Combat.AnimalKills, Is.EqualTo(1));
            Assert.That(world.Combat.Hits, Is.EqualTo(1)); Assert.That(world.Materials.Produced(ItemKind.Food), Is.Zero);
            Assert.That(world.Survival.Count(ItemKind.Arrow), Is.EqualTo(2)); Conserved(world);
        }

        [Test]
        public void LethalArrowWithinMinute_CancelsVictimUnreleasedAttackAtMinuteEnd()
        {
            WorldState world = Empty(); AnimalState wolf = Prey(world, 6, 5, AnimalSpecies.Wolf); wolf.Health = 1;
            NpcState npc = world.Entities.Npcs[0]; CombatSystem.RequestNpcAttack(world, wolf, npc, new List<SimEvent>());
            Minute(world); Arrow(world, npc.Position, 4 * 1024); Minute(world);
            Assert.That(wolf.Health, Is.Zero); Assert.That(npc.Needs.Health, Is.EqualTo(10000));
            Assert.That(world.Combat.GetAttack(wolf.Id), Is.Null); Conserved(world);
        }

        [Test]
        public void TracePacket_SameTickCompletedArrowAndRetainedPacketsSurviveNextTick()
        {
            WorldState world = Empty(); world.Map.SetTile(new GridPos(6, 5), TileType.Forest);
            Arrow(world, new GridPos(5, 5), 8 * 1024); Minute(world);
            Assert.That(world.Combat.Projectiles, Is.Empty);
            IReadOnlyList<CombatTraceSegment> retained = world.Combat.TraceSegments;
            IReadOnlyList<CombatTraceSegment> packet = world.Combat.CaptureTraceSegments();
            IReadOnlyList<CombatVisualEvent> events = world.Combat.CaptureCombatEvents();
            CombatTraceSegment trace = retained.Single(); byte[] authority = CombatSnapshot.Capture(world);
            CombatSystem.RecordPositions(world);
            CollectionAssert.AreEqual(authority, CombatSnapshot.Capture(world));
            for (int i = 0; i < 8; i++) { Minute(world); }
            Assert.That(retained.Count, Is.EqualTo(1)); Assert.That(packet.Count, Is.EqualTo(1));
            Assert.That(packet[0].ToX, Is.EqualTo(trace.ToX)); Assert.That(events.Any(e => e.State == CombatEventKind.Grounded), Is.True);
            Assert.That(world.Combat.TraceSegments, Is.Empty);
        }

        [Test]
        public void Aim_AdverseConditionsIncreaseFixedSeedAngularError_NoPresetHitFlag()
        {
            long good = 0, bad = 0;
            int clearHits = 0, adverseHits = 0;
            for (int seed = 1; seed <= 96; seed++)
            {
                WorldState clear = Empty(seed), adverse = Empty(seed);
                AnimalState a = Prey(clear, 9, 5), b = Prey(adverse, 9, 5);
                NpcState npc = adverse.Entities.Npcs[0]; npc.HuntingSkill = 5; npc.Needs.Fatigue = 1900; npc.Needs.Health = 3100;
                npc.Survival.BodyTemperature = 3300; adverse.Environment.Temperature = -2500;
                adverse.Environment.Daylight = 0; adverse.Environment.Wind = 100; adverse.Environment.Weather = WeatherKind.Blizzard;
                ProjectileState accurate = Release(clear, a), inaccurate = Release(adverse, b);
                good += Math.Abs(accurate.VelocityY); bad += Math.Abs(inaccurate.VelocityY);
                for (int i = 0; i < CombatState.ArrowLifetime; i++) { Minute(clear); Minute(adverse); }
                if (clear.Combat.Hits > 0) { clearHits++; }
                if (adverse.Combat.Hits > 0) { adverseHits++; }
            }
            Assert.That(bad, Is.GreaterThan(good * 2));
            Assert.That(clearHits, Is.GreaterThan(adverseHits));
            Assert.That(clearHits, Is.LessThan(96), "Even high skill is not a preselected guaranteed hit.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CombatSaveBranch_RestoresPreparingOrFlyingAuthority_ContinuesIdentically(bool flying)
        {
            WorldState original = Empty(); AnimalState prey = Prey(original, 9, 5); NpcState npc = original.Entities.Npcs[0];
            CombatSystem.TryStartHunt(original, npc, prey, new List<SimEvent>());
            Minute(original);
            if (flying) { Minute(original); Minute(original); Minute(original); }
            byte[] save = CombatSnapshot.Capture(original);
            WorldState restored = Empty(); AnimalState restoredPrey = Prey(restored, prey.Position.X, prey.Position.Y);
            restored.Tick = original.Tick; restoredPrey.Health = prey.Health;
            NpcState copy = restored.Entities.Npcs[0]; copy.Needs.Fatigue = npc.Needs.Fatigue; copy.HuntingSkill = npc.HuntingSkill;
            copy.Inventory.TryRemove(ItemKind.Arrow, 6 - npc.Inventory.Count(ItemKind.Arrow));
            copy.Survival.SetTool(ItemKind.Bow, npc.Survival.Durability(ItemKind.Bow));
            CombatSnapshot.Restore(save, restored); CollectionAssert.AreEqual(save, CombatSnapshot.Capture(restored));
            Assert.That(restored.Combat.TraceSegments, Is.Empty);
            for (int i = 0; i < 9; i++)
            {
                Minute(original); Minute(restored);
                CollectionAssert.AreEqual(CombatSnapshot.Capture(original), CombatSnapshot.Capture(restored), "Minute " + i);
                Assert.That(restoredPrey.Health, Is.EqualTo(prey.Health));
                Assert.That(restored.Survival.Count(ItemKind.Arrow), Is.EqualTo(original.Survival.Count(ItemKind.Arrow)));
            }
        }

        [Test]
        public void CombatSave_EmbeddedAndHistoricalDeadShooter_RetainArrowsAndAttribution()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 7, 5); NpcState npc = world.Entities.Npcs[0];
            Arrow(world, new GridPos(5, 5), 4 * 1024); Minute(world); npc.Survival.Life = LifeState.Dead; npc.Needs.Health = 0;
            byte[] save = CombatSnapshot.Capture(world);
            WorldState copy = Empty(); AnimalState other = Prey(copy, 7, 5); other.Health = prey.Health; copy.Tick = world.Tick;
            copy.Entities.Npcs[0].Survival.Life = LifeState.Dead; copy.Entities.Npcs[0].Needs.Health = 0;
            CombatSnapshot.Restore(save, copy);
            Assert.That(copy.Combat.Projectiles.Single().EmbeddedTarget, Is.EqualTo(prey.Id));
            Assert.That(copy.Combat.GetLastDamage(prey.Id).Value.Source, Is.EqualTo(npc.Id));
            other.Health = 0; Minute(copy);
            Assert.That(copy.Combat.Count(ItemKind.Arrow), Is.Zero); Assert.That(copy.Survival.Count(ItemKind.Arrow), Is.EqualTo(1));
        }

        [Test]
        public void PredatorAttack_IgnoresHunterReservation_UsesDelayedDeterministicContact()
        {
            WorldState world = Empty(); AnimalState rabbit = Prey(world, 6, 5);
            AnimalState wolf = Prey(world, 7, 5, AnimalSpecies.Wolf); int health = rabbit.Health;
            ulong rng = world.Combat.RngState;
            Assert.That(CombatSystem.RequestAnimalAttack(world, wolf, rabbit, new List<SimEvent>()), Is.True);
            Assert.That(rabbit.ReservedBy, Is.EqualTo(world.Entities.Npcs[0].Id));
            Assert.That(rabbit.Health, Is.EqualTo(health)); Minute(world); Minute(world);
            Assert.That(rabbit.Health, Is.EqualTo(Math.Max(0, health - 24)));
            Assert.That(world.Combat.GetLastDamage(rabbit.Id).Value.SourceIsAnimal, Is.True);
            Assert.That(world.Combat.GetLastDamage(rabbit.Id).Value.Source, Is.EqualTo(wolf.Id));
            Assert.That(world.Combat.RngState, Is.EqualTo(rng));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnreleasedAttack_ShooterDeathOrMissingAmmo_CancelsWithoutRelease(bool missingAmmo)
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); NpcState npc = world.Entities.Npcs[0];
            CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()); Minute(world);
            ulong rng = world.Combat.RngState;
            if (missingAmmo) { npc.Inventory.TryRemove(ItemKind.Arrow, 6); world.Materials.Consume(ItemKind.Arrow, 6); }
            else { npc.Needs.Health = 0; npc.Survival.Life = LifeState.Dead; }
            Minute(world); Minute(world);
            Assert.That(world.Combat.GetAttack(npc.Id), Is.Null); Assert.That(world.Combat.Projectiles, Is.Empty);
            Assert.That(world.Combat.RngState, Is.EqualTo(rng)); Assert.That(world.Combat.ShotsReleased, Is.Zero); Conserved(world);
        }

        [Test]
        public void BowBreakAtRelease_ConsumesOnlyTool_ArrowRemainsIndependentMaterial()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); NpcState npc = world.Entities.Npcs[0];
            npc.Survival.SetTool(ItemKind.Bow, 1); world.Materials.Initialize(world);
            Release(world, prey);
            Assert.That(npc.Survival.Durability(ItemKind.Bow), Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.Bow), Is.EqualTo(1));
            Assert.That(world.Materials.Consumed(ItemKind.Arrow), Is.Zero);
            Assert.That(world.Combat.Count(ItemKind.Arrow), Is.EqualTo(1)); Conserved(world);
        }

        [Test]
        public void CombatSave_MissingHistoricalShooter_IsValidButExecutableMissingActorRejects()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5); Release(world, prey);
            byte[] save = CombatSnapshot.Capture(world);
            var copy = new WorldState(new WorldConfigData(42, 32, 32, 0, 360, 2, new[] { 1 }), new WorldMap(32, 32), new DeterministicRng(42));
            copy.NextEntityId = prey.Id.Value;
            AnimalState other = copy.Ecology.Spawn(copy, prey.Species, prey.Position, prey.AgeTicks); other.Health = prey.Health;
            copy.Tick = world.Tick;
            Assert.DoesNotThrow(() => CombatSnapshot.Restore(save, copy));
            Assert.That(copy.Combat.Projectiles.Single().Shooter.Value, Is.EqualTo(1));
            Minute(copy);
            Assert.That(copy.Combat.TraceSegments.Count, Is.EqualTo(1));
            world = Empty(); prey = Prey(world, 9, 5);
            CombatSystem.TryStartHunt(world, world.Entities.Npcs[0], prey, new List<SimEvent>());
            save = CombatSnapshot.Capture(world);
            copy.Tick = world.Tick;
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Restore(save, copy));
        }

        [Test]
        public void CombatSave_InvalidReferencesRngAndTrailingDataRejectWithoutMutatingWorld()
        {
            WorldState world = Empty(); AnimalState prey = Prey(world, 9, 5);
            CombatSystem.TryStartHunt(world, world.Entities.Npcs[0], prey, new List<SimEvent>());
            byte[] good = CombatSnapshot.Capture(world), bad = (byte[])good.Clone();
            Array.Clear(bad, 4, 8);
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Restore(bad, world));
            CollectionAssert.AreEqual(good, CombatSnapshot.Capture(world));
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Restore(good.Concat(new byte[] { 1 }).ToArray(), world));
            CollectionAssert.AreEqual(good, CombatSnapshot.Capture(world));
            world.Combat.GetAttack(world.Entities.Npcs[0].Id).Target = new EntityId(world.NextEntityId + 100);
            Assert.Throws<InvalidDataException>(() => CombatSnapshot.Validate(world));
        }
    }
}
