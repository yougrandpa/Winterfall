using System;
using System.Collections.Generic;
using System.Linq;
using MyAiWorld.App.Observability;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class HuntingGameplayTests
    {
        internal static WorldState Arena(int count = 3)
        {
            var world = new WorldState(new WorldConfigData(42, 32, 32, count, 720, 2, new[]{1, 4, 8}), new WorldMap(32, 32), new DeterministicRng(42));
            for (int i = 0; i < count; i++)
            {
                var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("猎人" + i, "Hunter", 25), new Personality(), new GridPos(5, 5 + i), (ulong)(42 + i));
                npc.Position = new GridPos(5, 5 + i);
                npc.Needs.Health = npc.Needs.Fatigue = npc.Needs.Hunger = npc.Needs.Thirst = 10000;
                npc.Survival.SetTool(i == 1 ? ItemKind.Spear : ItemKind.Bow, 120);
                npc.Inventory.TryAdd(ItemKind.Arrow, 4);
                npc.Action.SetIdle("Controlled", long.MaxValue);
                world.Entities.AddNpc(npc);
                world.Market.IssuedCurrency += npc.Coins;
            }

            var reachable = new List<GridPos>();
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    reachable.Add(new GridPos(x, y));
                }
            }

            world.Ecology.Initialize(world, reachable);
            foreach (var animal in world.Ecology.Animals)
            {
                if (animal.Region == -1)
                {
                    animal.Position = new GridPos(28, 28);
                    animal.HabitatId = world.Ecology.HabitatAt(animal.Position).Id;
                }
            }

            world.Resources.Stockpile.TryAdd(ItemKind.Food, 100);
            world.Resources.Stockpile.TryAdd(ItemKind.Water, 100);
            world.Resources.Stockpile.TryAdd(ItemKind.WinterCoat, 100);
            world.Campaign.Initialize(world);
            world.Environment.Temperature = 1000;
            world.Environment.Daylight = 10000;
            world.Materials.Initialize(world);
            return world;
        }

        internal static AnimalState Prey(WorldState world, AnimalSpecies species = AnimalSpecies.Deer)
        {
            AnimalState animal = world.Ecology.Spawn(world, species, new GridPos(8, 6), AnimalDefinition.For(species).MaturityTicks + 1);
            world.Materials.Initialize(world);
            return animal;
        }

        internal static HuntTaskState Request(WorldState world, AnimalState prey, params EntityId[] members)
        {
            Assert.That(HuntingCoordinator.TryRequest(world, prey.Id, members, true, new List<SimEvent>(), out string reason), Is.True, reason);
            return world.Hunting.ForTarget(prey.Id);
        }

        internal static void Conserved(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString());
            }

            Assert.That(world.Ecology.Balance.BiomassError(world.Ecology), Is.Zero);
        }

        [Test]
        public void AutomaticTeam_SharesAuthorityAndReservesOneTarget()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            Assert.That(task.Members.Count, Is.EqualTo(2));
            Assert.That(deer.ReservedBy, Is.EqualTo(task.LeaderId));
            task.Stage = HuntStage.Engaging;
            foreach (var member in task.Members)
            {
                Assert.That(HuntingCoordinator.Authorized(world, world.Entities.Npcs.Single(n => n.Id == member.NpcId), deer), Is.True);
            }

            Assert.That(HuntingCoordinator.TryRequest(world, deer.Id, null, true, new List<SimEvent>(), out _), Is.False);
            Conserved(world);
        }

        [Test]
        public void ExplicitTeam_DuplicatesAndUnqualifiedMemberRejectAtomically()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            var first = world.Entities.Npcs[0];
            ulong hash = StateHasher.Compute(world);
            Assert.That(HuntingCoordinator.TryRequest(world, deer.Id, new[]{first.Id, first.Id}, true, new List<SimEvent>(), out _), Is.False);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
            world.Entities.Npcs[1].Plan.Start(NpcGoalType.Rescue, new List<PlanStep>{new PlanStep(PlanStepType.Rescue, first.Position, first.Id.Value)});
            hash = StateHasher.Compute(world);
            Assert.That(HuntingCoordinator.TryRequest(world, deer.Id, new[]{first.Id, world.Entities.Npcs[1].Id}, true, new List<SimEvent>(), out _), Is.False);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }

        [TestCase(AnimalSpecies.Deer, 1)]
        [TestCase(AnimalSpecies.Wolf, 2)]
        [TestCase(AnimalSpecies.Boar, 1)]
        public void DangerousPrey_InsufficientMembersNeverCreatesTask(AnimalSpecies species, int count)
        {
            WorldState world = Arena(count);
            AnimalState prey = Prey(world, species);
            Assert.That(HuntingCoordinator.TryRequest(world, prey.Id, null, true, new List<SimEvent>(), out _), Is.False);
            Assert.That(world.Hunting.Tasks, Is.Empty);
            Assert.That(prey.ReservedBy.IsNone, Is.True);
        }

        [Test]
        public void Boar_AutomaticRecruitmentIncludesSpearAndNeverCreatesEquipment()
        {
            WorldState world = Arena();
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            HuntTaskState task = Request(world, boar);
            Assert.That(task.Members.Any(m => m.Role == HuntRole.Spear), Is.True);
            Conserved(world);
        }

        [Test]
        public void Animal_ReactsBetweenEcologyDecisionsAndEscapesMoreThanOneCell()
        {
            WorldState world = Arena(1);
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            world.Entities.Npcs[0].Position = rabbit.Position.Offset(-2, 0);
            rabbit.NextDecisionTick = world.Tick + 30;
            rabbit.NextMoveTick = world.Tick + 18;
            GridPos start = rabbit.Position;
            for (int i = 0; i < 6; i++)
            {
                AnimalEncounterSystem.Update(world, rabbit, new List<SimEvent>());
                world.Tick++;
            }

            Assert.That(rabbit.Position.ManhattanDistance(start), Is.GreaterThanOrEqualTo(3));
            Assert.That(rabbit.Encounter.Stamina, Is.LessThan(HuntingRules.MaxStamina));
            Conserved(world);
        }

        [Test]
        public void HiddenThreat_UsesLastSeenPositionAndEventuallyRecovers()
        {
            WorldState world = Arena(1);
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            AnimalEncounterSystem.Update(world, rabbit, new List<SimEvent>());
            GridPos seen = rabbit.Encounter.LastThreatPosition;
            world.Entities.Npcs[0].Position = new GridPos(25, 25);
            world.Tick++;
            AnimalEncounterSystem.Update(world, rabbit, new List<SimEvent>());
            Assert.That(rabbit.Encounter.LastThreatPosition, Is.EqualTo(seen));
            world.Tick += HuntingRules.LostTicks;
            AnimalEncounterSystem.Update(world, rabbit, new List<SimEvent>());
            Assert.That(rabbit.Fleeing, Is.False);
            Assert.That(rabbit.Encounter.ThreatId.IsNone, Is.True);
        }

        [Test]
        public void BoarCharge_WindupLocksDirectionThenContactsOnlyOnce()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            boar.Position = new GridPos(8, 5);
            int health = npc.Needs.Health;
            Assert.That(CombatSystem.RequestCharge(world, boar, npc, new List<SimEvent>()), Is.True);
            for (int i = 0; i < 2; i++)
            {
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
                Assert.That(boar.Position, Is.EqualTo(new GridPos(8, 5)));
            }

            for (int i = 0; i < 8; i++)
            {
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
            }

            Assert.That(npc.Needs.Health, Is.EqualTo(health - boar.Definition.InjuryDamage * 3 / 2));
            Assert.That(world.Combat.NpcHits, Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void BoarCharge_SidestepAndWallPreventContact()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            boar.Position = new GridPos(8, 5);
            Assert.That(CombatSystem.RequestCharge(world, boar, npc, new List<SimEvent>()), Is.True);
            npc.Position = new GridPos(5, 6);
            world.Map.SetTile(new GridPos(6, 5), TileType.Rock);
            for (int i = 0; i < 9; i++)
            {
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
            }

            Assert.That(npc.Needs.Health, Is.EqualTo(10000));
            Assert.That(boar.Position, Is.EqualTo(new GridPos(7, 5)));
        }

        [Test]
        public void ArrowKillsChargingBoar_BeforeChargeContactAndUsesItsMovementSweep()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            boar.Position = new GridPos(6, 5);
            boar.HabitatId = world.Ecology.HabitatAt(boar.Position).Id;
            Assert.That(CombatSystem.RequestCharge(world, boar, npc, new List<SimEvent>()), Is.True);
            for (int i = 0; i < 2; i++)
            {
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
            }

            npc.Inventory.TryRemove(ItemKind.Arrow, 1);
            world.Combat.MutableProjectiles.Add(new ProjectileState(world.Combat.NextProjectileId++, npc.Id)
            {AttackId = world.Combat.NextAttackId++, Phase = ProjectilePhase.Flying, X = 7 * 1024 + 512, Y = 5 * 1024 + 512, VelocityX = -2048, ReleasedTick = world.Tick - 1, LastAdvancedTick = world.Tick - 1, RemainingTicks = CombatState.ArrowLifetime, Damage = 1000, Integrity = 1, LastWalkablePosition = new GridPos(7, 5), IgnoreInitialShooterOverlap = true});
            world.Combat.ShotsReleased++;
            CombatSystem.Update(world, new List<SimEvent>());
            world.Tick++;
            Assert.That(boar.Health, Is.Zero);
            Assert.That(npc.Needs.Health, Is.EqualTo(10000));
            Assert.That(world.Combat.NpcHits, Is.Zero);
            Conserved(world);
        }

        [Test]
        public void FriendlyLane_BlocksDrawAndBlocksReleaseWithoutSpendingAmmo()
        {
            WorldState world = Arena();
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            var shooter = world.Entities.Npcs[0];
            rabbit.Position = new GridPos(9, 5);
            rabbit.ReservedBy = shooter.Id;
            world.Entities.Npcs[1].Position = new GridPos(7, 5);
            Assert.That(CombatSystem.TryStartHunt(world, shooter, rabbit, new List<SimEvent>()), Is.False);
            world.Entities.Npcs[1].Position = new GridPos(7, 7);
            Assert.That(CombatSystem.TryStartHunt(world, shooter, rabbit, new List<SimEvent>()), Is.True);
            CombatSystem.Update(world, new List<SimEvent>());
            world.Tick++;
            world.Entities.Npcs[1].Position = new GridPos(7, 5);
            CombatSystem.Update(world, new List<SimEvent>());
            world.Tick++;
            CombatSystem.Update(world, new List<SimEvent>());
            world.Tick++;
            Assert.That(world.Combat.ShotsReleased, Is.Zero);
            Assert.That(shooter.Inventory.Count(ItemKind.Arrow), Is.EqualTo(4));
            Conserved(world);
        }

        [Test]
        public void VisiblePreyLeavingInitiationRange_DoesNotRestartDrawForever()
        {
            WorldState world = Arena(1);
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            var shooter = world.Entities.Npcs[0];
            rabbit.Position = new GridPos(9, 5);
            rabbit.ReservedBy = shooter.Id;
            Assert.That(CombatSystem.TryStartHunt(world, shooter, rabbit, new List<SimEvent>()), Is.True);
            for (int i = 0; i < 3; i++)
            {
                CombatSystem.RecordPositions(world);
                rabbit.Position = rabbit.Position.Offset(1, 0);
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
            }

            Assert.That(world.Combat.ShotsReleased, Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void Team_InjuryRetreatsAndLeaderDetachReassignsWithoutLeakingReservation()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            EntityId oldLeader = task.LeaderId;
            HuntingCoordinator.Detach(world, oldLeader);
            Assert.That(task.LeaderId, Is.Not.EqualTo(oldLeader));
            Assert.That(deer.ReservedBy, Is.EqualTo(task.LeaderId));
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(deer.ReservedBy.IsNone, Is.True);
            Assert.That(world.Hunting.RetryAt(oldLeader), Is.EqualTo(world.Tick + HuntingRules.RetryTicks));
        }

        [Test]
        public void Retrieval_TwoMembersNeverDuplicateMeatAndReturnDepositsActualGoods()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            deer.Health = 0;
            deer.DeathCause = AnimalDeathCause.Hunt;
            deer.LastDamageSourceId = task.LeaderId;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            var body = world.Ecology.FindCarcass(deer.Id.Value);
            int meat = body.MeatRemaining;
            int hide = body.HideRemaining;
            HuntingCoordinator.Update(world, new List<SimEvent>());
            foreach (var member in task.Members)
            {
                world.Entities.Npcs.Single(n => n.Id == member.NpcId).Position = body.Position;
            }

            for (int i = 0; i < 5; i++)
            {
                foreach (var member in task.Members.ToArray())
                {
                    HuntingCoordinator.UpdateMember(world, world.Entities.Npcs.Single(n => n.Id == member.NpcId), new List<SimEvent>());
                }

                HuntingCoordinator.CommitRetrieval(world, new List<SimEvent>());
            }

            foreach (var member in task.Members.ToArray())
            {
                var npc = world.Entities.Npcs.Single(n => n.Id == member.NpcId);
                npc.Position = world.Resources.StockpilePosition;
                HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>());
            }

            Assert.That(world.Resources.Stockpile.Count(ItemKind.RawMeat), Is.EqualTo(meat));
            Assert.That(world.Resources.Stockpile.Count(ItemKind.Hide), Is.EqualTo(hide));
            Assert.That(world.Ecology.HuntKills, Is.EqualTo(1));
            Assert.That(world.Hunting.Tasks, Is.Empty);
            Conserved(world);
        }

        [Test]
        public void Commands_SerializeParticipantsAndCancelMarksAssistedOnlyOnSuccess()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            var ids = new[]{world.Entities.Npcs[0].Id, world.Entities.Npcs[1].Id};
            var command = (RequestHuntCommand)SavedCommand.Capture(new RequestHuntCommand(deer.Id, ids)).Restore();
            CollectionAssert.AreEqual(ids, command.ParticipantIds);
            var events = new List<SimEvent>();
            Assert.That(command.Apply(world, new CommandContext(world.Tick, events)), Is.True);
            Assert.That(world.Campaign.Assisted, Is.True);
            var cancel = (CancelHuntCommand)SavedCommand.Capture(new CancelHuntCommand(world.Hunting.Tasks[0].Id)).Restore();
            Assert.That(cancel.Apply(world, new CommandContext(world.Tick, events)), Is.True);
            Assert.That(world.Hunting.Tasks[0].Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(deer.ReservedBy.IsNone, Is.True);
        }

        [TestCase(HuntStage.Gathering)]
        [TestCase(HuntStage.Approaching)]
        [TestCase(HuntStage.Engaging)]
        [TestCase(HuntStage.Retrieving)]
        [TestCase(HuntStage.Returning)]
        [TestCase(HuntStage.Retreating)]
        public void Save_EveryTaskPhaseRoundTripsAndContinuesIdentically(HuntStage stage)
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            if (stage == HuntStage.Retrieving || stage == HuntStage.Returning)
            {
                deer.Health = 0;
                deer.DeathCause = AnimalDeathCause.Hunt;
                deer.LastDamageSourceId = task.LeaderId;
                world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            }

            task.Stage = stage;
            if (stage == HuntStage.Retreating)
            {
                deer.ReservedBy = EntityId.None;
            }

            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(restored.Hunting.Tasks[0].Stage, Is.EqualTo(stage));
            var first = new SimulationEngine();
            var second = new SimulationEngine();
            for (int i = 0; i < 20; i++)
            {
                Assert.That(first.Tick(world, null).StateHash, Is.EqualTo(second.Tick(restored, null).StateHash), "tick " + i);
                Conserved(world);
                Conserved(restored);
            }
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        public void Save_ChargePreparationMovementAndRecoveryContinueIdentically(int elapsed)
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            boar.Position = new GridPos(8, 5);
            boar.HabitatId = world.Ecology.HabitatAt(boar.Position).Id;
            Assert.That(CombatSystem.RequestCharge(world, boar, npc, new List<SimEvent>()), Is.True);
            for (int i = 0; i < elapsed; i++)
            {
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
            }

            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            for (int i = 0; i < 10; i++)
            {
                CombatSystem.Update(world, new List<SimEvent>());
                world.Tick++;
                CombatSystem.Update(restored, new List<SimEvent>());
                restored.Tick++;
                Assert.That(StateHasher.Compute(world), Is.EqualTo(StateHasher.Compute(restored)));
            }
        }

        [Test]
        public void Save_DuplicateTaskMembershipIsRejected()
        {
            WorldState world = Arena();
            var task = Request(world, Prey(world));
            task.MutableMembers.Add(new HuntMemberState(task.Members[0].NpcId, HuntRole.Archer));
            Assert.Throws<System.IO.InvalidDataException>(() => WorldSnapshot.Restore(WorldSnapshot.Capture(world)));
        }

        [Test]
        public void HuntingReadModel_IsImmutableAndDoesNotAdvanceWorld()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            ulong hash = StateHasher.Compute(world);
            var snapshot = new HuntingReadModel(world);
            task.Stage = HuntStage.Engaging;
            Assert.That(snapshot.Tasks[0].Stage, Is.EqualTo(HuntStage.Gathering));
            Assert.Throws<NotSupportedException>(() => ((IList<HuntTaskSnapshot>)snapshot.Tasks).Clear());
            task.Stage = HuntStage.Gathering;
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }
    }
}
