using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Ai
{
    public enum PlanStepType
    {
        Move, TakeFromStock, Eat, Drink, Sleep, Gather, Deposit, Plant, Water, Harvest, Wait, Hunt, Buy, Sell, Greet, GiveFood,
        Deliver, TakeFacility, Build, Craft, Maintain, Equip, Pickup, Rescue, DrinkRaw, Treat, WarmUp, DeliverFuel, MeltSnow
    }

    public sealed class PlanStep
    {
        public PlanStep(PlanStepType type, GridPos target, int resourceId = 0, ItemKind item = ItemKind.Food, int quantity = 1, int durationTicks = 1, int unitPrice = 0)
        {
            Type = type;
            Target = target;
            ResourceId = resourceId;
            Item = item == ItemKind.Food && type == PlanStepType.Plant ? ItemKind.Seed : item == ItemKind.Food && type == PlanStepType.Water ? ItemKind.Water : item;
            Quantity = quantity;
            DurationTicks = durationTicks;
            UnitPrice = unitPrice;
        }

        public PlanStepType Type { get; }
        public GridPos Target { get; }
        public int ResourceId { get; }
        public ItemKind Item { get; }
        public int Quantity { get; }
        public int DurationTicks { get; }
        public int UnitPrice { get; }
        public string Preconditions => Type == PlanStepType.Move ? "reachable target" :
            Type == PlanStepType.Eat ? "carry food" : Type == PlanStepType.Drink ? "carry water" :
            Type == PlanStepType.Hunt ? "perceived reserved prey; weapon and ammunition; energy; attack range; recovery route" :
            Type == PlanStepType.Buy ? "at market; current quote; coins; market stock; bag space" :
            Type == PlanStepType.Sell ? "at market; current quote; bag stock; treasury; market space" :
            Type == PlanStepType.Sleep ? "at home" : Type == PlanStepType.Gather ? "at reserved node; yield available; bag space" :
            Type == PlanStepType.TakeFromStock ? "at stock; reserved stock available; bag space" :
            Type == PlanStepType.Deposit ? "at stock; bag items; stock space" :
            Type == PlanStepType.Plant ? "at reserved empty plot; carry seed; fertile soil" :
            Type == PlanStepType.Water ? "at reserved planted plot; carry water" :
            Type == PlanStepType.Harvest ? "at reserved mature plot; bag space" : "wait elapsed";
        public string Effects => Type == PlanStepType.Eat ? "consume 1 food; food satisfaction +4500" :
            Type == PlanStepType.Drink ? "consume 1 water; water satisfaction +5500" :
            Type == PlanStepType.Hunt ? "prepare and release attacks; spend ammunition; recover finite carcass meat and hide" :
            Type == PlanStepType.Buy ? "atomic coins for goods; ledger records result" :
            Type == PlanStepType.Sell ? "atomic goods for coins; ledger records result" :
            Type == PlanStepType.Sleep ? "energy recovers each minute" :
            Type == PlanStepType.Gather ? "node decreases; inventory increases" :
            Type == PlanStepType.TakeFromStock ? "stock decreases; inventory increases" :
            Type == PlanStepType.Deposit ? "inventory decreases; stock increases" :
            Type == PlanStepType.Plant ? "consume seed; start turnip; fertility -400" :
            Type == PlanStepType.Water ? "consume water; moisture +900" :
            Type == PlanStepType.Harvest ? "crop removed; gain food + seeds" :
            Type == PlanStepType.Move ? "position becomes target" : "planning retries";
        public string Summary => Type + " at " + Target +
            ((Type == PlanStepType.Buy || Type == PlanStepType.Sell) ? " " + Quantity + " " + Item + " @" + UnitPrice :
            ResourceId > 0 ? " #" + ResourceId : "");
    }

    public sealed class NpcPlanState
    {
        private readonly List<PlanStep> _steps = new List<PlanStep>();
        private readonly IReadOnlyList<PlanStep> _stepView;
        public NpcPlanState()
        {
            _stepView = _steps.AsReadOnly();
        }
        public NpcGoalType Goal { get; internal set; }
        public IReadOnlyList<PlanStep> Steps => _stepView;
        public int StepIndex { get; internal set; }
        public bool StepStarted { get; internal set; }
        public int FailedStepIndex { get; internal set; } = -1;
        public bool IsFailed => FailedStepIndex >= 0;
        public PlanStep CurrentStep => IsActive ? _steps[StepIndex] : null;
        public bool IsActive => StepIndex < _steps.Count;
        public string FailureReason { get; internal set; } = "none";
        public int ReplanCount { get; internal set; }
        public string Summary => Goal + " " + (IsActive ? (StepIndex + 1) + "/" + _steps.Count + " " + CurrentStep.Summary : IsFailed ? "failed at step " + (FailedStepIndex + 1) : "complete") + " / " + FailureReason;

        internal void Start(NpcGoalType goal, List<PlanStep> steps)
        {
            Goal = goal;
            _steps.Clear();
            _steps.AddRange(steps);
            StepIndex = 0;
            StepStarted = false;
            FailedStepIndex = -1;
            FailureReason = "none";
        }

        internal void Fail(string reason)
        {
            FailedStepIndex = StepIndex;
            FailureReason = reason;
            ReplanCount++;
            StepIndex = _steps.Count;
        }

        internal void Complete()
        {
            StepIndex = _steps.Count;
            StepStarted = false;
            FailedStepIndex = -1;
            FailureReason = "none";
        }
    }
}
