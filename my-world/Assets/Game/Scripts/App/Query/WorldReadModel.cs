using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class NpcSnapshot
    {
        public NpcSnapshot(EntityId id, string name, string role, GridPos cell, string actionLabel, NpcActionType actionType = NpcActionType.Idle, int health = 10000, LifeState life = LifeState.Active)
            : this(id, name, role, cell, actionLabel, actionType, health, life, 3700, 0, 0, 0, 0, 0, null)
        {
        }

        public NpcSnapshot(EntityId id, string name, string role, GridPos cell, string actionLabel,
            NpcActionType actionType, int health, LifeState life, int bodyTemperature, int wetness,
            int coldTicks, int shelterId, int coatDurability, int arrows, AttackSnapshot attack = null, GridPos? home = null)
        {
            Home = home ?? cell;
            Id = id;
            Name = name;
            Role = role;
            Cell = cell;
            ActionLabel = actionLabel;
            ActionType = actionType;
            Health = health; Life = life;
            BodyTemperature = bodyTemperature; Wetness = wetness; ColdTicks = coldTicks;
            ShelterId = shelterId; CoatDurability = coatDurability; Arrows = arrows; Attack = attack;
        }

        public int BodyTemperature { get; }
        public int Wetness { get; }
        public int ColdTicks { get; }
        public bool IsCold => BodyTemperature < 3500;
        public int ShelterId { get; }
        public int CoatDurability { get; }
        public bool HasWinterCoat => CoatDurability > 0;
        public int Arrows { get; }
        public AttackSnapshot Attack { get; }
        public EntityId Id { get; }
        public string Name { get; }
        public string Role { get; }
        public GridPos Cell { get; }
        public GridPos Home { get; }
        public string ActionLabel { get; }
        public NpcActionType ActionType { get; }
        public LifeState Life { get; }
        public int Health { get; }
    }

    public sealed class PlanStepSnapshot
    {
        internal PlanStepSnapshot(PlanStep step)
        {
            ResourceId = step.ResourceId; Type = step.Type; Target = step.Target; Item = step.Item;
            Quantity = step.Quantity; UnitPrice = step.UnitPrice;
        }
        public int ResourceId { get; }
        public PlanStepType Type { get; }
        public GridPos Target { get; }
        public ItemKind Item { get; }
        public int Quantity { get; }
        public int UnitPrice { get; }
    }

    public sealed class NeedsSample
    {
        internal NeedsSample(long tick, NeedsState needs)
        {
            Tick = tick;
            Food = needs.Food;
            Water = needs.Water;
            Energy = needs.Energy;
            Health = needs.Health;
            Safety = needs.Safety;
        }

        public long Tick { get; }
        public int Food { get; }
        public int Water { get; }
        public int Energy { get; }
        public int Health { get; }
        public int Safety { get; }
    }

    public sealed class NpcDetails
    {
        internal NpcDetails(NpcState npc, long tick, IReadOnlyList<NeedsSample> history, AttackSnapshot attack = null)
        {
            BodyTemperature = npc.Survival.BodyTemperature; Wetness = npc.Survival.Wetness;
            ColdTicks = npc.Survival.ColdTicks; ShelterId = npc.Survival.ShelterId;
            CoatDurability = npc.Survival.CoatDurability; Arrows = npc.Inventory.Count(ItemKind.Arrow); Attack = attack;
            var relations = new List<TrustSnapshot>();
            foreach (var relation in npc.Social.Relations) { relations.Add(new TrustSnapshot(relation.Other, relation.Trust)); }
            Relations = relations.AsReadOnly();
            Memories = Array.AsReadOnly(new List<MyAiWorld.Sim.Agents.SocialEvent>(npc.Social.Memories).ToArray());
            Life = npc.Survival.Life; DiseaseTicks = npc.Survival.DiseaseTicks; RescueRemaining = Math.Max(0, npc.Survival.RescueDeadline - tick);
            Rescuer = npc.Survival.Rescuer; BagTotal = npc.Inventory.TotalCount;
            var items = new int[(int)ItemKind.Count]; for (int i = 0; i < items.Length; i++) { items[i] = npc.Inventory.Count((ItemKind)i); }
            Items = Array.AsReadOnly(items);
            var tools = new int[4]; for (int i = 0; i < 4; i++) { tools[i] = npc.Survival.Durability((ItemKind)((int)ItemKind.Bow + i)); }
            Tools = Array.AsReadOnly(tools);
            Id = npc.Id;
            Name = npc.Name;
            Role = npc.Role;
            Age = npc.Identity.Age;
            ActionType = npc.Action.Type;
            GoalType = npc.Plan.Goal;
            BagFood = npc.Inventory.Food;
            BagWater = npc.Inventory.Water;
            BagSeeds = npc.Inventory.Seeds;
            BagCapacity = npc.Inventory.Capacity;
            Coins = npc.Coins;
            HasHuntingTool = npc.HasHuntingTool;
            HuntingSkill = npc.HuntingSkill;
            HarvestedFood = npc.HarvestedFood;
            GatheredFood = npc.GatheredFood;
            HuntKills = npc.HuntKills;
            Purchases = npc.Purchases;
            Sales = npc.Sales;
            StepIndex = npc.Plan.StepIndex;
            FailedStepIndex = npc.Plan.FailedStepIndex;
            var copiedSteps = new List<PlanStepSnapshot>();
            foreach (var sourceStep in npc.Plan.Steps) { copiedSteps.Add(new PlanStepSnapshot(sourceStep)); }
            Steps = copiedSteps.AsReadOnly();
            Cell = npc.Position;
            Home = npc.Home;
            ActionLabel = npc.Action.Label;
            ActionDetail = npc.Action.Type == NpcActionType.Move
                ? "To " + npc.Action.Target + "  step " + npc.Action.PathIndex + "/" + npc.Action.Path.Count
                : "Remaining " + Math.Max(0, npc.Action.EndTick - tick) + " min";
            Food = npc.Needs.Food;
            Water = npc.Needs.Water;
            Energy = npc.Needs.Energy;
            Health = npc.Needs.Health;
            Safety = npc.Needs.Safety;
            Needs = "Food " + Food / 100 + "%   Water " + Water / 100 + "%   Rest " + Energy / 100 +
                    "%\nHealth " + Health / 100 + "%   Safety " + Safety / 100 + "%";
            Inventory = "Food " + npc.Inventory.Food + "  Water " + npc.Inventory.Water +
                        "  Seeds " + npc.Inventory.Seeds + "\nBag " + npc.Inventory.TotalCount + "/" + npc.Inventory.Capacity;
            Economy = "Coins " + npc.Coins + "   Buys " + npc.Purchases + "   Sales " + npc.Sales +
                "\nHunting tool " + (npc.HasHuntingTool ? "bow" : "none") + "   Skill " + npc.HuntingSkill +
                "\nProduced: farm " + npc.HarvestedFood + " / gather " + npc.GatheredFood + " / kills " + npc.HuntKills;
            Personality = npc.Personality.Summary;
            Decision = npc.LastDecision != null ? npc.LastDecision.Summary : "none";
            DecisionReason = npc.LastDecision != null ? npc.LastDecision.ReasonCode : "none";
            Goal = npc.Plan.Goal.ToString();
            FailureReason = npc.Plan.FailureReason;
            IsPlanFailed = npc.Plan.IsFailed;
            ReplanCount = npc.Plan.ReplanCount;
            PathSteps = npc.Action.Path.Count;
            RngState = npc.RngState.ToString("x16");
            NeedsHistory = history;
            var plan = new StringBuilder();
            var steps = npc.Plan.Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                string status;
                if (npc.Plan.IsFailed)
                {
                    status = i < npc.Plan.FailedStepIndex ? "done " : i == npc.Plan.FailedStepIndex ? "failed " : "cancelled ";
                }
                else
                {
                    status = i < npc.Plan.StepIndex ? "done " : i == npc.Plan.StepIndex ? "> " : "  ";
                }

                plan.Append(status).Append(i + 1).Append(". ").Append(steps[i].Summary).Append('\n');
            }

            Plan = plan.Length > 0 ? plan.ToString().TrimEnd() : "No active steps";
            var step = npc.Plan.CurrentStep;
            PlanConditions = step != null ? step.Preconditions + "\nEffect: " + step.Effects : "";
        }

        public int BodyTemperature { get; }
        public int Wetness { get; }
        public int ColdTicks { get; }
        public bool IsCold => BodyTemperature < 3500;
        public int ShelterId { get; }
        public int CoatDurability { get; }
        public bool HasWinterCoat => CoatDurability > 0;
        public int Arrows { get; }
        public AttackSnapshot Attack { get; }
        public LifeState Life { get; }
        public int DiseaseTicks { get; }
        public long RescueRemaining { get; }
        public EntityId Rescuer { get; }
        public int BagTotal { get; }
        public IReadOnlyList<int> Items { get; }
        public IReadOnlyList<int> Tools { get; }
        public EntityId Id { get; }
        public string Name { get; }
        public string Role { get; }
        public int Age { get; }
        public NpcActionType ActionType { get; }
        public NpcGoalType GoalType { get; }
        public int BagFood { get; }
        public int BagWater { get; }
        public int BagSeeds { get; }
        public int BagCapacity { get; }
        public long Coins { get; }
        public bool HasHuntingTool { get; }
        public int HuntingSkill { get; }
        public int HarvestedFood { get; }
        public int GatheredFood { get; }
        public int HuntKills { get; }
        public int Purchases { get; }
        public int Sales { get; }
        public int StepIndex { get; }
        public int FailedStepIndex { get; }
        public IReadOnlyList<TrustSnapshot> Relations { get; }
        public IReadOnlyList<MyAiWorld.Sim.Agents.SocialEvent> Memories { get; }
        public IReadOnlyList<PlanStepSnapshot> Steps { get; }
        public GridPos Cell { get; }
        public GridPos Home { get; }
        public string ActionLabel { get; }
        public string ActionDetail { get; }
        public string Needs { get; }
        public int Food { get; }
        public int Water { get; }
        public int Energy { get; }
        public int Health { get; }
        public int Safety { get; }
        public string Inventory { get; }
        public string Economy { get; }
        public string Personality { get; }
        public string Decision { get; }
        public string DecisionReason { get; }
        public string Goal { get; }
        public string Plan { get; }
        public string PlanConditions { get; }
        public string FailureReason { get; }
        public bool IsPlanFailed { get; }
        public int ReplanCount { get; }
        public int PathSteps { get; }
        public string RngState { get; }
        public IReadOnlyList<NeedsSample> NeedsHistory { get; }
    }

    public sealed class ResourceNodeSnapshot
    {
        internal ResourceNodeSnapshot(ResourceNodeState node)
        {
            Id = node.Id;
            Cell = node.Position;
            Item = node.Item;
            Amount = node.Amount;
            Capacity = node.Capacity;
            ReservedBy = node.ReservedBy;
        }

        public int Id { get; }
        public GridPos Cell { get; }
        public ItemKind Item { get; }
        public int Amount { get; }
        public int Capacity { get; }
        public EntityId ReservedBy { get; }
    }

    public sealed class FarmPlotSnapshot
    {
        internal FarmPlotSnapshot(FarmPlotState plot)
        {
            Id = plot.Id;
            Cell = plot.Position;
            Stage = plot.Stage; Crop = plot.Crop; Damage = plot.Damage; PreviousCrop = plot.PreviousCrop;
            GrowthTicks = plot.GrowthTicks;
            GrowthRequired = plot.GrowthRequired;
            Moisture = plot.Moisture;
            Fertility = plot.Fertility;
            ReservedBy = plot.ReservedBy;
        }

        public int Id { get; }
        public GridPos Cell { get; }
        public CropStage Stage { get; }
        public CropType Crop { get; }
        public CropType PreviousCrop { get; }
        public int Damage { get; }
        public int GrowthTicks { get; }
        public int GrowthRequired { get; }
        public int Moisture { get; }
        public int Fertility { get; }
        public EntityId ReservedBy { get; }
    }

    public sealed class EventSnapshot
    {
        internal EventSnapshot(SimEvent source)
        {
            Sequence = source.Sequence;
            Kind = source.Kind;
            Tick = source.Tick;
            Source = source.Source;
            Message = source.Message;
        }

        public long Sequence { get; }
        public SimEventKind Kind { get; }
        public long Tick { get; }
        public EntityId Source { get; }
        public string Message { get; }
    }

    public sealed class TrustSnapshot
    {
        public TrustSnapshot(EntityId other, int trust) { Other = other; Trust = trust; }
        public EntityId Other { get; }
        public int Trust { get; }
    }

    public sealed class WorldReadModel
    {
        private const int MaxRecentEvents = 80;
        public const int MaxNeedsHistory = 24;
        public const int NeedsHistoryInterval = 30;
        private readonly List<NpcSnapshot> _npcs = new List<NpcSnapshot>();
        private readonly Dictionary<EntityId, NpcDetails> _details = new Dictionary<EntityId, NpcDetails>();
        private readonly Dictionary<EntityId, List<NeedsSample>> _history = new Dictionary<EntityId, List<NeedsSample>>();
        private readonly List<EventSnapshot> _recentEvents = new List<EventSnapshot>();
        private readonly List<ResourceNodeSnapshot> _resourceNodes = new List<ResourceNodeSnapshot>();
        private readonly List<FarmPlotSnapshot> _farmPlots = new List<FarmPlotSnapshot>();
        private readonly HashSet<EntityId> _nonLocalAnimals = new HashSet<EntityId>();

        public WorldReadModel(WorldConfigData config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Width = config.Width;
            Height = config.Height;
            Seed = config.Seed;
            Npcs = _npcs.AsReadOnly();
            RecentEvents = _recentEvents.AsReadOnly();
            ResourceNodes = _resourceNodes.AsReadOnly();
            FarmPlots = _farmPlots.AsReadOnly();
        }

        public WorldConfigData Config { get; }
        public int Width { get; }
        public int Height { get; }
        public int Seed { get; }
        public IReadOnlyList<TileType> MapTiles { get; private set; }
        public IReadOnlyList<NpcSnapshot> Npcs { get; }
        public IReadOnlyList<EventSnapshot> RecentEvents { get; }
        public IReadOnlyList<ResourceNodeSnapshot> ResourceNodes { get; }
        public IReadOnlyList<FarmPlotSnapshot> FarmPlots { get; }
        public GridPos StockpileCell { get; private set; }
        public string Stockpile { get; private set; }
        public int StockFood { get; private set; }
        public int StockWater { get; private set; }
        public int StockSeeds { get; private set; }
        public int TotalNodeFood { get; private set; }
        public int TotalNodeWater { get; private set; }
        public int MatureCropCount { get; private set; }
        public int DryCropCount { get; private set; }
        public int GrowingCropCount { get; private set; }
        public int EmptyPlotCount { get; private set; }
        public IReadOnlyList<MyAiWorld.Sim.Agents.SocialEvent> SocialTimeline { get; private set; }
        public IReadOnlyList<MyAiWorld.Sim.Agents.SocialEvent> GetTimeline(EntityId? npc = null)
        {
            var result = new List<MyAiWorld.Sim.Agents.SocialEvent>();
            var selected = npc.HasValue ? GetDetails(npc.Value) : null;
            var source = selected != null ? selected.Memories : SocialTimeline;
            foreach (var evt in source)
            {
                if (!npc.HasValue || evt.Actor == npc.Value || evt.Target == npc.Value) { result.Add(evt); }
            }
            return result.AsReadOnly();
        }
        public EcologyEconomySnapshot EcologyEconomy { get; private set; }
        public CombatReadModel Combat { get; private set; }
        // Missing locally does not imply death: regional residents have no renderable cell.
        public bool IsAnimalNonLocal(EntityId id) => _nonLocalAnimals.Contains(id);
        public int ReservationCount { get; private set; }
        public int CriticalNpcCount { get; private set; }
        public GameDateTime Date { get; private set; }
        public long Tick { get; private set; }
        public ulong StateHash { get; private set; }
        public int PathFailureCount { get; private set; }
        public SurvivalReadModel Survival { get; private set; }
        public CampaignSnapshot Campaign { get; private set; }
        public EnvironmentSnapshot Environment { get; private set; }
        public int NpcCount => _npcs.Count;

        public void Initialize(WorldState world, List<SimEvent> initialEvents)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            MapTiles = new ReadOnlyCollection<TileType>(world.Map.CopyTiles());
            Tick = world.Tick;
            Date = world.Date;
            StateHash = StateHasher.Compute(world);
            PathFailureCount = 0;
            _history.Clear();
            _recentEvents.Clear();
            AppendEvents(initialEvents);
            Rebuild(world);
        }

        public void Refresh(WorldState world, TickResult result, int pathFailureCount)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            Tick = world.Tick;
            Date = world.Date;
            StateHash = result != null ? result.StateHash : StateHasher.Compute(world);
            PathFailureCount = pathFailureCount;
            if (result != null)
            {
                AppendEvents(result.Events);
            }

            Rebuild(world);
        }

        public NpcDetails GetDetails(EntityId id)
        {
            NpcDetails details;
            return _details.TryGetValue(id, out details) ? details : null;
        }

        private void AppendEvents(List<SimEvent> events)
        {
            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Count; i++)
            {
                SimEvent evt = events[i];
                if (evt != null)
                {
                    _recentEvents.Add(new EventSnapshot(evt));
                }
            }

            while (_recentEvents.Count > MaxRecentEvents)
            {
                _recentEvents.RemoveAt(0);
            }
        }

        private void Rebuild(WorldState world)
        {
            SocialTimeline = Array.AsReadOnly(new List<MyAiWorld.Sim.Agents.SocialEvent>(world.Social.Timeline).ToArray());
            EcologyEconomy = new EcologyEconomySnapshot(world);
            Combat = new CombatReadModel(world.Combat);
            _nonLocalAnimals.Clear();
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Region != -1) { _nonLocalAnimals.Add(animal.Id); }
            }
            Survival = new SurvivalReadModel(world);
            Campaign = new CampaignSnapshot(world);
            Environment = new EnvironmentSnapshot(world);
            _npcs.Clear();
            _details.Clear();
            CriticalNpcCount = 0;
            IReadOnlyList<NpcState> source = world.Entities.Npcs;
            for (int i = 0; i < source.Count; i++)
            {
                NpcState npc = source[i];
                string label = npc.Action.Label + (npc.Action.Type == NpcActionType.Move ? " -> " + npc.Action.Target : "");
                AttackSnapshot attack = Combat.GetAttack(npc.Id);
                _npcs.Add(new NpcSnapshot(npc.Id, npc.Name, npc.Role, npc.Position, label, npc.Action.Type,
                    npc.Needs.Health, npc.Survival.Life, npc.Survival.BodyTemperature, npc.Survival.Wetness,
                    npc.Survival.ColdTicks, npc.Survival.ShelterId, npc.Survival.CoatDurability,
                    npc.Inventory.Count(ItemKind.Arrow), attack, npc.Home));
                List<NeedsSample> history;
                if (!_history.TryGetValue(npc.Id, out history))
                {
                    history = new List<NeedsSample>();
                    _history.Add(npc.Id, history);
                }

                if (history.Count == 0 || Tick - history[history.Count - 1].Tick >= NeedsHistoryInterval)
                {
                    history.Add(new NeedsSample(Tick, npc.Needs));
                    if (history.Count > MaxNeedsHistory)
                    {
                        history.RemoveAt(0);
                    }
                }

                _details[npc.Id] = new NpcDetails(npc, Tick, Array.AsReadOnly(history.ToArray()), attack);
                if (npc.Needs.Food < 2000 || npc.Needs.Water < 2000 || npc.Needs.Energy < 2000)
                {
                    CriticalNpcCount++;
                }
            }

            _resourceNodes.Clear();
            TotalNodeFood = 0;
            TotalNodeWater = 0;
            var nodes = world.Resources.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = new ResourceNodeSnapshot(nodes[i]);
                _resourceNodes.Add(node);
                if (node.Item == ItemKind.Food)
                {
                    TotalNodeFood += node.Amount;
                }
                else if (node.Item == ItemKind.RawWater)
                {
                    TotalNodeWater += node.Amount;
                }
            }

            _farmPlots.Clear();
            MatureCropCount = 0;
            GrowingCropCount = 0;
            EmptyPlotCount = 0;
            DryCropCount = 0;
            var plots = world.Resources.FarmPlots;
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = new FarmPlotSnapshot(plots[i]);
                _farmPlots.Add(plot);
                if (plot.Stage == CropStage.Mature)
                {
                    MatureCropCount++;
                }
                else if (plot.Stage == CropStage.Empty)
                {
                    EmptyPlotCount++;
                }
                else
                {
                    GrowingCropCount++;
                }

                if (plot.Stage != CropStage.Empty && plot.Moisture <= 0)
                {
                    DryCropCount++;
                }
            }

            StockpileCell = world.Resources.StockpilePosition;
            var stock = world.Resources.Stockpile;
            StockFood = stock.Food; StockWater = stock.Water; StockSeeds = stock.Seeds;
            Stockpile = "Food " + stock.Food + "   Water " + stock.Water + "   Seeds " + stock.Seeds;
            ReservationCount = world.Resources.Reservations.Count;
        }
    }
}
