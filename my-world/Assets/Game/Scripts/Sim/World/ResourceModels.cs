using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum ItemKind
    {
        Food, Water, SpinachSeed, RawWater, Wood, Stone, Fiber, Herb, Medicine, KaleSeed, LambsLettuceSeed, Bow, Spear, Hoe, Bucket,
        Arrow, RawMeat, Hide, WinterCoat, Fur, Feather, Count
    }
    public enum CropType
    {
        None, Spinach, Kale, LambsLettuce
    }
    public enum CropStage
    {
        Empty, Seedling, Growing, Mature
    }
    public enum ReservationKind
    {
        Node, FarmPlot, Stockpile, WorkCell, HuntSupply
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
        // Direct farm cargo is consumed first; only an actual stockpile deposit counts as delivery.
        public int UndeliveredFarmFood { get; internal set; }
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
            if (item == ItemKind.Food) { UndeliveredFarmFood = Math.Max(0, UndeliveredFarmFood - amount); }
            return true;
        }

        public string Summary => "Food " + Food + "  Water " + Water + "  Seeds " + Seeds + " (" + TotalCount + "/" + Capacity + ")";
    }

    public sealed class CropDefinition
    {
        // Game balance values, in effective daylight minutes and centi-degrees.
        public static readonly CropDefinition Spinach = new CropDefinition(CropType.Spinach, ItemKind.SpinachSeed, 700, 450, 3, 2, 300, -800);
        public static readonly CropDefinition Kale = new CropDefinition(CropType.Kale, ItemKind.KaleSeed, 1100, 700, 4, 3, 400, -1200);
        public static readonly CropDefinition LambsLettuce = new CropDefinition(CropType.LambsLettuce, ItemKind.LambsLettuceSeed, 850, 550, 2, 2, 200, -1000);
        public static readonly IReadOnlyList<CropDefinition> All = Array.AsReadOnly(new[] { Spinach, Kale, LambsLettuce });
        private CropDefinition(CropType type, ItemKind seed, int growth, int regrowth, int yield, int harvests, int minimum, int tolerance)
        {
            Type = type; Seed = seed; GrowthTicks = growth; RegrowthTicks = regrowth;
            Yield = yield; MaxHarvests = harvests; MinimumTemperature = minimum; ColdTolerance = tolerance;
        }
        public CropType Type { get; }
        public ItemKind Seed { get; }
        public int GrowthTicks { get; }
        public int RegrowthTicks { get; }
        public int WaterRequired => 1;
        public int Yield { get; }
        public int SeedYield => 0;
        public int MaxHarvests { get; }
        public int MinimumTemperature { get; }
        public int ColdTolerance { get; }
        public int FertilityChange => -400;
        public static CropDefinition For(CropType type) => type == CropType.Kale ? Kale : type == CropType.LambsLettuce ? LambsLettuce : Spinach;
        public static CropDefinition FromSeed(ItemKind item) => item == ItemKind.KaleSeed ? Kale : item == ItemKind.LambsLettuceSeed ? LambsLettuce : Spinach;
    }

    public enum FarmCropPolicy { Auto, Spinach, Kale, LambsLettuce, Fallow }
    public enum GrowingBedHeatPolicy { Off, Normal, Priority }

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
        public int GrowthRequired => HarvestCount == 0 ? CropDefinition.For(Crop).GrowthTicks : CropDefinition.For(Crop).RegrowthTicks;
        public FarmCropPolicy Policy { get; internal set; }
        public int BedId { get; internal set; }
        public int HarvestCount { get; internal set; }
        public int TotalHarvests { get; internal set; }
        public long GrowthRemainder { get; internal set; }
        public bool FinishAfterHarvest { get; internal set; }
        public bool LostCrop { get; internal set; }
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

        internal int TakeHuntSupply(EntityId owner, ItemKind item, int quantity)
        {
            int taken = 0;
            for (int i = _reservations.Count - 1; i >= 0 && taken < quantity; i--)
            {
                ResourceReservation r = _reservations[i];
                if (r.Owner != owner || r.Kind != ReservationKind.HuntSupply || r.Item != item) { continue; }
                int amount = Math.Min(quantity - taken, r.Quantity);
                if (!Stockpile.TryRemove(item, amount)) { break; }
                _reservations.RemoveAt(i);
                if (r.Quantity > amount) { _reservations.Insert(i, new ResourceReservation(owner, r.Kind, 0, item, r.Quantity - amount, StockpilePosition)); }
                taken += amount;
            }
            return taken;
        }

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
                if ((reservation.Kind == ReservationKind.Stockpile || reservation.Kind == ReservationKind.HuntSupply) && reservation.Item == item)
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
                if (_reservations[i].Kind != ReservationKind.HuntSupply && _reservations[i].Owner != owner && _reservations[i].Position == position)
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
            if ((reservation.Kind == ReservationKind.Stockpile || reservation.Kind == ReservationKind.HuntSupply) && reservation.Position != StockpilePosition)
            {
                return false;
            }
            for (int i = 0; i < _reservations.Count; i++)
            {
                ResourceReservation existing = _reservations[i];
                if (reservation.Kind != ReservationKind.HuntSupply && existing.Kind != ReservationKind.HuntSupply && existing.Owner != reservation.Owner && existing.Position == reservation.Position)
                {
                    return false;
                }
            }
            if ((reservation.Kind == ReservationKind.Stockpile || reservation.Kind == ReservationKind.HuntSupply) && AvailableStock(reservation.Item) < reservation.Quantity)
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
