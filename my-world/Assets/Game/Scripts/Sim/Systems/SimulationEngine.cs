using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public sealed class TickResult
    {
        public TickResult(long tick, ulong stateHash, int appliedCommands, List<SimEvent> events)
        {
            Tick = tick;
            StateHash = stateHash;
            AppliedCommands = appliedCommands;
            Events = events ?? new List<SimEvent>();
        }

        public long Tick { get; }
        public ulong StateHash { get; }
        public int AppliedCommands { get; }
        public List<SimEvent> Events { get; }
    }

    public sealed class SimulationEngine
    {
        private readonly INpcDecisionPolicy _decisionPolicy;
        private readonly GridPathfinder _pathfinder;
        private readonly SurvivalPlanner _planner;

        public SimulationEngine()
            : this(new RuleBasedNpcPolicy(), new GridPathfinder())
        {
        }

        public SimulationEngine(INpcDecisionPolicy decisionPolicy, GridPathfinder pathfinder)
        {
            _decisionPolicy = decisionPolicy ?? throw new ArgumentNullException(nameof(decisionPolicy));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _planner = new SurvivalPlanner(_pathfinder);
        }

        public TickResult Tick(WorldState world, IReadOnlyList<IWorldCommand> commands)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var events = new List<SimEvent>();
            int appliedCommands = ApplyCommands(world, commands, events);
            EmitCalendarEvents(world, events);
            world.Resources.Update(world.Materials, world.Date.Season, world.Tick);
            AgricultureSystem.Update(world);
            WaterSystem.Update(world);
            world.Ecology.Update(world, events);
            if (world.Tick % 60 == 0)
            {
                world.Market.UpdatePrices();
            }

            IReadOnlyList<NpcState> npcs = world.Entities.Npcs;
            for (int i = 0; i < npcs.Count; i++)
            {
                UpdateNpc(world, npcs[i], events);
            }

            world.Survival.MutableGround.RemoveAll(g => g.Empty && g.ReservedBy.IsNone);
            world.Tick++;
            ulong stateHash = StateHasher.Compute(world);
            return new TickResult(world.Tick, stateHash, appliedCommands, events);
        }

        private static int ApplyCommands(
            WorldState world,
            IReadOnlyList<IWorldCommand> commands,
            List<SimEvent> events)
        {
            if (commands == null || commands.Count == 0)
            {
                return 0;
            }

            var context = new CommandContext(world.Tick, events);
            int applied = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                IWorldCommand command = commands[i];
                if (command == null)
                {
                    continue;
                }

                if (command.Apply(world, context))
                {
                    applied++;
                }
            }

            return applied;
        }

        private static void EmitCalendarEvents(WorldState world, List<SimEvent> events)
        {
            GameDateTime date = GameCalendar.FromTick(world.Tick);
            if (world.Tick > 0 && date.MinuteOfDay == 0)
            {
                events.Add(new SimEvent(
                    SimEventKind.DayStarted,
                    world.Tick,
                    EntityId.None,
                    "A new day begins. " + date.ShortText + "."));

                if (date.DayOfSeason == 1)
                {
                    events.Add(new SimEvent(
                        SimEventKind.SeasonStarted,
                        world.Tick,
                        EntityId.None,
                        date.SeasonName + " has arrived."));
                }
            }
        }

        private void UpdateNpc(WorldState world, NpcState npc, List<SimEvent> events)
        {
            if (npc.Survival.Life == LifeState.Dead) { return; }
            if (npc.Needs.Health <= 0 && npc.Survival.Life != LifeState.Downed) { HealthSystem.Update(world, npc, events); return; }
            if (npc.Survival.Life != LifeState.Downed) { UpdateNeeds(world, npc, events); }
            if (!HealthSystem.Update(world, npc, events)) { return; }
            if (npc.Survival.Life == LifeState.Recovering && !npc.Survival.Rescuer.IsNone) { return; }
            if (npc.Plan.IsActive && ShouldInterrupt(npc))
            {
                FailPlan(world, npc, "critical_need_interrupt", events, true);
            }
            if (!npc.Plan.IsActive)
            {
                if (world.Tick >= npc.Action.EndTick)
                {
                    DecideAndStart(world, npc, events);
                }
                return;
            }
            PlanStep step = npc.Plan.CurrentStep;
            if (step.Type == PlanStepType.Greet || step.Type == PlanStepType.GiveFood)
            {
                UpdateSocial(world, npc, step, events);
                return;
            }
            if (step.Type == PlanStepType.Rescue)
            {
                bool complete;
                if (!HealthSystem.Rescue(world, npc, step, _pathfinder, events, out complete)) { FailPlan(world, npc, "rescue_target_route_or_supplies_changed", events); }
                else if (complete) { CompleteStep(world, npc); }
                return;
            }
            if (step.Type == PlanStepType.Hunt)
            {
                UpdateHunt(world, npc, step, events);
                return;
            }
            if (!npc.Plan.StepStarted)
            {
                // Deposits reserve the shared work cell on arrival, so travel does
                // not lock the village store. Waiting is bounded by need interruption.
                if (step.Type == PlanStepType.Deposit && !world.Resources.TryReserve(
                    new ResourceReservation(npc.Id, ReservationKind.Stockpile, 0, step.Item, 0, step.Target)))
                {
                    npc.Action.SetIdle("Waiting for stockpile work cell", world.Tick + 1);
                    return;
                }
                string failure;
                if (!CheckPreconditions(world, npc, step, out failure))
                {
                    FailPlan(world, npc, failure, events);
                    return;
                }
                if (step.Type == PlanStepType.Move)
                {
                    if (npc.Position == step.Target)
                    {
                        CompleteStep(world, npc);
                        return;
                    }
                    PathResult path = _pathfinder.FindPath(world.Map, npc.Position, step.Target);
                    if (!path.Found)
                    {
                        FailPlan(world, npc, "path_unreachable", events);
                        return;
                    }
                    npc.Action.SetMove(npc.Plan.Goal + ": moving", step.Target, path.Path);
                }
                else
                {
                    NpcActionType type = step.Type == PlanStepType.Sleep ? NpcActionType.Sleep :
                        step.Type == PlanStepType.Eat ? NpcActionType.Eat :
                        step.Type == PlanStepType.Drink ? NpcActionType.Drink :
                        step.Type == PlanStepType.Gather ? NpcActionType.Gather :
                        step.Type == PlanStepType.Wait ? NpcActionType.Idle : NpcActionType.Work;
                    npc.Action.SetTimed(type, step.Type.ToString(), step.Target, world.Tick + Math.Max(1, step.DurationTicks));
                }
                npc.Plan.StepStarted = true;
                events.Add(new SimEvent(SimEventKind.NpcActionStarted, world.Tick, npc.Id,
                    npc.Name + " started " + step.Summary + "."));
                return;
            }
            if (step.Type == PlanStepType.Move)
            {
                GridPos next = npc.Action.PeekNextStep();
                if (!world.Map.IsWalkable(next))
                {
                    FailPlan(world, npc, "path_blocked", events);
                    return;
                }
                npc.Position = next;
                npc.Action.AdvanceStep();
                if (npc.Action.IsComplete(world.Tick))
                {
                    CompleteStep(world, npc);
                }
                return;
            }
            if (world.Tick < npc.Action.EndTick)
            {
                if (step.Type == PlanStepType.Build) { var site = world.Survival.FindFacility(step.ResourceId); if (site != null) { site.Progress = Math.Min(SurvivalRules.ConstructionTicks - 1, site.Progress + 1); } }
                return;
            }
            string reason;
            if (!CheckPreconditions(world, npc, step, out reason) || !ApplyEffects(world, npc, step, events))
            {
                FailPlan(world, npc, reason == "none" ? ((step.Type == PlanStepType.Buy || step.Type == PlanStepType.Sell) ? npc.LastDecision.ReasonCode : "effect_transfer_failed") : reason, events);
                return;
            }
            events.Add(new SimEvent(SimEventKind.NpcActionCompleted, world.Tick, npc.Id,
                npc.Name + " completed " + step.Type + "."));
            CompleteStep(world, npc);
        }

        private void UpdateSocial(WorldState world, NpcState npc, PlanStep step, List<SimEvent> events)
        {
            if (!npc.Plan.StepStarted)
            {
                npc.Action.SetTimed(NpcActionType.Work, step.Type.ToString(), step.Target, world.Tick + step.DurationTicks);
                npc.Plan.StepStarted = true;
            }
            NpcState target;
            if (!SocialSystem.Comfortable(npc) || world.Tick >= npc.Action.EndTick ||
                !world.Entities.TryGetNpc(new EntityId(step.ResourceId), out target))
            {
                npc.Social.NextInteractionTick = world.Tick + 30;
                FailPlan(world, npc, "social_target_or_needs_changed", events);
                return;
            }
            if (npc.Position.ManhattanDistance(target.Position) > 1)
            {
                PathResult path = _pathfinder.FindPath(world.Map, npc.Position, target.Position);
                if (!path.Found || path.Path.Count == 0)
                {
                    npc.Social.NextInteractionTick = world.Tick + 30;
                    FailPlan(world, npc, "social_target_unreachable", events);
                    return;
                }
                npc.Position = path.Path[0];
                return;
            }
            if (!SocialSystem.Interact(world, npc, target, step.Type == PlanStepType.Greet ? SocialKind.Greeting : SocialKind.FoodGift, events))
            {
                npc.Social.NextInteractionTick = world.Tick + 30;
                FailPlan(world, npc, "social_target_or_needs_changed", events);
                return;
            }
            CompleteStep(world, npc);
        }

        private void UpdateHunt(WorldState world, NpcState npc, PlanStep step, List<SimEvent> events)
        {
            if (npc.Survival.HuntDropId > 0)
            {
                var ground = world.Survival.FindGround(npc.Survival.HuntDropId);
                if (ground == null || ground.ReservedBy != npc.Id || world.Tick >= npc.Action.EndTick)
                { FailPlan(world, npc, "hunt_retrieval_changed", events); return; }
                npc.Action.Label = "Hunt: retrieve"; npc.Action.Target = ground.Position;
                if (npc.Position != ground.Position)
                {
                    var path = _pathfinder.FindPath(world.Map, npc.Position, ground.Position);
                    if (!path.Found || path.Path.Count == 0) { FailPlan(world, npc, "hunt_retrieval_unreachable", events); return; }
                    npc.Position = path.Path[0]; return;
                }
                if (npc.Inventory.Capacity - npc.Inventory.TotalCount < ground.Inventory.Food)
                { FailPlan(world, npc, "hunt_retrieval_bag_full", events); return; }
                ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Pickup, ground.Position, ground.Id));
                npc.Survival.HuntDropId = 0; CompleteStep(world, npc); return;
            }
            AnimalState animal = world.Ecology.Find(step.ResourceId);
            if (!world.Ecology.CanHunt(animal) || animal.ReservedBy != npc.Id || !npc.HasHuntingTool ||
                npc.Needs.Energy < 1800 || npc.Needs.Health < 3000)
            {
                FailPlan(world, npc, "hunt_prey_quota_tool_energy_health_or_capacity", events);
                return;
            }
            if (!npc.Plan.StepStarted)
            {
                npc.Action.SetTimed(NpcActionType.Work, "Hunt: pursue " + animal.Species, animal.Position,
                    world.Tick + step.DurationTicks);
                npc.Plan.StepStarted = true;
            }
            if (world.Tick >= npc.Action.EndTick)
            {
                FailPlan(world, npc, "hunt_pursuit_timeout", events);
                return;
            }
            if (!HuntingSystem.CanStrike(world, npc, animal))
            {
                PathResult path = _pathfinder.FindPath(world.Map, npc.Position, animal.Position);
                if (!path.Found || path.Path.Count == 0)
                {
                    FailPlan(world, npc, "hunt_prey_unreachable", events);
                    return;
                }
                npc.Action.Label = "Hunt: track"; npc.Action.Target = animal.Position;
                npc.Position = path.Path[0]; npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - 4);
                return;
            }
            npc.Action.Label = "Hunt: attack"; npc.Action.Target = animal.Position;
            if ((world.Tick + npc.Id.Value) % 6 != 0)
            {
                return;
            }
            if (!world.Ecology.Strike(world, npc, animal, events))
            {
                FailPlan(world, npc, "hunt_strike_rejected", events);
            }
            else if (animal.Health == 0)
            {
                npc.Action.Label = "Hunt: retrieve";
            }
        }

        private static void UpdateNeeds(WorldState world, NpcState npc, List<SimEvent> events)
        {
            int previousFood = npc.Needs.Hunger;
            int previousWater = npc.Needs.Thirst;
            int previousEnergy = npc.Needs.Fatigue;
            npc.Needs.Hunger = Clamp(npc.Needs.Hunger - 6);
            npc.Needs.Thirst = Clamp(npc.Needs.Thirst - 9);
            npc.Needs.Fatigue = Clamp(npc.Needs.Fatigue + (npc.Action.Type == NpcActionType.Sleep ? 45 : -7));
            bool deprived = npc.Needs.Hunger == 0 || npc.Needs.Thirst == 0 || npc.Needs.Fatigue == 0;
            bool comfortable = npc.Needs.Hunger >= 2500 && npc.Needs.Thirst >= 2500 && npc.Needs.Fatigue >= 2500;
            npc.Needs.Health = Clamp(npc.Needs.Health + (deprived ? -12 : comfortable ? 2 : 0));
            bool night = world.Date.MinuteOfDay < 360 || world.Date.MinuteOfDay >= 1200;
            int targetSafety = deprived ? 2500 : night && npc.Position.ManhattanDistance(npc.Home) > 10 ? 5500 : 9500;
            npc.Needs.Safety = Clamp(npc.Needs.Safety + Math.Sign(targetSafety - npc.Needs.Safety) *
                Math.Min(25, Math.Abs(targetSafety - npc.Needs.Safety)));
            if ((previousFood > 1500 && npc.Needs.Hunger <= 1500) ||
                (previousWater > 1500 && npc.Needs.Thirst <= 1500) ||
                (previousEnergy > 1500 && npc.Needs.Fatigue <= 1500))
            {
                events.Add(new SimEvent(SimEventKind.NeedCritical, world.Tick, npc.Id,
                    npc.Name + " has critical satisfaction: " + npc.Needs.Summary + "."));
            }
        }

        private static bool ShouldInterrupt(NpcState npc)
        {
            if (npc.Needs.Thirst < 1800 && npc.Plan.Goal != NpcGoalType.Drink)
            {
                return true;
            }
            if (npc.Needs.Hunger < 1500 && npc.Plan.Goal != NpcGoalType.Eat && npc.Plan.Goal != NpcGoalType.Drink)
            {
                return true;
            }
            return npc.Needs.Fatigue < 1200 && npc.Plan.Goal != NpcGoalType.Sleep &&
                npc.Plan.Goal != NpcGoalType.Eat && npc.Plan.Goal != NpcGoalType.Drink;
        }

        private void DecideAndStart(WorldState world, NpcState npc, List<SimEvent> events)
        {
            NpcDecision decision = _decisionPolicy.Decide(new NpcDecisionContext(npc, world.Map, world.Tick, world));
            npc.RngState = decision.UpdatedRngState;
            npc.LastDecision = decision.Trace;
            string failure;
            if (!_planner.TryBuild(world, npc, decision, out failure))
            {
                // Failed work proposals fall through to the next feasible utility candidate.
                // Urgent survival plans never get replaced by discretionary work.
                ProductionSystem.Release(world, npc);
                npc.Survival.WorkRetryTick = world.Tick + 30;
                npc.Plan.Fail(failure);
                events.Add(new SimEvent(SimEventKind.PlanFailed, world.Tick, npc.Id,
                    npc.Name + ": " + decision.Goal + " / " + failure + "."));
                bool found = false;
                if (decision.Goal == NpcGoalType.Farm || decision.Goal == NpcGoalType.GatherFood || decision.Goal == NpcGoalType.GatherWater)
                {
                    NpcGoalType[] fallback = { NpcGoalType.GatherFood, NpcGoalType.GatherWater };
                    for (int i = 0; i < fallback.Length; i++)
                    {
                        if (fallback[i] == decision.Goal ||
                            (fallback[i] == NpcGoalType.GatherFood && world.Resources.Stockpile.Food >= 40) ||
                            (fallback[i] == NpcGoalType.GatherWater && world.Resources.Stockpile.Water >= 40))
                        {
                            continue;
                        }
                        var alternative = new NpcDecision(fallback[i], npc.Position, 12, npc.RngState, decision.Trace);
                        if (!_planner.TryBuild(world, npc, alternative, out failure))
                        {
                            continue;
                        }
                        decision.Trace.Goal = fallback[i].ToString();
                        decision.Trace.ReasonCode = "preferred_work_unavailable_supply_fallback";
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    decision.Trace.ReasonCode = failure;
                    npc.Action.SetIdle("Waiting: " + failure, world.Tick + 12);
                    return;
                }
            }
            npc.Action.SetIdle("Plan ready", world.Tick);
            events.Add(new SimEvent(SimEventKind.PlanStarted, world.Tick, npc.Id,
                npc.Name + " chose " + npc.Plan.Goal + " (" + npc.Plan.Steps.Count + " steps): " + decision.Trace.ReasonCode + "."));
        }

        private static bool CheckPreconditions(WorldState world, NpcState npc, PlanStep step, out string reason)
        {
            reason = "none";
            if (step.Type == PlanStepType.Move)
            {
                if (!world.Map.IsWalkable(step.Target))
                {
                    reason = "target_unwalkable";
                    return false;
                }
                return true;
            }
            if (step.Type != PlanStepType.Eat && step.Type != PlanStepType.Drink && step.Type != PlanStepType.Wait && npc.Position != step.Target)
            {
                reason = "not_at_work_location";
                return false;
            }
            int free = npc.Inventory.Capacity - npc.Inventory.TotalCount;
            ResourceNodeState node = world.Resources.FindNode(step.ResourceId);
            FarmPlotState plot = world.Resources.FindPlot(step.ResourceId);
            bool valid = true;
            if (step.Type >= PlanStepType.Deliver) { valid = ProductionSystem.Check(world, npc, step); reason = valid ? "none" : "production_precondition_changed"; return valid; }
            switch (step.Type)
            {
                case PlanStepType.Buy:
                case PlanStepType.Sell:
                    valid = step.Target == world.Resources.StockpilePosition;
                    reason = "not_at_market";
                    break;
                case PlanStepType.Eat:
                    valid = npc.Inventory.Food >= 1;
                    reason = "food_missing";
                    break;
                case PlanStepType.Drink:
                    valid = npc.Inventory.Water >= 1;
                    reason = "water_missing";
                    break;
                case PlanStepType.Sleep:
                    valid = npc.Position == npc.Home;
                    reason = "home_unavailable";
                    break;
                case PlanStepType.TakeFromStock:
                    valid = step.Quantity > 0 && step.Target == world.Resources.StockpilePosition && world.Resources.Stockpile.Count(step.Item) >= step.Quantity && free >= step.Quantity &&
                        HasReservation(world, npc, ReservationKind.Stockpile, 0, step.Item, step.Quantity);
                    reason = "reserved_stock_unavailable_or_bag_full";
                    break;
                case PlanStepType.Gather:
                    valid = step.Quantity > 0 && node != null && step.Target == node.Position && node.Item == step.Item && node.ReservedBy == npc.Id && node.Amount >= step.Quantity && free >= step.Quantity && WaterSystem.SourceAvailable(world, node) &&
                        HasReservation(world, npc, ReservationKind.Node, node.Id, step.Item, step.Quantity);
                    reason = "reserved_node_unavailable_or_bag_full";
                    break;
                case PlanStepType.Deposit:
                    valid = step.Quantity > 0 && step.Target == world.Resources.StockpilePosition && npc.Inventory.Count(step.Item) >= step.Quantity &&
                        world.Resources.Stockpile.Capacity - world.Resources.Stockpile.TotalCount >= step.Quantity;
                    reason = "deposit_items_missing_or_stock_full";
                    break;
                case PlanStepType.Plant:
                    valid = plot != null && step.Target == plot.Position && plot.ReservedBy == npc.Id && plot.Crop == CropType.None && plot.Fertility >= 2000 && npc.Inventory.Count(step.Item) >= 1 && SurvivalRules.IsSeed(step.Item) && world.Date.Season != Season.Winter;
                    reason = "plant_requires_reserved_empty_fertile_plot_and_seed";
                    break;
                case PlanStepType.Water:
                    valid = plot != null && step.Target == plot.Position && plot.ReservedBy == npc.Id && plot.Crop != CropType.None && npc.Inventory.Count(step.Item) >= 1 && (step.Item == ItemKind.Water || step.Item == ItemKind.RawWater);
                    reason = "water_requires_reserved_crop_and_water";
                    break;
                case PlanStepType.Harvest:
                    valid = plot != null && step.Target == plot.Position && plot.ReservedBy == npc.Id && plot.Stage == CropStage.Mature && free >= plot.HarvestYield + CropDefinition.For(plot.Crop).SeedYield;
                    reason = "harvest_requires_reserved_mature_crop_and_space";
                    break;
            }
            if (valid)
            {
                reason = "none";
            }
            return valid;
        }

        private static bool HasReservation(WorldState world, NpcState npc, ReservationKind kind, int id, ItemKind item, int amount)
        {
            for (int i = 0; i < world.Resources.Reservations.Count; i++)
            {
                ResourceReservation reservation = world.Resources.Reservations[i];
                if (reservation.Owner == npc.Id && reservation.Kind == kind && reservation.ResourceId == id &&
                    reservation.Item == item && reservation.Quantity >= amount)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ApplyEffects(WorldState world, NpcState npc, PlanStep step, List<SimEvent> events)
        {
            if (step.Type >= PlanStepType.Deliver) { return ProductionSystem.Apply(world, npc, step); }
            FarmPlotState plot = world.Resources.FindPlot(step.ResourceId);
            switch (step.Type)
            {
                case PlanStepType.Buy:
                case PlanStepType.Sell:
                    bool traded = world.Market.Execute(world, new TradeOrder(world.Market.NextOrderId, npc.Id,
                        step.Type == PlanStepType.Buy ? TradeSide.Buy : TradeSide.Sell,
                        step.Item, step.Quantity, step.UnitPrice), events);
                    if (!traded)
                    {
                        npc.LastDecision.ReasonCode = world.Market.Ledger[world.Market.Ledger.Count - 1].Reason;
                    }
                    return traded;
                case PlanStepType.Eat:
                    if (!npc.Inventory.TryRemove(ItemKind.Food, 1))
                    {
                        return false;
                    }
                    world.Materials.Consume(ItemKind.Food, 1);
                    npc.Needs.Hunger = Clamp(npc.Needs.Hunger + 4500);
                    break;
                case PlanStepType.Drink:
                    if (!npc.Inventory.TryRemove(ItemKind.Water, 1))
                    {
                        return false;
                    }
                    world.Materials.Consume(ItemKind.Water, 1);
                    npc.Needs.Thirst = Clamp(npc.Needs.Thirst + 5500);
                    break;
                case PlanStepType.TakeFromStock:
                    if (!Transfer(world.Resources.Stockpile, npc.Inventory, step.Item, step.Quantity))
                    {
                        return false;
                    }
                    world.Resources.ReleaseStep(npc.Id, ReservationKind.Stockpile, 0, step.Item);
                    break;
                case PlanStepType.Deposit:
                    if (!Transfer(npc.Inventory, world.Resources.Stockpile, step.Item, step.Quantity))
                    {
                        return false;
                    }
                    world.Resources.ReleaseStep(npc.Id, ReservationKind.Stockpile, 0, step.Item);
                    break;
                case PlanStepType.Gather:
                    ResourceNodeState node = world.Resources.FindNode(step.ResourceId);
                    if (!npc.Inventory.TryAdd(step.Item, step.Quantity))
                    {
                        return false;
                    }
                    node.Amount -= step.Quantity;
                    if (step.Item == ItemKind.RawWater) { ProductionSystem.Wear(world, npc, ItemKind.Bucket); }
                    if (step.Item == ItemKind.Food)
                    {
                        npc.GatheredFood += step.Quantity;
                    }
                    world.Resources.ReleaseStep(npc.Id, ReservationKind.Node, node.Id, step.Item);
                    events.Add(new SimEvent(SimEventKind.ResourceGathered, world.Tick, npc.Id,
                        npc.Name + " gathered " + step.Quantity + " " + step.Item + " from node #" + node.Id + "."));
                    break;
                case PlanStepType.Plant:
                    if (!npc.Inventory.TryRemove(step.Item, 1)) { return false; }
                    var planted = CropDefinition.FromSeed(step.Item);
                    world.Materials.Consume(step.Item, 1); plot.Crop = planted.Type;
                    plot.GrowthTicks = 0; plot.Moisture = 0; plot.Damage = 0;
                    plot.Fertility = Math.Max(0, Math.Min(10000, plot.Fertility + planted.FertilityChange));
                    ProductionSystem.Wear(world, npc, ItemKind.Hoe);
                    events.Add(new SimEvent(SimEventKind.CropPlanted, world.Tick, npc.Id, npc.Name + " planted " + plot.Crop + " at " + plot.Position + "."));
                    break;
                case PlanStepType.Water:
                    if (!npc.Inventory.TryRemove(step.Item, 1)) { return false; }
                    world.Materials.Consume(step.Item, 1); plot.Moisture = Math.Min(1800, plot.Moisture + 900);
                    ProductionSystem.Wear(world, npc, ItemKind.Bucket);
                    world.Resources.ReleaseStep(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food);
                    events.Add(new SimEvent(SimEventKind.CropWatered, world.Tick, npc.Id, npc.Name + " watered plot #" + plot.Id + "."));
                    break;
                case PlanStepType.Harvest:
                    int yield = plot.HarvestYield;
                    var harvested = CropDefinition.For(plot.Crop);
                    npc.Inventory.TryAdd(ItemKind.Food, yield); npc.Inventory.TryAdd(harvested.Seed, harvested.SeedYield);
                    world.Materials.Produce(ItemKind.Food, yield); world.Materials.Produce(harvested.Seed, harvested.SeedYield);
                    npc.HarvestedFood += yield; plot.PreviousCrop = plot.Crop; plot.Crop = CropType.None;
                    plot.GrowthTicks = 0; plot.Moisture = 0; plot.Damage = 0;
                    ProductionSystem.Wear(world, npc, ItemKind.Hoe);
                    world.Resources.ReleaseStep(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food);
                    events.Add(new SimEvent(SimEventKind.CropHarvested, world.Tick, npc.Id, npc.Name + " harvested " + yield + " food at " + plot.Position + "."));
                    break;
            }
            return true;
        }

        private static bool Transfer(InventoryState source, InventoryState target, ItemKind item, int amount)
        {
            if (amount <= 0 || item < ItemKind.Food || item >= ItemKind.Count || source.Count(item) < amount || target.Capacity - target.TotalCount < amount)
            {
                return false;
            }
            return source.TryRemove(item, amount) && target.TryAdd(item, amount);
        }

        private static void CompleteStep(WorldState world, NpcState npc)
        {
            npc.Plan.StepIndex++;
            npc.Plan.StepStarted = false;
            npc.Action.SetIdle("Next step", world.Tick + 1);
            if (!npc.Plan.IsActive)
            {
                world.Resources.Release(npc.Id);
                world.Ecology.Release(npc.Id);
                ProductionSystem.Release(world, npc);
            }
        }

        private static void FailPlan(WorldState world, NpcState npc, string reason, List<SimEvent> events, bool interrupted = false)
        {
            world.Resources.Release(npc.Id);
            world.Ecology.Release(npc.Id);
            ProductionSystem.Release(world, npc);
            npc.Survival.WorkRetryTick = world.Tick + 30;
            npc.Plan.Fail(reason);
            npc.LastDecision.ReasonCode = reason;
            npc.Action.SetIdle("Replanning: " + reason, world.Tick + (interrupted ? 0 : 2));
            events.Add(new SimEvent(interrupted ? SimEventKind.PlanInterrupted : SimEventKind.PlanFailed,
                world.Tick, npc.Id, npc.Name + ": " + reason + "."));
            if (reason == "path_blocked" || reason == "path_unreachable" || reason == "target_unwalkable")
            {
                events.Add(new SimEvent(SimEventKind.NpcPathFailed, world.Tick, npc.Id, npc.Name + ": " + reason + "."));
            }
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(NeedsState.MaxValue, value));
    }
}
