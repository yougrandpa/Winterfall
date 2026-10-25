using System.Collections.Generic;
using System.Linq;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using static MyAiWorld.Tests.EditMode.HuntingGameplayTests;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class HuntingCompletionTests
    {
        private static void Gather(WorldState world, HuntTaskState task)
        {
            for (int i = 0; i < 8 && task.Stage == HuntStage.Gathering; i++)
            {
                HuntingCoordinator.Update(world, new List<SimEvent>());
                foreach (HuntMemberState member in task.Members.ToArray())
                {
                    HuntingCoordinator.UpdateMember(world, world.Entities.Npcs.Single(n => n.Id == member.NpcId), new List<SimEvent>());
                }
                world.Tick++;
            }
        }

        [Test]
        public void UnarmedTeam_EquipsOnlyRealStockAndPublishesPendingEquipment()
        {
            WorldState world = Arena(2);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Survival.SetTool(ItemKind.Bow, 0);
                npc.Survival.SetTool(ItemKind.Spear, 0);
                npc.Inventory.TryRemove(ItemKind.Arrow, npc.Inventory.Count(ItemKind.Arrow));
                npc.Position = world.Resources.StockpilePosition;
            }
            world.Resources.Stockpile.TryAdd(ItemKind.Bow, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Spear, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 4);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            var before = new HuntingReadModel(world);
            Assert.That(before.Candidates.Count, Is.EqualTo(2));
            Assert.That(before.Candidates.All(c => !c.HasBow && !c.HasSpear && c.CanPrepareBow && c.CanPrepareSpear), Is.True);
            HuntTaskState task = Request(world, boar);
            Gather(world, task);
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Approaching));
            Assert.That(world.Entities.Npcs.Sum(n => n.Survival.EquipmentCount(ItemKind.Bow)), Is.EqualTo(1));
            Assert.That(world.Entities.Npcs.Sum(n => n.Survival.EquipmentCount(ItemKind.Spear)), Is.EqualTo(1));
            Assert.That(world.Entities.Npcs.Sum(n => n.Inventory.Count(ItemKind.Arrow)), Is.EqualTo(4));
            Assert.That(world.Resources.Stockpile.Count(ItemKind.Bow) + world.Resources.Stockpile.Count(ItemKind.Spear), Is.Zero);
            Assert.That(before.Candidates.All(c => !c.HasBow && !c.HasSpear), Is.True);
            Conserved(world);
        }

        [Test]
        public void SharedSupply_CannotPromiseOneBowToTwoExplicitParticipants()
        {
            WorldState world = Arena(2);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Survival.SetTool(ItemKind.Bow, 0);
                npc.Survival.SetTool(ItemKind.Spear, 0);
            }
            world.Resources.Stockpile.TryAdd(ItemKind.Bow, 1);
            AnimalState deer = Prey(world);
            ulong before = StateHasher.Compute(world);
            Assert.That(HuntingCoordinator.TryRequest(world, deer.Id, world.Entities.Npcs.Select(n => n.Id).ToArray(),
                true, new List<SimEvent>(), out string reason), Is.False);
            Assert.That(reason, Does.Contain("武器"));
            Assert.That(StateHasher.Compute(world), Is.EqualTo(before));
        }

        [Test]
        public void LimitedArrows_AreSharedBeforeGatheringCompletes()
        {
            WorldState world = Arena(2);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Survival.SetTool(ItemKind.Spear, 0);
                npc.Survival.SetTool(ItemKind.Bow, 120);
                npc.Inventory.TryRemove(ItemKind.Arrow, 4);
                npc.Position = world.Resources.StockpilePosition;
            }
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 2);
            HuntTaskState task = Request(world, Prey(world));
            Gather(world, task);
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Approaching));
            Assert.That(world.Entities.Npcs.All(n => n.Inventory.Count(ItemKind.Arrow) == 1), Is.True);
            Assert.That(task.Members.Any(m => m.Role == HuntRole.Flanker), Is.True, "A deer team needs an intercepting member.");
            Conserved(world);
        }

        [Test]
        public void OccupiedStockpile_AllowsAdjacentEquipmentPickupWithoutOverlappingTheLeader()
        {
            WorldState world = Arena(2);
            GridPos stock = world.Resources.StockpilePosition;
            for (int i = 0; i < 2; i++)
            {
                NpcState npc = world.Entities.Npcs[i];
                npc.Survival.SetTool(ItemKind.Bow, 0);
                npc.Survival.SetTool(ItemKind.Spear, 0);
                npc.Inventory.TryRemove(ItemKind.Arrow, 4);
                npc.Position = stock.Offset(i * 2, 0);
            }
            world.Resources.Stockpile.TryAdd(ItemKind.Bow, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Spear, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 2);
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Boar));
            Gather(world, task);
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Approaching));
            Assert.That(world.Entities.Npcs[0].Position, Is.Not.EqualTo(world.Entities.Npcs[1].Position));
            Assert.That(world.Entities.Npcs.Sum(n => n.Survival.EquipmentCount(ItemKind.Bow)), Is.EqualTo(1));
            Assert.That(world.Entities.Npcs.Sum(n => n.Survival.EquipmentCount(ItemKind.Spear)), Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void ReturningTeam_DepositsBesideAnOccupiedStockpileAndReleasesTheTask()
        {
            WorldState world = Arena(2);
            HuntTaskState task = Request(world, Prey(world));
            task.Stage = HuntStage.Returning;
            NpcState carrier = world.Entities.Npcs[1];
            world.Entities.Npcs[0].Position = world.Resources.StockpilePosition;
            carrier.Position = world.Resources.StockpilePosition.Offset(2, 0);
            carrier.Inventory.TryAdd(ItemKind.RawMeat, 2);
            world.Materials.Initialize(world);
            for (int i = 0; i < 5 && world.Hunting.Tasks.Count > 0; i++)
            {
                foreach (HuntMemberState member in task.Members.ToArray())
                {
                    HuntingCoordinator.UpdateMember(world, world.Entities.Npcs.Single(n => n.Id == member.NpcId), new List<SimEvent>());
                }
                world.Tick++;
            }
            Assert.That(world.Hunting.Tasks, Is.Empty);
            Assert.That(carrier.Inventory.Count(ItemKind.RawMeat), Is.Zero);
            Assert.That(world.Resources.Stockpile.Count(ItemKind.RawMeat), Is.EqualTo(2));
            Conserved(world);
        }

        [Test]
        public void AutomaticTeam_CanRecruitACarriedSpearWhenTheFirstPairCannotEquipOne()
        {
            WorldState world = Arena();
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Survival.SetTool(ItemKind.Spear, 0);
                npc.Survival.SetTool(ItemKind.Bow, 120);
            }
            NpcState prepared = world.Entities.Npcs[2];
            prepared.Survival.SetTool(ItemKind.Bow, 0);
            prepared.Inventory.TryAdd(ItemKind.Spear, 1);
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Boar));
            Assert.That(task.Member(prepared.Id)?.Role, Is.EqualTo(HuntRole.Spear));
            Assert.That(task.Members.Count, Is.EqualTo(2));
            Assert.That(task.LeaderId, Is.EqualTo(world.Entities.Npcs[1].Id));
            Conserved(world);
        }

        [Test]
        public void DeerDriveAndIntercept_UseDistinctRoutesAndAnAheadWorkCell()
        {
            WorldState world = Arena(2);
            AnimalState deer = Prey(world);
            deer.Position = new GridPos(13, 6);
            HuntTaskState task = Request(world, deer);
            task.Stage = HuntStage.Approaching;
            task.LastSeenPosition = deer.Position;
            task.PreviousSeenPosition = deer.Position.Offset(-1, 0);
            var before = world.Entities.Npcs.Select(n => n.Position).ToArray();
            foreach (NpcState npc in world.Entities.Npcs) { HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>()); }
            HuntMemberState flanker = task.Members.Single(m => m.Role == HuntRole.Flanker);
            HuntMemberState driver = task.Members.Single(m => m.Role == HuntRole.Spear);
            Assert.That(flanker.WorkCell.X, Is.GreaterThan(deer.Position.X));
            Assert.That(driver.WorkCell, Is.Not.EqualTo(flanker.WorkCell));
            Assert.That(world.Entities.Npcs[0].Position, Is.Not.EqualTo(world.Entities.Npcs[1].Position));
            for (int i = 0; i < 2; i++) { Assert.That(before[i].ManhattanDistance(world.Entities.Npcs[i].Position), Is.EqualTo(1)); }
            Assert.That(world.Combat.ShotsReleased, Is.Zero);
        }

        [Test]
        public void TemporarilyOccupiedFiringSlots_WaitInsteadOfAbandoningAReachableHunt()
        {
            WorldState world = Arena(26);
            AnimalState deer = Prey(world);
            deer.Position = new GridPos(10, 5);
            HuntTaskState task = Request(world, deer, world.Entities.Npcs[0].Id, world.Entities.Npcs[1].Id);
            task.Stage = HuntStage.Approaching;
            task.LastSeenPosition = deer.Position;
            task.PreviousSeenPosition = deer.Position.Offset(-1, 0);
            int index = 2;
            for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++)
            {
                int distance = System.Math.Abs(x) + System.Math.Abs(y);
                if (distance > 0 && distance <= 3) { world.Entities.Npcs[index++].Position = new GridPos(12 + x, 5 + y); }
            }
            NpcState archer = world.Entities.Npcs[0];
            GridPos before = archer.Position;
            HuntingCoordinator.UpdateMember(world, archer, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Approaching));
            Assert.That(archer.Position, Is.EqualTo(before));
            Assert.That(archer.Action.Label, Is.EqualTo("Hunt: wait for lane"));
            Assert.That(deer.ReservedBy, Is.EqualTo(task.LeaderId));
            Conserved(world);
        }

        [Test]
        public void Gathering_DeadlineAlsoAppliesWhileWalkingToRally()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            world.Entities.Npcs[0].Position = new GridPos(25, 25);
            world.Tick = task.DeadlineTick;
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(task.Reason, Does.Contain("集结超时"));
            Assert.That(deer.ReservedBy.IsNone, Is.True);
        }

        [Test]
        public void MissingGatheringSupply_ImmediatelyHandsBackToProduction()
        {
            WorldState world = Arena(1);
            NpcState npc = world.Entities.Npcs[0];
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            HuntTaskState task = Request(world, rabbit);
            int removed = npc.Inventory.Count(ItemKind.Arrow);
            npc.Inventory.TryRemove(ItemKind.Arrow, removed);
            world.Materials.Consume(ItemKind.Arrow, removed);
            HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(task.Reason, Does.Contain("生产"));
            Assert.That(ProductionSystem.NeedsArrows(npc), Is.True);
            Assert.That(rabbit.ReservedBy.IsNone, Is.True);
            Conserved(world);
        }

        [Test]
        public void LostSightRetreat_DoesNotReclaimTheTargetInTheSameUpdate()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            task.Stage = HuntStage.Approaching;
            deer.Position = new GridPos(25, 25);
            world.Tick += HuntingRules.LostTicks;
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(deer.ReservedBy.IsNone, Is.True);
        }

        [Test]
        public void NewlyVisibleWolfPack_StopsAnAlreadyPreparedTeam()
        {
            WorldState world = Arena();
            AnimalState wolf = Prey(world, AnimalSpecies.Wolf);
            HuntTaskState task = Request(world, wolf);
            task.Stage = HuntStage.Approaching;
            for (int i = 0; i < 3; i++) { world.Ecology.Spawn(world, AnimalSpecies.Wolf, new GridPos(8 + i, 7), 1500); }
            world.Materials.Initialize(world);
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(task.Reason, Does.Contain("狼群"));
            Assert.That(wolf.ReservedBy.IsNone, Is.True);
            Conserved(world);
        }

        [Test]
        public void UnseenWolfPack_DoesNotGiveTheTeamOmniscientDangerInformation()
        {
            WorldState world = Arena();
            AnimalState wolf = Prey(world, AnimalSpecies.Wolf);
            HuntTaskState task = Request(world, wolf);
            task.Stage = HuntStage.Approaching;
            for (int y = 0; y < 32; y++) { world.Map.SetTile(new GridPos(7, y), TileType.Forest); }
            for (int i = 0; i < 4; i++) { world.Ecology.Spawn(world, AnimalSpecies.Wolf, new GridPos(9, 5 + i), 1500); }
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Approaching));
        }

        [Test]
        public void BlockedRetreatRoute_StopsTheWholeTeamBeforeAttacking()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            task.Stage = HuntStage.Engaging;
            world.Entities.Npcs[0].Position = new GridPos(8, 7);
            foreach (GridPos cell in new[] { new GridPos(4, 5), new GridPos(6, 5), new GridPos(5, 4), new GridPos(5, 6) })
            {
                world.Map.SetTile(cell, TileType.Rock);
            }
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(task.Reason, Does.Contain("退路"));
            Assert.That(deer.ReservedBy.IsNone, Is.True);
        }

        [TestCase(1, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 1)]
        [TestCase(0, -1)]
        public void DrawingArcher_SidestepsAVisibleChargeAndAvoidsOccupiedSide(int dx, int dy)
        {
            WorldState world = Arena(2);
            NpcState archer = world.Entities.Npcs[0], partner = world.Entities.Npcs[1];
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            boar.Position = new GridPos(10, 10);
            archer.Position = boar.Position.Offset(dx * 3, dy * 3);
            partner.Position = archer.Position.Offset(dy == 0 ? 0 : -1, dx == 0 ? 0 : -1);
            HuntTaskState task = Request(world, boar);
            task.Stage = HuntStage.Engaging;
            Assert.That(CombatSystem.TryStartHunt(world, archer, boar, new List<SimEvent>()), Is.True);
            Assert.That(CombatSystem.RequestCharge(world, boar, archer, new List<SimEvent>()), Is.True);
            GridPos before = archer.Position;
            HuntingCoordinator.UpdateMember(world, archer, new List<SimEvent>());
            Assert.That(archer.Position.ManhattanDistance(before), Is.EqualTo(1));
            Assert.That(archer.Position, Is.Not.EqualTo(partner.Position));
            Assert.That(AnimalEncounterSystem.OnChargeLane(boar.Position, archer.Position, dx, dy), Is.False);
            Assert.That(world.Combat.GetAttack(archer.Id), Is.Null);
            Assert.That(archer.Inventory.Count(ItemKind.Arrow), Is.EqualTo(4));
            Assert.That(archer.Action.Label, Is.EqualTo("Hunt: dodge"));
            Conserved(world);
        }

        [Test]
        public void HiddenTarget_DrawKeepsLastVisibleAimAndReleasesWithoutTracking()
        {
            WorldState first = Arena(1);
            NpcState npc = first.Entities.Npcs[0];
            AnimalState rabbit = Prey(first, AnimalSpecies.Rabbit);
            rabbit.Position = new GridPos(9, 5);
            rabbit.ReservedBy = npc.Id;
            Assert.That(CombatSystem.TryStartHunt(first, npc, rabbit, new List<SimEvent>()), Is.True);
            WorldState second = WorldSnapshot.Restore(WorldSnapshot.Capture(first));
            int index = 0;
            foreach (WorldState world in new[] { first, second })
            {
                AnimalState target = world.Ecology.Find(rabbit.Id.Value);
                target.Position = new GridPos(10, 6 + index++);
                for (int y = 6; y < 10; y++) { world.Map.SetTile(new GridPos(8, y), TileType.Forest); }
                world.Tick += 2;
                CombatSystem.Update(world, new List<SimEvent>());
                Assert.That(world.Combat.GetAttack(npc.Id).AimPosition, Is.EqualTo(new GridPos(9, 5)));
                Assert.That(world.Combat.ShotsReleased, Is.EqualTo(1));
            }
            Assert.That(first.Combat.Projectiles.Single().VelocityX, Is.EqualTo(second.Combat.Projectiles.Single().VelocityX));
            Assert.That(first.Combat.Projectiles.Single().VelocityY, Is.EqualTo(second.Combat.Projectiles.Single().VelocityY));
        }

        [Test]
        public void ArrowFromUnseenOrDeadShooter_OnlyRevealsIncomingDirection()
        {
            WorldState world = Arena(1);
            NpcState npc = world.Entities.Npcs[0];
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            npc.Position = new GridPos(25, 25);
            var arrow = new ProjectileState(world.Combat.NextProjectileId++, npc.Id) { VelocityX = 2048, VelocityY = 0 };
            world.Combat.MutableProjectiles.Add(arrow);
            AnimalEncounterSystem.Hit(world, rabbit, npc.Id, false, arrow.Id);
            Assert.That(rabbit.Encounter.LastThreatPosition, Is.EqualTo(rabbit.Position.Offset(-1, 0)));
            npc.Survival.Life = LifeState.Dead;
            rabbit.Encounter.ThreatId = EntityId.None;
            AnimalEncounterSystem.Hit(world, rabbit, npc.Id, false, arrow.Id);
            Assert.That(rabbit.Encounter.Phase, Is.EqualTo(EncounterPhase.Fleeing));
            Assert.That(rabbit.Encounter.LastThreatPosition, Is.EqualTo(rabbit.Position.Offset(-1, 0)));
        }

        [Test]
        public void DetachedMember_CannotCompleteAnUnreleasedAttack()
        {
            WorldState world = Arena(1);
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            HuntTaskState task = Request(world, rabbit);
            task.Stage = HuntStage.Engaging;
            NpcState npc = world.Entities.Npcs[0];
            Assert.That(CombatSystem.TryStartHunt(world, npc, rabbit, new List<SimEvent>()), Is.True);
            HuntingCoordinator.Detach(world, npc.Id);
            Assert.That(world.Combat.GetAttack(npc.Id), Is.Null);
            Assert.That(rabbit.ReservedBy.IsNone, Is.True);
            Assert.That(world.Hunting.RetryAt(npc.Id), Is.EqualTo(world.Tick + HuntingRules.RetryTicks));
            Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(4));
        }

        [Test]
        public void TaskCarcass_IsNotPoachedByAnUnrelatedPlannerBeforeCollectorAssignment()
        {
            WorldState world = Arena();
            AnimalState deer = Prey(world);
            HuntTaskState task = Request(world, deer);
            deer.Health = 0; deer.DeathCause = AnimalDeathCause.Hunt; deer.LastDamageSourceId = task.LeaderId;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            CarcassState carcass = world.Ecology.FindCarcass(deer.Id.Value);
            task.Stage = HuntStage.Retrieving;
            carcass.ReservedBy = EntityId.None; // A completed collection batch releases the claim before the next collector.
            NpcState outsider = world.Entities.Npcs.Single(n => task.Member(n.Id) == null);
            var planner = new SurvivalPlanner(new GridPathfinder());
            Assert.That(planner.TryBuild(world, outsider, new NpcDecision(NpcGoalType.Hunt, carcass.Position,
                120, outsider.RngState, NpcDecisionTrace.Empty), out _), Is.False);
            Assert.That(carcass.ReservedBy.IsNone, Is.True);
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retrieving));
            Assert.That(world.Ecology.HuntKills, Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void FatiguedSpearMember_RetreatsWithoutStartingACoverAttack()
        {
            WorldState world = Arena(2);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            HuntTaskState task = Request(world, boar);
            NpcState spear = world.Entities.Npcs.Single(n => task.Member(n.Id).Role == HuntRole.Spear);
            spear.Position = boar.Position.Offset(1, 0);
            spear.Needs.Fatigue = HuntingRules.RetreatEnergy - 1;
            HuntingCoordinator.UpdateMember(world, spear, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(world.Combat.GetAttack(spear.Id), Is.Null);
            Assert.That(HuntingCoordinator.Authorized(world, spear, boar), Is.False);
        }

        [TestCase("food")]
        [TestCase("water")]
        [TestCase("cold")]
        public void UrgentSpearMember_RetreatsWithoutStartingACoverAttack(string need)
        {
            WorldState world = Arena(2);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            HuntTaskState task = Request(world, boar);
            NpcState spear = world.Entities.Npcs.Single(n => task.Member(n.Id).Role == HuntRole.Spear);
            spear.Position = boar.Position.Offset(1, 0);
            if (need == "food") { spear.Needs.Hunger = 1499; }
            if (need == "water") { spear.Needs.Thirst = 1799; }
            if (need == "cold") { spear.Survival.BodyTemperature = 3599; }
            HuntingCoordinator.UpdateMember(world, spear, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(world.Combat.GetAttack(spear.Id), Is.Null);
            Assert.That(HuntingCoordinator.Authorized(world, spear, boar), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CoverPreparation_HoldsPositionUnlessTheMemberDevelopsAnUrgentNeed(bool urgent)
        {
            WorldState world = Arena(2);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            HuntTaskState task = Request(world, boar);
            NpcState spear = world.Entities.Npcs.Single(n => task.Member(n.Id).Role == HuntRole.Spear);
            spear.Position = boar.Position.Offset(1, 0);
            HuntingCoordinator.Retreat(world, task, "队友撤退", new List<SimEvent>());
            HuntingCoordinator.UpdateMember(world, spear, new List<SimEvent>());
            Assert.That(world.Combat.GetAttack(spear.Id)?.Phase, Is.EqualTo(CombatAttackPhase.Preparing));
            GridPos before = spear.Position;
            world.Tick++;
            if (urgent) { spear.Needs.Hunger = 1499; }
            HuntingCoordinator.UpdateMember(world, spear, new List<SimEvent>());
            if (urgent)
            {
                Assert.That(world.Combat.GetAttack(spear.Id), Is.Null);
            }
            else
            {
                Assert.That(world.Combat.GetAttack(spear.Id)?.Phase, Is.EqualTo(CombatAttackPhase.Preparing));
                Assert.That(spear.Position, Is.EqualTo(before), "The covering thrust must release before the member leaves melee range.");
            }
            Conserved(world);
        }

        [Test]
        public void FailedAutonomousRecruitment_UsesRetryCooldownInsteadOfRepeatingEveryTick()
        {
            WorldState world = Arena(1);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            var npc = world.Entities.Npcs[0];
            var planner = new SurvivalPlanner(new GridPathfinder());
            var decision = new NpcDecision(NpcGoalType.Hunt, boar.Position, 120, npc.RngState, NpcDecisionTrace.Empty);
            Assert.That(planner.TryBuild(world, npc, decision, out _), Is.False);
            Assert.That(world.Hunting.RetryAt(npc.Id), Is.EqualTo(world.Tick + HuntingRules.RetryTicks));
            Assert.That(world.Hunting.Tasks, Is.Empty);
        }

        [Test]
        public void Preparation_SaveAfterEquippingOneWeapon_ContinuesWithIdenticalHashesAndSupply()
        {
            WorldState world = Arena(2);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Survival.SetTool(ItemKind.Bow, 0); npc.Survival.SetTool(ItemKind.Spear, 0);
                npc.Inventory.TryRemove(ItemKind.Arrow, 4);
                npc.Position = world.Resources.StockpilePosition;
            }
            world.Resources.Stockpile.TryAdd(ItemKind.Bow, 1); world.Resources.Stockpile.TryAdd(ItemKind.Spear, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 4);
            Request(world, Prey(world, AnimalSpecies.Boar));
            var engine = new SimulationEngine();
            engine.Tick(world, null);
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            for (int i = 0; i < 20; i++)
            {
                Assert.That(engine.Tick(world, null).StateHash, Is.EqualTo(engine.Tick(restored, null).StateHash));
            }
            Conserved(world); Conserved(restored);
        }

        [Test]
        public void CancellingMissingTask_ReportsChineseReasonWithoutAssistingChallenge()
        {
            WorldState world = Arena(1);
            ulong before = StateHasher.Compute(world);
            var events = new List<SimEvent>();
            Assert.That(new CancelHuntCommand(123).Apply(world, new CommandContext(world.Tick, events)), Is.False);
            Assert.That(events.Single().Message, Is.EqualTo("狩猎任务不存在或已结束"));
            Assert.That(StateHasher.Compute(world), Is.EqualTo(before));
        }
    }
}
