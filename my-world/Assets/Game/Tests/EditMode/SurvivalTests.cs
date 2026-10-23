using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class SurvivalTests
    {
        private static WorldState EmptyWorld(int npcCount = 1)
        {
            var config = new WorldConfigData(42, 16, 16, npcCount, GameCalendar.StartTick, 2, new[] { 1 });
            var world = new WorldState(config, new WorldMap(16, 16), new DeterministicRng(42));
            for (int i = 0; i < npcCount; i++)
            {
                world.Entities.AddNpc(new NpcState(world.AllocateEntityId(), new NpcIdentity("Test " + i, "Farmer", 30),
                    new Personality(), new GridPos(8 + i, 8), (ulong)(10 + i)));
            }

            return world;
        }

        private static void StartPlan(NpcState npc, NpcGoalType goal, params PlanStep[] steps)
        {
            npc.Plan.Start(goal, new List<PlanStep>(steps));
        }

        private static void FinishPlan(WorldState world, NpcState npc, int maximumTicks = 200)
        {
            var engine = new SimulationEngine();
            for (int i = 0; i < maximumTicks && npc.Plan.IsActive; i++)
            {
                engine.Tick(world, null);
            }

            Assert.That(npc.Plan.IsActive, Is.False, "Plan did not complete: " + npc.Plan.Summary);
            Assert.That(npc.Plan.FailureReason, Is.EqualTo("none"), "Plan failed instead of completing.");
        }

        [Test]
        public void Leisure_AfterUsingStockpile_ChoosesReachableWalkAndBreakAwayFromCounter()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 200);
            world.Resources.Stockpile.TryAdd(ItemKind.Water, 200);
            var policy = new RuleBasedNpcPolicy();
            ulong before = npc.RngState;
            var context = new NpcDecisionContext(npc, world.Map, world.Tick, world);
            NpcDecision first = policy.Decide(context);
            NpcDecision second = policy.Decide(context);

            Assert.That(first.Goal, Is.EqualTo(NpcGoalType.Wander));
            Assert.That(first.Target.ManhattanDistance(world.Resources.StockpilePosition), Is.GreaterThan(2));
            Assert.That(first.Trace.ReasonCode, Is.EqualTo("clear_shared_counter"));
            Assert.That(second.Target, Is.EqualTo(first.Target));
            Assert.That(second.UpdatedRngState, Is.EqualTo(first.UpdatedRngState));
            Assert.That(npc.RngState, Is.EqualTo(before), "A decision proposal must not mutate the NPC.");

            var planner = new SurvivalPlanner(new MyAiWorld.Sim.Navigation.GridPathfinder());
            string failure;
            Assert.That(planner.TryBuild(world, npc, first, out failure), Is.True, failure);
            Assert.That(npc.Plan.Steps[0].Type, Is.EqualTo(PlanStepType.Move));
            Assert.That(npc.Plan.Steps[1].Type, Is.EqualTo(PlanStepType.Wait));
            Assert.That(npc.Plan.Steps[1].Target, Is.EqualTo(first.Target));
            FinishPlan(world, npc);
            Assert.That(npc.Position, Is.EqualTo(first.Target));
        }

        [Test]
        public void Leisure_DoesNotReplaceUrgentSurvivalOrTrapIsolatedNpc()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 200);
            world.Resources.Stockpile.TryAdd(ItemKind.Water, 200);
            var policy = new RuleBasedNpcPolicy();
            npc.Needs.Thirst = 1000;
            NpcDecision decision = policy.Decide(new NpcDecisionContext(npc, world.Map, world.Tick, world));
            Assert.That(decision.Goal, Is.EqualTo(NpcGoalType.Drink));

            npc.Needs.Thirst = 10000;
            for (int y = 0; y < world.Map.Height; y++)
            for (int x = 0; x < world.Map.Width; x++)
            {
                var cell = new GridPos(x, y);
                if (cell != npc.Position) { world.Map.SetTile(cell, TileType.Water); }
            }
            decision = policy.Decide(new NpcDecisionContext(npc, world.Map, world.Tick, world));
            Assert.That(decision.Goal, Is.EqualTo(NpcGoalType.Rest));
            Assert.That(decision.Target, Is.EqualTo(npc.Position));
        }

        [Test]
        public void DefaultWorld_IdleNpcs_WalkAndRestOutsideSharedCounter()
        {
            WorldState world = WorldFactory.Create(WorldConfigData.CreateDefault());
            var engine = new SimulationEngine();
            var walkers = new HashSet<EntityId>();
            int breaks = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                engine.Tick(world, null);
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    if (!npc.Plan.IsActive || npc.Plan.Goal != NpcGoalType.Wander) { continue; }
                    walkers.Add(npc.Id);
                    if (npc.Plan.CurrentStep.Type == PlanStepType.Wait && npc.Plan.StepStarted)
                    {
                        breaks++;
                        Assert.That(npc.Position.ManhattanDistance(world.Resources.StockpilePosition), Is.GreaterThan(2));
                    }
                }
            }
            Assert.That(walkers.Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(breaks, Is.GreaterThan(0));
            AssertWorldInvariants(world);
        }

        [Test]
        public void Needs_IdleTimeReducesSatisfaction_AndDeprivationDamagesHealth()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            int food = npc.Needs.Hunger;
            int water = npc.Needs.Thirst;
            int energy = npc.Needs.Fatigue;
            StartPlan(npc, NpcGoalType.Rest, new PlanStep(PlanStepType.Wait, npc.Position, durationTicks: 100));
            var engine = new SimulationEngine();
            for (int i = 0; i < 20; i++) { engine.Tick(world, null); }

            Assert.That(npc.Needs.Hunger, Is.LessThan(food));
            Assert.That(npc.Needs.Thirst, Is.LessThan(water));
            Assert.That(npc.Needs.Fatigue, Is.LessThan(energy));
            npc.Needs.Hunger = 0;
            int health = npc.Needs.Health;
            engine.Tick(world, null);
            Assert.That(npc.Needs.Hunger, Is.Zero);
            Assert.That(npc.Needs.Health, Is.LessThan(health));
        }

        [Test]
        public void EatingAndDrinking_ConsumeExactlyOneCarriedItem_AndRestoreSatisfaction()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            npc.Needs.Hunger = 3000;
            npc.Needs.Thirst = 3000;
            npc.Inventory.TryAdd(ItemKind.Food, 2);
            npc.Inventory.TryAdd(ItemKind.Water, 2);
            StartPlan(npc, NpcGoalType.Eat, new PlanStep(PlanStepType.Eat, npc.Position));
            FinishPlan(world, npc);
            Assert.That(npc.Needs.Hunger, Is.GreaterThan(6000));
            Assert.That(npc.Inventory.Food, Is.EqualTo(1));
            Assert.That(npc.Inventory.Water, Is.EqualTo(2));

            StartPlan(npc, NpcGoalType.Drink, new PlanStep(PlanStepType.Drink, npc.Position));
            FinishPlan(world, npc);
            Assert.That(npc.Needs.Thirst, Is.GreaterThan(7000));
            Assert.That(npc.Inventory.Water, Is.EqualTo(1));
            Assert.That(npc.Inventory.Food, Is.EqualTo(1));
        }

        [Test]
        public void SleepingAtHome_RestoresEnergy_WithoutCreatingFoodOrWater()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            npc.Needs.Fatigue = 2000;
            npc.Inventory.TryAdd(ItemKind.Food, 1);
            StartPlan(npc, NpcGoalType.Sleep, new PlanStep(PlanStepType.Sleep, npc.Home, durationTicks: 100));
            FinishPlan(world, npc);

            Assert.That(npc.Needs.Fatigue, Is.GreaterThan(6000));
            Assert.That(npc.Inventory.Food, Is.EqualTo(1));
            Assert.That(npc.Inventory.Water, Is.Zero);
            Assert.That(npc.Position, Is.EqualTo(npc.Home));
        }

        [Test]
        public void StockTransfers_ConserveItems_AndRejectFullDestinationAtomically()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 7);
            Assert.That(world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Stockpile, 0,
                ItemKind.Food, 3, world.Resources.StockpilePosition)), Is.True);
            StartPlan(npc, NpcGoalType.Eat, new PlanStep(PlanStepType.TakeFromStock, npc.Position, quantity: 3));
            FinishPlan(world, npc);
            Assert.That(npc.Inventory.Food, Is.EqualTo(3));
            Assert.That(world.Resources.Stockpile.Food, Is.EqualTo(4));

            StartPlan(npc, NpcGoalType.Rest, new PlanStep(PlanStepType.Deposit, npc.Position, quantity: 3));
            FinishPlan(world, npc);
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(world.Resources.Stockpile.Food, Is.EqualTo(7));

            world.Resources.Stockpile.TryAdd(ItemKind.Seed, world.Resources.Stockpile.Capacity - 7);
            npc.Inventory.TryAdd(ItemKind.Food, 3);
            StartPlan(npc, NpcGoalType.Rest, new PlanStep(PlanStepType.Deposit, npc.Position, quantity: 3));
            TickResult failure = new SimulationEngine().Tick(world, null);
            Assert.That(failure.Events.Exists(evt => evt.Kind == SimEventKind.PlanFailed), Is.True);
            Assert.That(npc.Inventory.Food, Is.EqualTo(3));
            Assert.That(world.Resources.Stockpile.Food, Is.EqualTo(7));
            Assert.That(world.Resources.Stockpile.TotalCount, Is.EqualTo(world.Resources.Stockpile.Capacity));
        }

        [Test]
        public void SharedNodeReservation_PreventsTwoOwners_AndIsReleasedAfterGathering()
        {
            WorldState world = EmptyWorld(2);
            NpcState first = world.Entities.Npcs[0];
            NpcState second = world.Entities.Npcs[1];
            var node = new ResourceNodeState(1, first.Position, ItemKind.Food, 5, 1000);
            world.Resources.AddNode(node);
            var firstClaim = new ResourceReservation(first.Id, ReservationKind.Node, node.Id, ItemKind.Food, 2, node.Position);
            var secondClaim = new ResourceReservation(second.Id, ReservationKind.Node, node.Id, ItemKind.Food, 2, node.Position);
            Assert.That(world.Resources.TryReserve(firstClaim), Is.True);
            Assert.That(world.Resources.TryReserve(secondClaim), Is.False);
            StartPlan(first, NpcGoalType.GatherFood, new PlanStep(PlanStepType.Gather, node.Position, node.Id, quantity: 2));
            StartPlan(second, NpcGoalType.Rest, new PlanStep(PlanStepType.Wait, second.Position, durationTicks: 100));
            FinishPlan(world, first);

            Assert.That(first.Inventory.Food, Is.EqualTo(2));
            Assert.That(node.Amount, Is.EqualTo(3));
            Assert.That(node.ReservedBy.IsNone, Is.True);
            Assert.That(world.Resources.Reservations.Count, Is.Zero);
            Assert.That(world.Resources.TryReserve(secondClaim), Is.True);
        }

        [Test]
        public void ResourceExhaustionDuringAction_FailsWithoutCreatingItems_AndReleasesReservation()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            var node = new ResourceNodeState(1, npc.Position, ItemKind.Food, 2, 1000);
            world.Resources.AddNode(node);
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Node, node.Id, ItemKind.Food, 2, node.Position));
            StartPlan(npc, NpcGoalType.GatherFood, new PlanStep(PlanStepType.Gather, node.Position, node.Id, quantity: 2, durationTicks: 2));
            var engine = new SimulationEngine();
            engine.Tick(world, null);
            node.Amount = 0;
            engine.Tick(world, null);
            TickResult result = engine.Tick(world, null);

            Assert.That(result.Events.Exists(evt => evt.Kind == SimEventKind.PlanFailed), Is.True);
            Assert.That(npc.Plan.ReplanCount, Is.GreaterThan(0));
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(node.Amount, Is.Zero);
            Assert.That(node.ReservedBy.IsNone, Is.True);
            Assert.That(world.Resources.Reservations.Count, Is.Zero);
            node.Amount = 2;
            bool gatheredAfterReplan = false;
            for (int i = 0; i < 120; i++)
            {
                TickResult retry = engine.Tick(world, null);
                gatheredAfterReplan |= retry.Events.Exists(evt => evt.Kind == SimEventKind.ResourceGathered);
                if (gatheredAfterReplan) { break; }
            }
            Assert.That(gatheredAfterReplan, Is.True, "Replenished resources must become usable after a failed action.");
            Assert.That(npc.Inventory.Food, Is.EqualTo(2));
            Assert.That(node.Amount, Is.Zero);
        }

        [Test]
        public void BlockedPath_ReleasesClaim_AndReplansInsteadOfWalkingThroughObstacle()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            GridPos target = npc.Position.Offset(3, 0);
            var node = new ResourceNodeState(1, target, ItemKind.Food, 2, 1000);
            world.Resources.AddNode(node);
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Node, node.Id, ItemKind.Food, 1, target));
            StartPlan(npc, NpcGoalType.GatherFood, new PlanStep(PlanStepType.Move, target),
                new PlanStep(PlanStepType.Gather, target, node.Id));
            var engine = new SimulationEngine();
            engine.Tick(world, null);
            GridPos next = npc.Action.PeekNextStep();
            Assert.That(next.IsValid, Is.True);
            world.Map.SetTile(next, TileType.Rock);
            TickResult result = engine.Tick(world, null);

            Assert.That(result.Events.Exists(evt => evt.Kind == SimEventKind.NpcPathFailed), Is.True);
            Assert.That(npc.Position, Is.Not.EqualTo(next));
            Assert.That(node.ReservedBy.IsNone, Is.True);
            Assert.That(world.Resources.Reservations.Count, Is.Zero);
            Assert.That(npc.Plan.FailureReason, Is.EqualTo("path_blocked"));
            for (int i = 0; i < 10; i++) { engine.Tick(world, null); }
            Assert.That(npc.Plan.IsActive, Is.True, "NPC should replace its failed plan.");
            Assert.That(world.Map.IsWalkable(npc.Position), Is.True);
        }

        [Test]
        public void UnreachableNearbyFood_IsSkippedForReachableAlternative()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            npc.Needs.Hunger = 3000;
            for (int y = 0; y < world.Map.Height; y++) { world.Map.SetTile(new GridPos(9, y), TileType.Rock); }
            var unreachable = new ResourceNodeState(1, new GridPos(10, 8), ItemKind.Food, 3, 1000);
            var reachable = new ResourceNodeState(2, new GridPos(2, 8), ItemKind.Food, 3, 1000);
            world.Resources.AddNode(unreachable);
            world.Resources.AddNode(reachable);
            var engine = new SimulationEngine();

            engine.Tick(world, null);
            Assert.That(npc.Plan.Goal, Is.EqualTo(NpcGoalType.Eat));
            Assert.That(unreachable.ReservedBy.IsNone, Is.True);
            Assert.That(reachable.ReservedBy, Is.EqualTo(npc.Id));
            bool ate = false;
            for (int i = 0; i < 100; i++)
            {
                engine.Tick(world, null);
                if (npc.Needs.Hunger > 6000) { ate = true; break; }
            }

            Assert.That(ate, Is.True, "An unreachable preferred resource must not prevent eating elsewhere.");
            Assert.That(unreachable.Amount, Is.EqualTo(3));
            Assert.That(reachable.Amount, Is.EqualTo(2));
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(world.Resources.Reservations.Count, Is.Zero);
        }

        [Test]
        public void CropLifecycle_RequiresSeedAndWater_GrowsThenHarvestsFoodAndSeeds()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            var plot = new FarmPlotState(1, npc.Position);
            world.Resources.AddPlot(plot);
            npc.Inventory.TryAdd(ItemKind.Seed, 1);
            npc.Inventory.TryAdd(ItemKind.Water, 1);
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food, 0, plot.Position));
            int fertility = plot.Fertility;
            StartPlan(npc, NpcGoalType.Farm, new PlanStep(PlanStepType.Plant, plot.Position, plot.Id));
            FinishPlan(world, npc);
            Assert.That(plot.Stage, Is.EqualTo(CropStage.Seedling));
            Assert.That(npc.Inventory.Seeds, Is.Zero);
            Assert.That(plot.Fertility, Is.LessThan(fertility));
            Assert.That(plot.Moisture, Is.Zero);

            // Empty worlds let crop time advance without an NPC watering automatically.
            for (int i = 0; i < 20; i++) { world.Resources.Update(); AgricultureSystem.Update(world); }
            Assert.That(plot.GrowthTicks, Is.Zero, "Dry crops must not grow.");
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food, 0, plot.Position));
            StartPlan(npc, NpcGoalType.Farm, new PlanStep(PlanStepType.Water, plot.Position, plot.Id));
            FinishPlan(world, npc);
            Assert.That(npc.Inventory.Water, Is.Zero);
            Assert.That(plot.Moisture, Is.GreaterThan(0));
            for (int i = 0; i < plot.GrowthRequired; i++) { world.Resources.Update(); AgricultureSystem.Update(world); }
            Assert.That(plot.Stage, Is.EqualTo(CropStage.Mature));

            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food, 0, plot.Position));
            StartPlan(npc, NpcGoalType.Farm, new PlanStep(PlanStepType.Harvest, plot.Position, plot.Id));
            FinishPlan(world, npc);
            Assert.That(plot.Stage, Is.EqualTo(CropStage.Empty));
            Assert.That(npc.Inventory.Food, Is.EqualTo(CropDefinition.Turnip.Yield));
            Assert.That(npc.Inventory.Seeds, Is.EqualTo(CropDefinition.Turnip.SeedYield));
            int depleted = plot.Fertility;
            for (int i = 0; i < 20; i++) { world.Resources.Update(); AgricultureSystem.Update(world); }
            Assert.That(plot.Fertility, Is.GreaterThan(depleted));
            Assert.That(plot.Fertility, Is.LessThanOrEqualTo(10000));
        }

        [Test]
        public void ResourceNode_RegeneratesGradually_AndNeverExceedsCapacity()
        {
            WorldState world = EmptyWorld();
            var node = new ResourceNodeState(1, new GridPos(4, 4), ItemKind.Food, 3, 10);
            node.Amount = 0;
            world.Resources.AddNode(node);
            for (int i = 0; i < 9; i++) { world.Resources.Update(); AgricultureSystem.Update(world); }
            Assert.That(node.Amount, Is.Zero);
            world.Resources.Update();
            Assert.That(node.Amount, Is.EqualTo(1));
            for (int i = 0; i < 100; i++) { world.Resources.Update(); AgricultureSystem.Update(world); }
            Assert.That(node.Amount, Is.EqualTo(node.Capacity));
            Assert.That(node.RegenerationProgress, Is.Zero);
        }

        [Test]
        public void CriticalThirst_InterruptsWork_ReleasesClaims_AndStartsDrinkingPlan()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            var node = new ResourceNodeState(1, npc.Position, ItemKind.Food, 5, 1000);
            world.Resources.AddNode(node);
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Node, node.Id, ItemKind.Food, 2, node.Position));
            StartPlan(npc, NpcGoalType.GatherFood, new PlanStep(PlanStepType.Gather, npc.Position, node.Id, quantity: 2, durationTicks: 100));
            npc.Inventory.TryAdd(ItemKind.Water, 1);
            npc.Needs.Thirst = 1000;

            TickResult result = new SimulationEngine().Tick(world, null);

            Assert.That(result.Events.Exists(evt => evt.Kind == SimEventKind.PlanInterrupted), Is.True);
            Assert.That(node.ReservedBy.IsNone, Is.True);
            Assert.That(world.Resources.Reservations.Count, Is.Zero);
            Assert.That(npc.Plan.Goal, Is.EqualTo(NpcGoalType.Drink));
            Assert.That(npc.Plan.IsActive, Is.True);
            Assert.That(node.Amount, Is.EqualTo(5));
            Assert.That(npc.Inventory.Food, Is.Zero);
        }

        [Test]
        public void Harvest_InsufficientBagSpace_LeavesCropAndBothOutputsUntouched()
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            var plot = new FarmPlotState(1, npc.Position);
            plot.Crop = CropType.Turnip;
            plot.GrowthTicks = plot.GrowthRequired;
            world.Resources.AddPlot(plot);
            npc.Inventory.TryAdd(ItemKind.Water, npc.Inventory.Capacity - 5);
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food, 0, plot.Position));
            StartPlan(npc, NpcGoalType.Farm, new PlanStep(PlanStepType.Harvest, plot.Position, plot.Id));

            TickResult result = new SimulationEngine().Tick(world, null);

            Assert.That(result.Events.Exists(evt => evt.Kind == SimEventKind.PlanFailed), Is.True);
            Assert.That(plot.Stage, Is.EqualTo(CropStage.Mature));
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(npc.Inventory.Seeds, Is.Zero);
            Assert.That(npc.Inventory.Water, Is.EqualTo(npc.Inventory.Capacity - 5));
            Assert.That(plot.ReservedBy.IsNone, Is.True);
        }

        [TestCase(ItemKind.Food)]
        [TestCase(ItemKind.Water)]
        public void FullBagOfUnneededSupplies_IsDepositedBeforeAcquiringSurvivalItem(ItemKind needed)
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            int carriedSeeds = npc.Inventory.Capacity;
            npc.Inventory.TryAdd(ItemKind.Seed, carriedSeeds);
            world.Resources.Stockpile.TryAdd(needed, 4);
            if (needed == ItemKind.Food) { npc.Needs.Hunger = 3000; }
            else { npc.Needs.Thirst = 3000; }
            var engine = new SimulationEngine();
            bool restored = false;

            for (int i = 0; i < 100; i++)
            {
                engine.Tick(world, null);
                int satisfaction = needed == ItemKind.Food ? npc.Needs.Hunger : npc.Needs.Thirst;
                if (satisfaction > 6000) { restored = true; break; }
            }

            Assert.That(restored, Is.True, "A full bag must not trap the NPC in repeated survival failures.");
            Assert.That(npc.Inventory.Seeds + world.Resources.Stockpile.Seeds, Is.EqualTo(carriedSeeds));
            Assert.That(npc.Inventory.Seeds, Is.Zero, "Unneeded seeds should have been safely stored.");
            Assert.That(npc.Inventory.Count(needed) + world.Resources.Stockpile.Count(needed), Is.EqualTo(3),
                "Recovery should consume exactly one survival item.");
            Assert.That(npc.Plan.ReplanCount, Is.Zero, "Recoverable bag pressure should not repeatedly fail plans.");
        }

        [TestCase("carried_items")]
        [TestCase("stockpile_items")]
        [TestCase("node_amount")]
        [TestCase("node_regeneration")]
        [TestCase("crop_growth")]
        [TestCase("crop_moisture")]
        [TestCase("soil_fertility")]
        [TestCase("plan_goal")]
        [TestCase("plan_progress")]
        [TestCase("plan_started")]
        [TestCase("plan_retries")]
        [TestCase("plan_failure")]
        [TestCase("plan_failed_step")]
        [TestCase("plan_step_quantity")]
        [TestCase("reservation_quantity")]
        public void StateHash_ChangesWhenAuthoritativeSurvivalStateChanges(string change)
        {
            WorldState world = EmptyWorld();
            NpcState npc = world.Entities.Npcs[0];
            var node = new ResourceNodeState(1, new GridPos(4, 4), ItemKind.Food, 5, 1000);
            var plot = new FarmPlotState(1, new GridPos(5, 5));
            plot.Crop = CropType.Turnip;
            world.Resources.AddNode(node);
            world.Resources.AddPlot(plot);
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 5);
            StartPlan(npc, NpcGoalType.Eat, new PlanStep(PlanStepType.TakeFromStock, npc.Position),
                new PlanStep(PlanStepType.Eat, npc.Position));
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Stockpile, 0,
                ItemKind.Food, 1, world.Resources.StockpilePosition));
            ulong before = StateHasher.Compute(world);

            switch (change)
            {
                case "carried_items": npc.Inventory.TryAdd(ItemKind.Food, 1); break;
                case "stockpile_items": world.Resources.Stockpile.TryAdd(ItemKind.Water, 1); break;
                case "node_amount": node.Amount--; break;
                case "node_regeneration": node.RegenerationProgress++; break;
                case "crop_growth": plot.GrowthTicks++; break;
                case "crop_moisture": plot.Moisture++; break;
                case "soil_fertility": plot.Fertility--; break;
                case "plan_goal": npc.Plan.Goal = NpcGoalType.Drink; break;
                case "plan_progress": npc.Plan.StepIndex++; break;
                case "plan_started": npc.Plan.StepStarted = true; break;
                case "plan_retries": npc.Plan.ReplanCount++; break;
                case "plan_failure": npc.Plan.FailureReason = "resource_changed"; break;
                case "plan_failed_step": npc.Plan.FailedStepIndex = 0; break;
                case "plan_step_quantity":
                    StartPlan(npc, NpcGoalType.Eat, new PlanStep(PlanStepType.TakeFromStock, npc.Position, quantity: 2),
                        new PlanStep(PlanStepType.Eat, npc.Position));
                    break;
                case "reservation_quantity":
                    world.Resources.Release(npc.Id);
                    Assert.That(world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Stockpile, 0,
                        ItemKind.Food, 2, world.Resources.StockpilePosition)), Is.True);
                    break;
                default: Assert.Fail("Unknown test state: " + change); break;
            }

            Assert.That(StateHasher.Compute(world), Is.Not.EqualTo(before), change + " is missing from the replay hash.");
        }

        [Test]
        public void DefaultWorld_SevenDays_UsesResourceLoopAndKeepsEveryNpcHealthy()
        {
            WorldState world = WorldFactory.Create(WorldConfigData.CreateDefault());
            var engine = new SimulationEngine();
            var ate = new HashSet<EntityId>();
            var drank = new HashSet<EntityId>();
            var slept = new HashSet<EntityId>();
            var eventCounts = new Dictionary<SimEventKind, int>();
            int[] previousFood = new int[20];
            int[] previousWater = new int[20];
            int[] previousEnergy = new int[20];
            Assert.That(world.Entities.Npcs.Count, Is.EqualTo(20));
            long start = world.Tick;

            for (int tick = 0; tick < GameCalendar.TicksPerDay * 7; tick++)
            {
                for (int i = 0; i < 20; i++)
                {
                    previousFood[i] = world.Entities.Npcs[i].Needs.Hunger;
                    previousWater[i] = world.Entities.Npcs[i].Needs.Thirst;
                    previousEnergy[i] = world.Entities.Npcs[i].Needs.Fatigue;
                }

                TickResult result = engine.Tick(world, null);
                foreach (SimEvent evt in result.Events)
                {
                    int count;
                    eventCounts.TryGetValue(evt.Kind, out count);
                    eventCounts[evt.Kind] = count + 1;
                }

                for (int i = 0; i < 20; i++)
                {
                    NpcState npc = world.Entities.Npcs[i];
                    if (npc.Needs.Hunger > previousFood[i]) { ate.Add(npc.Id); }
                    if (npc.Needs.Thirst > previousWater[i]) { drank.Add(npc.Id); }
                    if (npc.Needs.Fatigue > previousEnergy[i]) { slept.Add(npc.Id); }
                }

                if (tick % 60 == 0) { AssertWorldInvariants(world); }
            }

            Assert.That(world.Tick - start, Is.EqualTo(10080));
            Assert.That(world.Entities.Npcs.Count, Is.EqualTo(20));
            Assert.That(ate.Count, Is.EqualTo(20), "Every NPC must actually eat during the survival run.");
            Assert.That(drank.Count, Is.EqualTo(20), "Every NPC must actually drink during the survival run.");
            Assert.That(slept.Count, Is.EqualTo(20), "Every NPC must actually recover energy by sleeping.");
            foreach (SimEventKind kind in new[] { SimEventKind.CropPlanted, SimEventKind.CropWatered,
                SimEventKind.CropHarvested, SimEventKind.ResourceGathered })
            {
                Assert.That(eventCounts.ContainsKey(kind) && eventCounts[kind] > 0, Is.True,
                    "A seven-day run must exercise " + kind + ".");
            }

            AssertWorldInvariants(world);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                Assert.That(npc.Needs.Health, Is.GreaterThanOrEqualTo(8000), npc.Name + " lost too much health.");
                Assert.That(npc.Needs.Hunger, Is.GreaterThan(0), npc.Name + " ended starving.");
                Assert.That(npc.Needs.Thirst, Is.GreaterThan(0), npc.Name + " ended dehydrated.");
                Assert.That(npc.Needs.Fatigue, Is.GreaterThan(0), npc.Name + " ended exhausted.");
            }
        }

        private static void AssertWorldInvariants(WorldState world)
        {
            AssertInventory(world.Resources.Stockpile);
            foreach (NpcState npc in world.Entities.Npcs)
            {
                AssertInventory(npc.Inventory);
                Assert.That(npc.Needs.Hunger, Is.InRange(0, NeedsState.MaxValue));
                Assert.That(npc.Needs.Thirst, Is.InRange(0, NeedsState.MaxValue));
                Assert.That(npc.Needs.Fatigue, Is.InRange(0, NeedsState.MaxValue));
                Assert.That(npc.Needs.Health, Is.InRange(1, NeedsState.MaxValue));
                Assert.That(npc.Needs.Safety, Is.InRange(0, NeedsState.MaxValue));
                Assert.That(world.Map.IsWalkable(npc.Position), Is.True);
            }

            foreach (ResourceNodeState node in world.Resources.Nodes)
            {
                Assert.That(node.Amount, Is.InRange(0, node.Capacity));
            }

            foreach (FarmPlotState plot in world.Resources.FarmPlots)
            {
                Assert.That(plot.Fertility, Is.InRange(0, 10000));
                Assert.That(plot.Moisture, Is.InRange(0, 1800));
                Assert.That(plot.GrowthTicks, Is.InRange(0, plot.GrowthRequired));
            }

            var claimedPositions = new Dictionary<GridPos, EntityId>();
            foreach (ResourceReservation reservation in world.Resources.Reservations)
            {
                EntityId owner;
                if (claimedPositions.TryGetValue(reservation.Position, out owner))
                {
                    Assert.That(reservation.Owner, Is.EqualTo(owner), "Two NPCs claimed the same work location.");
                }
                else { claimedPositions.Add(reservation.Position, reservation.Owner); }
                NpcState npc;
                Assert.That(world.Entities.TryGetNpc(reservation.Owner, out npc), Is.True);
                Assert.That(npc.Plan.IsActive, Is.True, "Completed plans must release all claims.");
            }
        }

        private static void AssertInventory(InventoryState inventory)
        {
            Assert.That(inventory.Food, Is.GreaterThanOrEqualTo(0));
            Assert.That(inventory.Water, Is.GreaterThanOrEqualTo(0));
            Assert.That(inventory.Seeds, Is.GreaterThanOrEqualTo(0));
            Assert.That(inventory.TotalCount, Is.InRange(0, inventory.Capacity));
        }
    }
}
