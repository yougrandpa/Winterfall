using System.Collections.Generic;
using System.Linq;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using static MyAiWorld.Tests.EditMode.HuntingGameplayTests;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class HuntingStabilityTests
    {
        [TestCase("food")]
        [TestCase("water")]
        [TestCase("energy")]
        [TestCase("cold")]
        public void RetreatingMember_UrgentNeedsReturnToTheSurvivalPlanner(string need)
        {
            WorldState world = Arena(1);
            NpcState hunter = world.Entities.Npcs[0];
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            HuntingCoordinator.Retreat(world, task, "停止追猎", new List<SimEvent>());
            if (need == "food") { hunter.Needs.Hunger = 1499; }
            if (need == "water") { hunter.Needs.Thirst = 1799; }
            if (need == "energy") { hunter.Needs.Fatigue = HuntingRules.RetreatEnergy - 1; }
            if (need == "cold") { hunter.Survival.BodyTemperature = 3599; }
            Assert.That(HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>()), Is.False,
                "Return false so SimulationEngine can run urgent survival planning in the same tick.");
            Assert.That(world.Hunting.ForNpc(hunter.Id), Is.Null);
            Assert.That(hunter.Plan.IsActive, Is.False);
            Assert.That(world.Combat.GetAttack(hunter.Id), Is.Null);
            Conserved(world);
        }

        [Test]
        public void ProlongedCrowding_BoundedWaitSurvivesSaveAndReleasesTheReturnTask()
        {
            WorldState world = Arena(2);
            NpcState hunter = world.Entities.Npcs[0], blocker = world.Entities.Npcs[1];
            blocker.Needs.Health = 3000;
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = HuntStage.Returning;
            hunter.Position = world.Resources.StockpilePosition.Offset(4, 0);
            blocker.Position = world.Resources.StockpilePosition.Offset(3, 0);
            for (int x = 16; x <= 22; x++)
            {
                world.Map.SetTile(new GridPos(x, 15), TileType.Rock);
                world.Map.SetTile(new GridPos(x, 17), TileType.Rock);
            }
            world.Map.SetTile(new GridPos(21, 16), TileType.Rock);
            HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            for (int i = 0; i <= HuntingRules.PathWaitTicks; i++)
            {
                foreach (WorldState branch in new[] { world, restored })
                {
                    HuntingCoordinator.UpdateMember(branch, branch.Entities.Npcs[0], new List<SimEvent>());
                    branch.Tick++;
                }
                Assert.That(StateHasher.Compute(world), Is.EqualTo(StateHasher.Compute(restored)));
            }
            Assert.That(world.Hunting.ForNpc(hunter.Id), Is.Null);
            Assert.That(hunter.Plan.FailureReason, Is.EqualTo("hunt_return_unreachable"));
            Conserved(world);
            Conserved(restored);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerStop_EndsCoverEvenWhenTheTeamIsAlreadyRetreating(bool alreadyRetreating)
        {
            WorldState world = Arena(2);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            HuntTaskState task = Request(world, boar);
            task.Stage = HuntStage.Engaging;
            NpcState spear = world.Entities.Npcs.Single(n => task.Member(n.Id).Role == HuntRole.Spear);
            spear.Position = boar.Position.Offset(1, 0);
            if (alreadyRetreating) { HuntingCoordinator.Retreat(world, task, "队员受伤、疲惫或需求告急", new List<SimEvent>()); }
            Assert.That(CombatSystem.TryStartHunt(world, spear, boar, new List<SimEvent>()), Is.True);
            var events = new List<SimEvent>();
            Assert.That(new CancelHuntCommand(task.Id).Apply(world, new CommandContext(world.Tick, events)), Is.True);
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(task.DeadlineTick, Is.EqualTo(world.Tick));
            Assert.That(world.Combat.GetAttack(spear.Id), Is.Null);
            Assert.That(events.Any(e => e.Kind == SimEventKind.CommandRejected), Is.False);
            if (alreadyRetreating) { Assert.That(task.Reason, Does.Contain("受伤")); }
            // Repeated stop orders are safe and must never reopen a cover window.
            Assert.That(new CancelHuntCommand(task.Id).Apply(world, new CommandContext(world.Tick, events)), Is.True);
            world.Tick++;
            HuntingCoordinator.UpdateMember(world, spear, events);
            Assert.That(world.Combat.GetAttack(spear.Id), Is.Null);
            Assert.That(HuntingCoordinator.Authorized(world, spear, boar), Is.False);
            Conserved(world);
        }

        [Test]
        public void AutomaticCommand_BlockedSupplyRoute_GathersWithoutWalkingBackAndForth()
        {
            WorldState world = Arena(2);
            NpcState hunter = world.Entities.Npcs[0], blocker = world.Entities.Npcs[1];
            hunter.Position = world.Resources.StockpilePosition.Offset(-4, 0);
            blocker.Position = hunter.Position.Offset(1, 0);
            blocker.Needs.Health = 3000;
            hunter.Inventory.TryRemove(ItemKind.Arrow, 4);
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 4);
            hunter.Plan.Start(NpcGoalType.GatherFood, new List<PlanStep> { new PlanStep(PlanStepType.Wait, hunter.Position, durationTicks: 100) });
            hunter.LastDecision.Goal = NpcGoalType.GatherFood.ToString();
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            rabbit.Position = world.Resources.StockpilePosition.Offset(8, 0);
            var engine = new SimulationEngine();
            TickResult result = engine.Tick(world, new IWorldCommand[] { new RequestHuntCommand(rabbit.Id, null) });
            Assert.That(result.AppliedCommands, Is.EqualTo(1));
            HuntTaskState task = world.Hunting.ForTarget(rabbit.Id);
            var positions = new List<GridPos> { hunter.Position };
            for (int i = 0; i < 20 && task.Stage == HuntStage.Gathering; i++)
            {
                engine.Tick(world, null);
                positions.Add(hunter.Position);
                Assert.That(hunter.Plan.Goal, Is.EqualTo(NpcGoalType.Hunt));
            }
            Assert.That(task.Stage, Is.Not.EqualTo(HuntStage.Gathering), string.Join(" -> ", positions));
            Assert.That(task.Stage, Is.Not.EqualTo(HuntStage.Retreating), task.Reason);
            Assert.That(hunter.Inventory.Count(ItemKind.Arrow), Is.GreaterThan(0));
            Assert.That(hunter.LastDecision.Goal, Is.EqualTo(NpcGoalType.Hunt.ToString()));
            Conserved(world);
        }

        [TestCase(HuntStage.Returning)]
        [TestCase(HuntStage.Retreating)]
        public void BlockedReturnRoute_GoesAroundTheWholeObstacleInsteadOfUndoingItsDetour(HuntStage stage)
        {
            WorldState world = Arena(2);
            NpcState hunter = world.Entities.Npcs[0], blocker = world.Entities.Npcs[1];
            blocker.Needs.Health = 3000;
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = stage;
            task.Reason = stage == HuntStage.Retreating ? "玩家取消狩猎" : "";
            GridPos destination = stage == HuntStage.Returning ? world.Resources.StockpilePosition : hunter.Home;
            hunter.Position = destination.Offset(4, 0);
            blocker.Position = destination.Offset(3, 0);
            for (int i = 0; i < 15 && world.Hunting.ForNpc(hunter.Id) != null; i++)
            {
                HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
                Assert.That(hunter.Position, Is.Not.EqualTo(blocker.Position));
                world.Tick++;
            }
            Assert.That(world.Hunting.ForNpc(hunter.Id), Is.Null, hunter.Position.ToString());
            Assert.That(hunter.Position.ManhattanDistance(destination), Is.LessThanOrEqualTo(stage == HuntStage.Returning ? 1 : 0));
        }

        [Test]
        public void Flanker_DoesNotRemainInAnInterceptCellOutsideItsWeaponRange()
        {
            WorldState world = Arena(2);
            AnimalState deer = Prey(world);
            deer.Position = new GridPos(13, 6);
            HuntTaskState task = Request(world, deer);
            task.Stage = HuntStage.Approaching;
            task.RallyCell = new GridPos(5, 6);
            task.LastSeenPosition = task.PreviousSeenPosition = deer.Position;
            HuntMemberState member = task.Members.Single(m => m.Role == HuntRole.Flanker);
            NpcState flanker = world.Entities.Npcs.Single(n => n.Id == member.NpcId);
            flanker.Position = member.WorkCell = deer.Position.Offset(5, 0);
            for (int i = 0; i < 10 && world.Combat.GetAttack(flanker.Id) == null; i++)
            {
                HuntingCoordinator.UpdateMember(world, flanker, new List<SimEvent>());
                world.Tick++;
            }
            Assert.That(world.Combat.GetAttack(flanker.Id), Is.Not.Null, flanker.Position.ToString());
            Assert.That(flanker.Position.ManhattanDistance(deer.Position), Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void Flanker_KeepsAReachableInterceptCellWhileItRemainsValid()
        {
            WorldState world = Arena(2);
            AnimalState deer = Prey(world);
            deer.Position = new GridPos(13, 6);
            HuntTaskState task = Request(world, deer);
            task.Stage = HuntStage.Approaching;
            task.LastSeenPosition = deer.Position;
            task.PreviousSeenPosition = deer.Position.Offset(-1, 0);
            HuntMemberState member = task.Members.Single(m => m.Role == HuntRole.Flanker);
            NpcState flanker = world.Entities.Npcs.Single(n => n.Id == member.NpcId);
            flanker.Position = deer.Position.Offset(5, 0);
            GridPos committed = member.WorkCell = deer.Position.Offset(3, 0);
            for (int i = 0; i < 3; i++)
            {
                HuntingCoordinator.UpdateMember(world, flanker, new List<SimEvent>());
                world.Tick++;
                Assert.That(member.WorkCell, Is.EqualTo(committed));
            }
            Assert.That(world.Combat.GetAttack(flanker.Id), Is.Not.Null);
        }

        [Test]
        public void UnseenPlayerMark_ApproachesBeforeStartingTheLostSightCountdown()
        {
            WorldState world = Arena(1);
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            rabbit.Position = new GridPos(29, 16);
            rabbit.HabitatId = world.Ecology.HabitatAt(rabbit.Position).Id;
            HuntTaskState task = Request(world, rabbit);
            task.Stage = HuntStage.Approaching;
            task.DeadlineTick = world.Tick + HuntingRules.PursuitTicks;
            world.Tick += HuntingRules.LostTicks;
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Approaching), task.Reason);
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            var engine = new SimulationEngine();
            for (int i = 0; i < 20; i++)
            {
                Assert.That(engine.Tick(world, null).StateHash, Is.EqualTo(engine.Tick(restored, null).StateHash));
            }
            Conserved(world);
            Conserved(restored);
        }

        [Test]
        public void Detour_SaveDuringMovement_ResumesTheSameRouteAndHash()
        {
            WorldState world = Arena(2);
            NpcState hunter = world.Entities.Npcs[0], blocker = world.Entities.Npcs[1];
            blocker.Needs.Health = 3000;
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = HuntStage.Returning;
            hunter.Position = world.Resources.StockpilePosition.Offset(4, 0);
            blocker.Position = world.Resources.StockpilePosition.Offset(3, 0);
            HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
            Assert.That(hunter.Action.Type, Is.EqualTo(NpcActionType.Move));
            Assert.That(hunter.Action.PathIndex, Is.GreaterThan(0));
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            var engine = new SimulationEngine();
            for (int i = 0; i < 10; i++)
            {
                Assert.That(engine.Tick(world, null).StateHash, Is.EqualTo(engine.Tick(restored, null).StateHash));
            }
            Conserved(world);
            Conserved(restored);
        }

        [Test]
        public void TemporarilyBlockedPassage_WaitsThenResumesWithoutCancellingTheHunt()
        {
            WorldState world = Arena(2);
            NpcState hunter = world.Entities.Npcs[0], blocker = world.Entities.Npcs[1];
            blocker.Needs.Health = 3000;
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = HuntStage.Returning;
            hunter.Position = world.Resources.StockpilePosition.Offset(4, 0);
            blocker.Position = world.Resources.StockpilePosition.Offset(3, 0);
            for (int x = 16; x <= 22; x++)
            {
                world.Map.SetTile(new GridPos(x, 15), TileType.Rock);
                world.Map.SetTile(new GridPos(x, 17), TileType.Rock);
            }
            world.Map.SetTile(new GridPos(21, 16), TileType.Rock);
            GridPos before = hunter.Position;
            for (int i = 0; i < 3; i++)
            {
                HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
                world.Tick++;
                Assert.That(hunter.Position, Is.EqualTo(before));
                Assert.That(task.Stage, Is.EqualTo(HuntStage.Returning));
            }
            blocker.Position = new GridPos(10, 10);
            for (int i = 0; i < 8 && world.Hunting.ForNpc(hunter.Id) != null; i++)
            {
                HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
                world.Tick++;
            }
            Assert.That(world.Hunting.ForNpc(hunter.Id), Is.Null);
            Assert.That(hunter.Plan.IsFailed, Is.False);
        }

        [Test]
        public void UnseenTarget_StillHonorsThePursuitDeadline()
        {
            WorldState world = Arena(1);
            AnimalState rabbit = Prey(world, AnimalSpecies.Rabbit);
            rabbit.Position = new GridPos(29, 16);
            HuntTaskState task = Request(world, rabbit);
            task.Stage = HuntStage.Approaching;
            task.DeadlineTick = world.Tick + HuntingRules.PursuitTicks;
            world.Tick = task.DeadlineTick;
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(task.Reason, Does.Contain("追猎超时"));
            Assert.That(rabbit.ReservedBy.IsNone, Is.True);
        }

        [TestCase(-2L)]
        [TestCase(0L)]
        [TestCase(long.MaxValue)]
        public void Snapshot_RejectsInvalidSightTimesApartFromTheUnseenSentinel(long sightTick)
        {
            WorldState world = Arena(1);
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.LastSeenTick = sightTick;
            Assert.Throws<System.IO.InvalidDataException>(() => WorldSnapshot.Restore(WorldSnapshot.Capture(world)));
        }

        [Test]
        public void SuccessfulReturn_CompletesThePlanWithoutRecordingAFailure()
        {
            WorldState world = Arena(1);
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            NpcState hunter = world.Entities.Npcs[0];
            task.Stage = HuntStage.Returning;
            hunter.Position = world.Resources.StockpilePosition;
            HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
            Assert.That(hunter.Plan.IsActive, Is.False);
            Assert.That(hunter.Plan.IsFailed, Is.False);
            Assert.That(hunter.Plan.ReplanCount, Is.Zero);
            Assert.That(hunter.Plan.FailureReason, Is.EqualTo("none"));
        }

        [Test]
        public void RetreatReturn_PreservesItsActualReasonInsteadOfReportingHuntComplete()
        {
            WorldState world = Arena(1);
            HuntTaskState task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            NpcState hunter = world.Entities.Npcs[0];
            HuntingCoordinator.Retreat(world, task, "队员受伤、疲惫或需求告急", new List<SimEvent>());
            hunter.Position = hunter.Home;
            HuntingCoordinator.UpdateMember(world, hunter, new List<SimEvent>());
            Assert.That(hunter.Plan.FailureReason, Is.EqualTo(task.Reason));
            Assert.That(hunter.Action.Label, Is.Not.EqualTo("Hunt: complete"));
        }
    }
}
