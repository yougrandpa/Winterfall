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
        public TickResult(long tick, ulong stateHash, int appliedCommands, List<SimEvent> events,
            IReadOnlyList<CombatTraceSegment> traces = null, IReadOnlyList<CombatVisualEvent> combatEvents = null)
        {
            Tick = tick;
            StateHash = stateHash;
            AppliedCommands = appliedCommands;
            Events = events ?? new List<SimEvent>();
            CombatTraces = Array.AsReadOnly(traces == null ? Array.Empty<CombatTraceSegment>() : new List<CombatTraceSegment>(traces).ToArray());
            CombatEvents = Array.AsReadOnly(combatEvents == null ? Array.Empty<CombatVisualEvent>() : new List<CombatVisualEvent>(combatEvents).ToArray());
        }

        public long Tick { get; }
        public ulong StateHash { get; }
        public int AppliedCommands { get; }
        public List<SimEvent> Events { get; }
        public IReadOnlyList<CombatTraceSegment> CombatTraces { get; }
        public IReadOnlyList<CombatVisualEvent> CombatEvents { get; }
    }

    public sealed class SimulationEngine
    {
        private readonly INpcDecisionPolicy _decisionPolicy;
        private readonly GridPathfinder _pathfinder;
        private readonly SurvivalPlanner _planner;
        private readonly List<KeyValuePair<NpcState, PlanStep>> _pendingCompletions = new List<KeyValuePair<NpcState, PlanStep>>();

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
            bool wasLocked = !world.Campaign.CanAdvance;
            int appliedCommands = ApplyCommands(world, commands, events);
            if (wasLocked)
            {
                return new TickResult(world.Tick, StateHasher.Compute(world), appliedCommands, events);
            }
            _pendingCompletions.Clear();
            CombatSystem.RecordPositions(world);
            EmitCalendarEvents(world, events);
            EnvironmentSystem.Update(world);
            ThermalSystem.UpdateWorld(world);
            world.Ecology.UpdateResources(world);
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

            CombatSystem.Update(world, events);
            for (int i = 0; i < npcs.Count; i++)
            {
                if (npcs[i].Needs.Health <= 0 && npcs[i].Survival.Life != LifeState.Dead && npcs[i].Survival.Life != LifeState.Downed)
                {
                    HealthSystem.Update(world, npcs[i], events);
                }
            }
            world.Ecology.ResolveDeaths(world, events);
            foreach (KeyValuePair<NpcState, PlanStep> completion in _pendingCompletions)
            {
                NpcState npc = completion.Key;
                if (npc.Survival.Life != LifeState.Active || !npc.Plan.IsActive || npc.Plan.CurrentStep != completion.Value) { continue; }
                if (completion.Value.Type == PlanStepType.Rescue)
                {
                    if (!HealthSystem.CommitRescue(world, npc, completion.Value, events, out bool complete))
                    {
                        FailPlan(world, npc, "rescue_target_route_or_supplies_changed", events);
                    }
                    else if (complete) { CompleteStep(world, npc); }
                    continue;
                }
                if (completion.Value.Type == PlanStepType.Hunt)
                {
                    CarcassState carcass = world.Ecology.FindCarcass(npc.Survival.HuntDropId);
                    if (carcass == null || carcass.ReservedBy != npc.Id ||
                        !world.Ecology.HarvestCarcass(world, npc, carcass, events))
                    {
                        FailPlan(world, npc, "hunt_retrieval_changed", events);
                    }
                    else
                    {
                        CombatSystem.RecoverArrows(world, npc, carcass.Position);
                        npc.Survival.HuntDropId = 0;
                        CompleteStep(world, npc);
                    }
                    continue;
                }
                string reason;
                if (!CheckPreconditions(world, npc, completion.Value, out reason) || !ApplyEffects(world, npc, completion.Value, events))
                {
                    if (completion.Value.Type == PlanStepType.Greet || completion.Value.Type == PlanStepType.GiveFood)
                    {
                        npc.Social.NextInteractionTick = world.Tick + 30;
                    }
                    FailPlan(world, npc, reason == "none" ? "effect_transfer_failed" : reason, events);
                    continue;
                }
                events.Add(new SimEvent(SimEventKind.NpcActionCompleted, world.Tick, npc.Id,
                    npc.Name + " completed " + completion.Value.Type + "."));
                CompleteStep(world, npc);
            }
            world.Survival.MutableGround.RemoveAll(g => g.Empty && g.ReservedBy.IsNone);
            world.Tick++;
            if (world.Campaign.Evaluate(world))
            {
                events.Add(new SimEvent(SimEventKind.CampaignEnded, world.Tick, EntityId.None,
                    "Winter survival ended: " + world.Campaign.Result.Grade + "; survivors " + world.Campaign.Result.Survivors + "."));
            }
            ulong stateHash = StateHasher.Compute(world);
            return new TickResult(world.Tick, stateHash, appliedCommands, events,
                world.Combat.TraceSegments, world.Combat.CombatEvents);
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

                if (!world.Campaign.CanAdvance && !(command is ContinueObservationCommand))
                {
                    events.Add(new SimEvent(SimEventKind.CommandRejected, world.Tick, EntityId.None, "The settlement is locked."));
                    continue;
                }
                if (command.Apply(world, context))
                {
                    if (command is SpawnNpcCommand && world.Campaign.Result == null) { world.Campaign.Assisted = true; }
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
            ThermalSystem.UpdateNpc(world, npc);
            if (npc.Needs.Health <= 0 && npc.Survival.Life != LifeState.Downed) { HealthSystem.Update(world, npc, events); return; }
            if (npc.Survival.Life != LifeState.Downed) { UpdateNeeds(world, npc, events); }
            if (!HealthSystem.Update(world, npc, events)) { return; }
            if (npc.Survival.Life == LifeState.Recovering && !npc.Survival.Rescuer.IsNone) { return; }
            if (npc.Plan.IsActive && (ShouldInterrupt(npc) ||
                ThermalSystem.ShouldSeekWarmth(world, npc) && npc.Plan.Goal != NpcGoalType.WarmUp &&
                npc.Plan.Goal != NpcGoalType.Eat && npc.Plan.Goal != NpcGoalType.Drink &&
                !(npc.Plan.Goal == NpcGoalType.Produce && ThermalSystem.IsAtShelter(world, npc) &&
                  (npc.Inventory.Count(ItemKind.Wood) > 0 || npc.Inventory.Count(ItemKind.WinterCoat) > 0))))
            {
                FailPlan(world, npc, npc.Survival.IsCold ? "critical_cold_interrupt" : "critical_need_interrupt", events, true);
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
                if (!HealthSystem.PrepareRescue(world, npc, step, _pathfinder, out bool ready))
                {
                    FailPlan(world, npc, "rescue_target_route_or_supplies_changed", events);
                }
                else if (ready) { _pendingCompletions.Add(new KeyValuePair<NpcState, PlanStep>(npc, step)); }
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
                    NpcActionType type = step.Type == PlanStepType.Sleep || step.Type == PlanStepType.WarmUp ? NpcActionType.Sleep :
                        step.Type == PlanStepType.Eat ? NpcActionType.Eat :
                        step.Type == PlanStepType.Drink ? NpcActionType.Drink :
                        step.Type == PlanStepType.Gather ? NpcActionType.Gather :
                        step.Type == PlanStepType.Wait ? NpcActionType.Idle : NpcActionType.Work;
                    int duration = Math.Max(1, step.DurationTicks);
                    if (npc.Survival.IsCold && type == NpcActionType.Work) { duration += Math.Max(1, duration / 2); }
                    npc.Action.SetTimed(type, step.Type.ToString(), step.Target, world.Tick + duration);
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
            _pendingCompletions.Add(new KeyValuePair<NpcState, PlanStep>(npc, step));
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
            _pendingCompletions.Add(new KeyValuePair<NpcState, PlanStep>(npc, step));
        }

        private void UpdateHunt(WorldState world, NpcState npc, PlanStep step, List<SimEvent> events)
        {
            if (!npc.Plan.StepStarted)
            {
                npc.Action.SetTimed(NpcActionType.Work, "Hunt: track", step.Target, world.Tick + step.DurationTicks);
                npc.Plan.StepStarted = true;
            }
            if (world.Tick >= npc.Action.EndTick)
            {
                FailPlan(world, npc, "hunt_pursuit_timeout", events);
                return;
            }
            CarcassState carcass = world.Ecology.FindCarcass(step.ResourceId);
            if (carcass != null)
            {
                if (carcass.Region != -1 || !carcass.ReservedBy.IsNone && carcass.ReservedBy != npc.Id ||
                    npc.Inventory.TotalCount == npc.Inventory.Capacity)
                {
                    FailPlan(world, npc, "hunt_retrieval_changed_or_bag_full", events);
                    return;
                }
                carcass.ReservedBy = npc.Id;
                npc.Survival.HuntDropId = carcass.Id.Value;
                npc.Action.Label = "Hunt: retrieve";
                npc.Action.Target = carcass.Position;
                if (npc.Position.ManhattanDistance(carcass.Position) > 1)
                {
                    PathResult retrieval = _pathfinder.FindPath(world.Map, npc.Position, carcass.Position);
                    if (!retrieval.Found || retrieval.Path.Count == 0)
                    {
                        FailPlan(world, npc, "hunt_retrieval_unreachable", events);
                        return;
                    }
                    npc.Position = retrieval.Path[0];
                    return;
                }
                _pendingCompletions.Add(new KeyValuePair<NpcState, PlanStep>(npc, step));
                return;
            }
            AnimalState animal = world.Ecology.Find(step.ResourceId);
            if (!world.Ecology.CanHunt(animal) || animal.ReservedBy != npc.Id || !npc.HasHuntingTool ||
                npc.Survival.Durability(HuntingSystem.Weapon(npc)) <= 0 || npc.Needs.Energy < 1800 || npc.Needs.Health < 3000)
            {
                FailPlan(world, npc, "hunt_prey_tool_energy_or_health_changed", events);
                return;
            }
            bool perceived = HuntingSystem.Perceived(world, npc, animal);
            if (perceived) { npc.Action.Target = animal.Position; }
            if (!perceived || !HuntingSystem.CanStrike(world, npc, animal))
            {
                if (npc.Position == npc.Action.Target)
                {
                    FailPlan(world, npc, "hunt_prey_lost", events);
                    return;
                }
                PathResult path = _pathfinder.FindPath(world.Map, npc.Position, npc.Action.Target);
                if (!path.Found || path.Path.Count == 0)
                {
                    FailPlan(world, npc, "hunt_prey_unreachable", events);
                    return;
                }
                npc.Action.Label = "Hunt: track";
                npc.Position = path.Path[0];
                npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - 4);
                return;
            }
            npc.Action.Label = "Hunt: attack";
            CombatSystem.TryStartHunt(world, npc, animal, events);
        }

        private static void UpdateNeeds(WorldState world, NpcState npc, List<SimEvent> events)
        {
            int previousFood = npc.Needs.Hunger;
            int previousWater = npc.Needs.Thirst;
            int previousEnergy = npc.Needs.Fatigue;
            int coldCost = npc.Survival.BodyTemperature < 3600 ? 4 : 0;
            npc.Needs.Hunger = Clamp(npc.Needs.Hunger - 6 - coldCost);
            npc.Needs.Thirst = Clamp(npc.Needs.Thirst - 9);
            npc.Needs.Fatigue = Clamp(npc.Needs.Fatigue + (npc.Action.Type == NpcActionType.Sleep ? 45 : -7 - coldCost));
            bool deprived = npc.Needs.Hunger == 0 || npc.Needs.Thirst == 0 || npc.Needs.Fatigue == 0;
            bool comfortable = npc.Needs.Hunger >= 2500 && npc.Needs.Thirst >= 2500 && npc.Needs.Fatigue >= 2500 &&
                ThermalSystem.IsSafe(world, npc);
            int previousHealth = npc.Needs.Health;
            npc.Needs.Health = Clamp(previousHealth + (deprived ? -12 : comfortable ? 2 : 0));
            if (previousHealth > 0 && npc.Needs.Health == 0)
            {
                npc.Survival.DeathCause = npc.Needs.Thirst == 0 ? NpcDeathCause.Thirst :
                    npc.Needs.Hunger == 0 ? NpcDeathCause.Starvation : NpcDeathCause.Exhaustion;
            }
            bool night = !world.Environment.IsDaytime;
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
            return npc.Needs.Fatigue < 1200 && npc.Plan.Goal != NpcGoalType.Sleep && npc.Plan.Goal != NpcGoalType.WarmUp &&
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
            if (step.Type == PlanStepType.Greet || step.Type == PlanStepType.GiveFood)
            {
                bool socialValid = SocialSystem.Comfortable(npc) && world.Entities.TryGetNpc(new EntityId(step.ResourceId), out NpcState partner) &&
                    partner.Survival.Life == LifeState.Active && partner.Needs.Health > 0 && npc.Position.ManhattanDistance(partner.Position) <= 1;
                reason = socialValid ? "none" : "social_target_or_needs_changed";
                return socialValid;
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
            if (ProductionSystem.IsProductionStep(step.Type)) { valid = ProductionSystem.Check(world, npc, step); reason = valid ? "none" : "production_precondition_changed"; return valid; }
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
                case PlanStepType.WarmUp:
                    valid = ThermalSystem.IsAtShelter(world, npc);
                    reason = "shelter_unavailable";
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
            if (ProductionSystem.IsProductionStep(step.Type)) { return ProductionSystem.Apply(world, npc, step); }
            FarmPlotState plot = world.Resources.FindPlot(step.ResourceId);
            switch (step.Type)
            {
                case PlanStepType.Greet:
                case PlanStepType.GiveFood:
                    return world.Entities.TryGetNpc(new EntityId(step.ResourceId), out NpcState partner) &&
                        SocialSystem.Interact(world, npc, partner, step.Type == PlanStepType.Greet ? SocialKind.Greeting : SocialKind.FoodGift, events);
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
