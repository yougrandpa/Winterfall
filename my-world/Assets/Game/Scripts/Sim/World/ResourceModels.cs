using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum ItemKind
    {
        Food, Water, Seed, RawWater, Wood, Stone, Fiber, Herb, Medicine, WheatSeed, BeanSeed, Bow, Spear, Hoe, Bucket,
        Arrow, RawMeat, Hide, WinterCoat, Count
    }
    public enum CropType
    {
        None, Turnip, Wheat, Bean
    }
    public enum CropStage
    {
        Empty, Seedling, Growing, Mature
    }
    public enum ReservationKind
    {
        Node, FarmPlot, Stockpile, WorkCell
    }

    public sealed class InventoryState
    {
        private readonly int[] _amounts = new int[(int)ItemKind.Count];

        public InventoryState(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            Capacity = capacity;
        }

        public int Capacity { get; }
        public int TotalCount
        {
            get { int total = 0; foreach (int amount in _amounts) { total += amount; } return total; }
        }
        public int Food => _amounts[0];
        public int Water => _amounts[1];
        public int Seeds => _amounts[2];
        public int Count(ItemKind item)
        {
            return IsItem(item) ? _amounts[(int)item] : 0;
        }

        private static bool IsItem(ItemKind item) => item >= ItemKind.Food && item < ItemKind.Count;

        public bool TryAdd(ItemKind item, int amount)
        {
            if (!IsItem(item) || amount <= 0 || amount > Capacity - TotalCount)
            {
                return false;
            }
            _amounts[(int)item] += amount;
            return true;
        }

        public bool TryRemove(ItemKind item, int amount)
        {
            if (!IsItem(item) || amount <= 0 || amount > Count(item))
            {
                return false;
            }
            _amounts[(int)item] -= amount;
            return true;
        }

        public string Summary => "Food " + Food + "  Water " + Water + "  Seeds " + Seeds + " (" + TotalCount + "/" + Capacity + ")";
    }

    public sealed class CropDefinition
    {
        public static readonly CropDefinition Turnip = new CropDefinition(CropType.Turnip, ItemKind.Seed, 720, 4, -400);
        public static readonly CropDefinition Wheat = new CropDefinition(CropType.Wheat, ItemKind.WheatSeed, 1440, 8, -700);
        public static readonly CropDefinition Bean = new CropDefinition(CropType.Bean, ItemKind.BeanSeed, 1000, 5, 500);
        private CropDefinition(CropType type, ItemKind seed, int growth, int yield, int fertility)
        {
            Type = type; Seed = seed; GrowthTicks = growth; Yield = yield; FertilityChange = fertility;
        }
        public CropType Type { get; }
        public ItemKind Seed { get; }
        public int GrowthTicks { get; }
        public int WaterRequired => 1;
        public int Yield { get; }
        public int SeedYield => 2;
        public int FertilityChange { get; }
        public static CropDefinition For(CropType type) => type == CropType.Wheat ? Wheat : type == CropType.Bean ? Bean : Turnip;
        public static CropDefinition FromSeed(ItemKind item) => item == ItemKind.WheatSeed ? Wheat : item == ItemKind.BeanSeed ? Bean : Turnip;
    }

    public sealed class ResourceNodeState
    {
        public ResourceNodeState(int id, GridPos position, ItemKind item, int capacity, int regenerationTicks)
        {
            if (capacity <= 0 || regenerationTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            Id = id;
            Position = position;
            Item = item;
            Capacity = capacity;
            Amount = capacity;
            RegenerationTicks = regenerationTicks;
        }

        public int Id { get; }
        public GridPos Position { get; }
        public ItemKind Item { get; }
        public int Amount { get; internal set; }
        public int Capacity { get; }
        public int RegenerationTicks { get; }
        public int RegenerationProgress { get; internal set; }
        public EntityId ReservedBy { get; internal set; }
    }

    public sealed class FarmPlotState
    {
        public FarmPlotState(int id, GridPos position)
        {
            Id = id;
            Position = position;
            Fertility = 10000;
        }

        public int Id { get; }
        public GridPos Position { get; }
        public CropType Crop { get; internal set; }
        public int GrowthTicks { get; internal set; }
        public int GrowthRequired => CropDefinition.For(Crop).GrowthTicks;
        public CropType PreviousCrop { get; internal set; }
        public int Damage { get; internal set; }
        public int HarvestYield => Math.Max(1, CropDefinition.For(Crop).Yield - Damage / 600);
        public int Moisture { get; internal set; }
        public int Fertility { get; internal set; }
        public EntityId ReservedBy { get; internal set; }
        public CropStage Stage => Crop == CropType.None ? CropStage.Empty :
            GrowthTicks >= GrowthRequired ? CropStage.Mature :
            GrowthTicks < GrowthRequired / 3 ? CropStage.Seedling : CropStage.Growing;
    }

    public sealed class ResourceReservation
    {
        public ResourceReservation(EntityId owner, ReservationKind kind, int resourceId, ItemKind item, int quantity, GridPos position)
        {
            Owner = owner;
            Kind = kind;
            ResourceId = resourceId;
            Item = item;
            Quantity = quantity;
            Position = position;
        }

        public EntityId Owner { get; }
        public ReservationKind Kind { get; }
        public int ResourceId { get; }
        public ItemKind Item { get; }
        public int Quantity { get; }
        public GridPos Position { get; }
    }

    public sealed class WorldResourceState
    {
        private readonly List<ResourceNodeState> _nodes = new List<ResourceNodeState>();
        private readonly List<FarmPlotState> _plots = new List<FarmPlotState>();
        private readonly List<ResourceReservation> _reservations = new List<ResourceReservation>();
        private readonly IReadOnlyList<ResourceNodeState> _nodeView;
        private readonly IReadOnlyList<FarmPlotState> _plotView;
        private readonly IReadOnlyList<ResourceReservation> _reservationView;

        public WorldResourceState(GridPos stockpilePosition)
        {
            _nodeView = _nodes.AsReadOnly();
            _plotView = _plots.AsReadOnly();
            _reservationView = _reservations.AsReadOnly();
            StockpilePosition = stockpilePosition;
            Stockpile = new InventoryState(4000);
        }

        public InventoryState Stockpile { get; }
        public GridPos StockpilePosition { get; }
        public IReadOnlyList<ResourceNodeState> Nodes => _nodeView;
        public IReadOnlyList<FarmPlotState> FarmPlots => _plotView;
        public IReadOnlyList<ResourceReservation> Reservations => _reservationView;

        internal void AddNode(ResourceNodeState node)
        {
            _nodes.Add(node);
        }
        internal void AddPlot(FarmPlotState plot)
        {
            _plots.Add(plot);
        }
        public ResourceNodeState FindNode(int id) => _nodes.Find(node => node.Id == id);
        public FarmPlotState FindPlot(int id) => _plots.Find(plot => plot.Id == id);

        public int AvailableStock(ItemKind item)
        {
            int available = Stockpile.Count(item);
            for (int i = 0; i < _reservations.Count; i++)
            {
                ResourceReservation reservation = _reservations[i];
                if (reservation.Kind == ReservationKind.Stockpile && reservation.Item == item)
                {
                    available -= reservation.Quantity;
                }
            }
            return Math.Max(0, available);
        }

        public bool IsWorkCellAvailable(EntityId owner, GridPos position)
        {
            for (int i = 0; i < _reservations.Count; i++)
            {
                if (_reservations[i].Owner != owner && _reservations[i].Position == position)
                {
                    return false;
                }
            }
            return true;
        }

        internal bool TryReserve(ResourceReservation reservation)
        {
            if (reservation == null || reservation.Owner.IsNone || reservation.Quantity < 0)
            {
                return false;
            }
            if (reservation.Kind == ReservationKind.Stockpile && reservation.Position != StockpilePosition)
            {
                return false;
            }
            for (int i = 0; i < _reservations.Count; i++)
            {
                ResourceReservation existing = _reservations[i];
                if (existing.Owner != reservation.Owner && existing.Position == reservation.Position)
                {
                    return false;
                }
            }
            if (reservation.Kind == ReservationKind.Stockpile && AvailableStock(reservation.Item) < reservation.Quantity)
            {
                return false;
            }
            ResourceNodeState node = reservation.Kind == ReservationKind.Node ? FindNode(reservation.ResourceId) : null;
            FarmPlotState plot = reservation.Kind == ReservationKind.FarmPlot ? FindPlot(reservation.ResourceId) : null;
            if (reservation.Kind == ReservationKind.Node && (node == null || node.Position != reservation.Position || node.Item != reservation.Item || !node.ReservedBy.IsNone || node.Amount < reservation.Quantity))
            {
                return false;
            }
            if (reservation.Kind == ReservationKind.FarmPlot && (plot == null || plot.Position != reservation.Position || !plot.ReservedBy.IsNone))
            {
                return false;
            }
            _reservations.Add(reservation);
            if (node != null)
            {
                node.ReservedBy = reservation.Owner;
            }
            if (plot != null)
            {
                plot.ReservedBy = reservation.Owner;
            }
            return true;
        }

        internal void Release(EntityId owner)
        {
            for (int i = _reservations.Count - 1; i >= 0; i--)
            {
                ResourceReservation reservation = _reservations[i];
                if (reservation.Owner != owner)
                {
                    continue;
                }
                if (reservation.Kind == ReservationKind.Node)
                {
                    FindNode(reservation.ResourceId).ReservedBy = EntityId.None;
                }
                if (reservation.Kind == ReservationKind.FarmPlot)
                {
                    FindPlot(reservation.ResourceId).ReservedBy = EntityId.None;
                }
                _reservations.RemoveAt(i);
            }
        }

        internal void ReleaseStep(EntityId owner, ReservationKind kind, int resourceId, ItemKind item)
        {
            for (int i = 0; i < _reservations.Count; i++)
            {
                ResourceReservation reservation = _reservations[i];
                if (reservation.Owner != owner || reservation.Kind != kind || reservation.ResourceId != resourceId || reservation.Item != item)
                {
                    continue;
                }
                _reservations.RemoveAt(i);
                if (kind == ReservationKind.Node)
                {
                    FindNode(resourceId).ReservedBy = EntityId.None;
                }
                if (kind == ReservationKind.FarmPlot)
                {
                    FindPlot(resourceId).ReservedBy = EntityId.None;
                }
                return;
            }
        }

        internal void Update(MaterialBalance materials = null, Season season = Season.Spring, long tick = 0)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                ResourceNodeState node = _nodes[i];
                if (node.Amount >= node.Capacity)
                {
                    node.RegenerationProgress = 0;
                    continue;
                }
                if (season == Season.Winter && node.Item == ItemKind.Food && tick % 4 != 0) { continue; }
                node.RegenerationProgress++;
                if (node.RegenerationProgress >= node.RegenerationTicks)
                {
                    node.Amount++;
                    materials?.Produce(node.Item, 1);
                    node.RegenerationProgress = 0;
                }
            }
            // Crop progression is owned by AgricultureSystem, with calendar context.
        }
    }
}
