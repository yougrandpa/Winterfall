using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum LifeState { Active, Downed, Recovering, Dead }
    public enum FacilityKind { Workbench, Purifier, Cistern, Well, Irrigation }

    public static class SurvivalRules
    {
        public const int ToolDurability = 120;
        public const int RescueWindow = 180;
        public const int DiseaseDuration = 720;
        public const int DiseaseRisk = 35;
        public const int CropDeathDamage = 1800;
        public const int ConstructionTicks = 30;
        public const int MaintenanceInterval = 3 * 1440;
        public const int MarketItemCount = 3;
        public static bool IsTool(ItemKind item) => item >= ItemKind.Bow && item <= ItemKind.Bucket;
        public static bool IsSeed(ItemKind item) => item == ItemKind.Seed || item == ItemKind.WheatSeed || item == ItemKind.BeanSeed;
    }

    public sealed class SurvivalNpcState
    {
        internal readonly int[] Tools = new int[4];
        public LifeState Life { get; internal set; }
        public long RescueDeadline { get; internal set; }
        public EntityId Rescuer { get; internal set; }
        public int DiseaseTicks { get; internal set; }
        public long WorkRetryTick { get; internal set; }
        public int HuntDropId { get; internal set; }
        public int Rescues { get; internal set; }
        public int Crafted { get; internal set; }
        public int Built { get; internal set; }
        public int WaterProduced { get; internal set; }
        public int Durability(ItemKind item) => SurvivalRules.IsTool(item) ? Tools[(int)item - (int)ItemKind.Bow] : 0;
        internal void SetTool(ItemKind item, int durability) { Tools[(int)item - (int)ItemKind.Bow] = durability; }
        public int EquipmentCount(ItemKind item) => Durability(item) > 0 ? 1 : 0;
    }

    public sealed class FacilityState
    {
        public FacilityState(int id, FacilityKind kind, GridPos position, GridPos workCell)
        {
            Id = id; Kind = kind; Position = position; WorkCell = workCell;
        }
        public int Id { get; }
        public FacilityKind Kind { get; }
        public GridPos Position { get; }
        public GridPos WorkCell { get; }
        public InventoryState Inventory { get; } = new InventoryState(120);
        public int Progress { get; internal set; }
        public bool Complete => Progress >= SurvivalRules.ConstructionTicks;
        public int Condition { get; internal set; } = 100;
        public EntityId ReservedBy { get; internal set; }
        public int WellNodeId { get; internal set; }
    }

    public sealed class GroundStockState
    {
        public GroundStockState(int id, GridPos position) { Id = id; Position = position; }
        public int Id { get; }
        public GridPos Position { get; }
        public InventoryState Inventory { get; } = new InventoryState(1000);
        internal readonly int[] Tools = new int[4];
        public long Coins { get; internal set; }
        public EntityId ReservedBy { get; internal set; }
        public bool Empty => Inventory.TotalCount == 0 && Coins == 0 && Array.TrueForAll(Tools, value => value == 0);
    }

    public sealed class SurvivalWorldState
    {
        internal readonly List<FacilityState> MutableFacilities = new List<FacilityState>();
        internal readonly List<GroundStockState> MutableGround = new List<GroundStockState>();
        public SurvivalWorldState() { Facilities = MutableFacilities.AsReadOnly(); Ground = MutableGround.AsReadOnly(); }
        public IReadOnlyList<FacilityState> Facilities { get; }
        public IReadOnlyList<GroundStockState> Ground { get; }
        public int NextId { get; internal set; } = 1;
        public bool RefillWater { get; internal set; } = true;
        public int CropsLost { get; internal set; }
        public long GroundCoins { get { long total = 0; foreach (var stock in Ground) { total += stock.Coins; } return total; } }
        public FacilityState FindFacility(int id) => MutableFacilities.Find(f => f.Id == id);
        public GroundStockState FindGround(int id) => MutableGround.Find(g => g.Id == id);
        public FacilityState Facility(FacilityKind kind) => MutableFacilities.Find(f => f.Kind == kind && f.Complete && f.Condition > 0);
        public long Count(ItemKind item)
        {
            long total = 0;
            foreach (var facility in Facilities) { total += facility.Inventory.Count(item); }
            foreach (var stock in Ground)
            {
                total += stock.Inventory.Count(item);
                if (SurvivalRules.IsTool(item) && stock.Tools[(int)item - (int)ItemKind.Bow] > 0) { total++; }
            }
            return total;
        }
        internal GroundStockState Drop(GridPos position)
        {
            var stock = new GroundStockState(NextId++, position); MutableGround.Add(stock); return stock;
        }
        internal void Release(EntityId owner)
        {
            foreach (var facility in Facilities) { if (facility.ReservedBy == owner) { facility.ReservedBy = EntityId.None; } }
            foreach (var stock in Ground) { if (stock.ReservedBy == owner) { stock.ReservedBy = EntityId.None; } }
        }
    }

    public sealed class ProductionRecipe
    {
        public ProductionRecipe(ItemKind output, int quantity, FacilityKind facility, int ticks, params int[] inputs)
        { Output = output; Quantity = quantity; Facility = facility; Ticks = ticks; Inputs = inputs; }
        public ItemKind Output { get; }
        public int Quantity { get; }
        public FacilityKind Facility { get; }
        public int Ticks { get; }
        internal int[] Inputs { get; }
        public static readonly ProductionRecipe[] All =
        {
            new ProductionRecipe(ItemKind.Water, 6, FacilityKind.Purifier, 8, (int)ItemKind.RawWater, 6, (int)ItemKind.Wood, 1),
            new ProductionRecipe(ItemKind.Medicine, 2, FacilityKind.Workbench, 10, (int)ItemKind.Herb, 2),
            new ProductionRecipe(ItemKind.Bow, 1, FacilityKind.Workbench, 12, (int)ItemKind.Wood, 2, (int)ItemKind.Fiber, 2),
            new ProductionRecipe(ItemKind.Spear, 1, FacilityKind.Workbench, 10, (int)ItemKind.Wood, 2, (int)ItemKind.Stone, 1),
            new ProductionRecipe(ItemKind.Hoe, 1, FacilityKind.Workbench, 10, (int)ItemKind.Wood, 1, (int)ItemKind.Stone, 2),
            new ProductionRecipe(ItemKind.Bucket, 1, FacilityKind.Workbench, 10, (int)ItemKind.Wood, 2, (int)ItemKind.Fiber, 1)
        };
        public static ProductionRecipe For(ItemKind item) => Array.Find(All, recipe => recipe.Output == item);
        public static int[] ConstructionCost(FacilityKind kind) => kind == FacilityKind.Workbench ?
            new[] { (int)ItemKind.Wood, 4, (int)ItemKind.Stone, 2 } :
            kind == FacilityKind.Purifier ? new[] { (int)ItemKind.Stone, 4, (int)ItemKind.Wood, 2 } :
            new[] { (int)ItemKind.Wood, 4, (int)ItemKind.Stone, 4 };
    }
}
