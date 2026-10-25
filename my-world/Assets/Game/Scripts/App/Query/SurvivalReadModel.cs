using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class FarmingPolicyOrderSnapshot
    {
        internal FarmingPolicyOrderSnapshot(bool heating, int targetId, int policy)
        { Heating = heating; TargetId = targetId; Policy = policy; }
        public bool Heating { get; }
        public int TargetId { get; }
        public int Policy { get; }
    }
    public sealed class FacilitySnapshot
    {
        internal FacilitySnapshot(FacilityState f)
        {
            Id = f.Id; Kind = f.Kind; Cell = f.Position; WorkCell = f.WorkCell; Complete = f.Complete; Progress = f.Progress;
            Condition = f.Condition; ReservedBy = f.ReservedBy;
            var items = new int[(int)ItemKind.Count]; for (int i = 0; i < items.Length; i++) { items[i] = f.Inventory.Count((ItemKind)i); }
            Items = Array.AsReadOnly(items);
        }
        public int Id { get; }
        public FacilityKind Kind { get; }
        public GridPos Cell { get; }
        public GridPos WorkCell { get; }
        public bool Complete { get; }
        public int Progress { get; }
        public int Condition { get; }
        public EntityId ReservedBy { get; }
        public IReadOnlyList<int> Items { get; }
    }
    public sealed class GroundStockSnapshot
    {
        internal GroundStockSnapshot(GroundStockState g) { Id = g.Id; Cell = g.Position; Count = g.Inventory.TotalCount; Coins = g.Coins; Empty = g.Empty; }
        public int Id { get; }
        public GridPos Cell { get; }
        public int Count { get; }
        public long Coins { get; }
        public bool Empty { get; }
    }
    public sealed class ShelterSnapshot
    {
        internal ShelterSnapshot(ShelterState shelter)
        {
            Id = shelter.Id;
            Cell = shelter.Position;
            Entrance = shelter.Entrance;
            Capacity = shelter.Capacity;
            Temperature = shelter.Temperature;
            FuelTicks = shelter.FuelTicks;
            HeatBudget = shelter.HeatBudget;
            var items = new int[(int)ItemKind.Count];
            for (int i = 0; i < items.Length; i++) { items[i] = shelter.Inventory.Count((ItemKind)i); }
            Items = Array.AsReadOnly(items);
            Wood = items[(int)ItemKind.Wood];
        }

        public int Id { get; }
        public GridPos Cell { get; }
        public GridPos Entrance { get; }
        public int Capacity { get; }
        public int Temperature { get; }
        public int FuelTicks { get; }
        public int HeatBudget { get; }
        public int Wood { get; }
        public IReadOnlyList<int> Items { get; }
        public bool Burning => FuelTicks > 0 && HeatBudget > 0;
    }
    public sealed class GrowingBedSnapshot
    {
        internal GrowingBedSnapshot(WorldState world, FacilityState bed)
        {
            Id = bed.Id; Cell = bed.Position; Complete = bed.Complete; Condition = bed.Condition;
            Progress = bed.Progress; Status = GrowingBedSystem.Status(world, bed);
            Temperature = bed.Temperature; FuelTicks = bed.FuelTicks; HeatBudget = bed.HeatBudget;
            Wood = bed.Inventory.Count(ItemKind.Wood); SnowDepth = bed.SnowDepth; HeatPolicy = bed.HeatPolicy;
            PlotIds = Array.AsReadOnly(new List<int>(bed.PlotIds).ToArray());
        }
        public int Id { get; }
        public GridPos Cell { get; }
        public bool Complete { get; }
        public int Condition { get; }
        public int Temperature { get; }
        public int FuelTicks { get; }
        public int HeatBudget { get; }
        public int Wood { get; }
        public int SnowDepth { get; }
        public GrowingBedHeatPolicy HeatPolicy { get; }
        public int Progress { get; }
        public string Status { get; }
        public IReadOnlyList<int> PlotIds { get; }
    }
    public sealed class SurvivalReadModel
    {
        internal SurvivalReadModel(WorldState world)
        {
            var shelters = new List<ShelterSnapshot>();
            foreach (ShelterState shelter in world.Survival.Shelters) { shelters.Add(new ShelterSnapshot(shelter)); }
            Shelters = shelters.AsReadOnly();
            var facilities = new List<FacilitySnapshot>(); foreach (var f in world.Survival.Facilities) { facilities.Add(new FacilitySnapshot(f)); } Facilities = facilities.AsReadOnly();
            var ground = new List<GroundStockSnapshot>(); foreach (var g in world.Survival.Ground) { if (!g.Empty) { ground.Add(new GroundStockSnapshot(g)); } } Ground = ground.AsReadOnly();
            var stock = new int[(int)ItemKind.Count]; for (int i = 0; i < stock.Length; i++) { stock[i] = world.Resources.Stockpile.Count((ItemKind)i); } Stock = Array.AsReadOnly(stock);
            var beds = new List<GrowingBedSnapshot>();
            foreach (var f in world.Survival.Facilities) { if (f.Kind == FacilityKind.GrowingBed) { beds.Add(new GrowingBedSnapshot(world, f)); } }
            GrowingBeds = beds.AsReadOnly();
            FarmFoodDelivered = world.Survival.FarmFoodDelivered;
            Living = world.LivingCount; CropsLost = world.Survival.CropsLost; RefillWater = world.Survival.RefillWater;
            FoodTarget = Math.Max(40, Living * (world.Date.Season == Season.Autumn ? 24 : 8)); WaterTarget = Math.Max(20, Living * 5);
            foreach (var npc in world.Entities.Npcs)
            { if (npc.Survival.Life == LifeState.Downed) { Downed++; } if (npc.Survival.Life == LifeState.Recovering) { Recovering++; } if (npc.Survival.Life == LifeState.Dead) { Dead++; } }
        }
        public IReadOnlyList<GrowingBedSnapshot> GrowingBeds { get; }
        public IReadOnlyList<FacilitySnapshot> Facilities { get; }
        public IReadOnlyList<ShelterSnapshot> Shelters { get; }
        public IReadOnlyList<GroundStockSnapshot> Ground { get; }
        public IReadOnlyList<int> Stock { get; }
        public int Living { get; }
        public int Downed { get; }
        public int Recovering { get; }
        public int Dead { get; }
        public int CropsLost { get; }
        public long FarmFoodDelivered { get; }
        public bool RefillWater { get; }
        public int FoodTarget { get; }
        public int WaterTarget { get; }
    }
}
