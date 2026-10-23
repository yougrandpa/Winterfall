using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public enum SupplyStatus { NoDemand, Exhausted, Tight, Sufficient }

    // A pooled game estimate, not a promise of accessibility, delivery or survival.
    public sealed class SupplyEstimateSnapshot
    {
        internal SupplyEstimateSnapshot(long reserve, int consumers, long satisfactionPerUnit,
            int demandPerMinute, long remainingSupplyMinutes = 0)
        {
            Reserve = reserve;
            ReferenceConsumers = consumers;
            Minutes = consumers <= 0 ? 0 :
                (reserve * satisfactionPerUnit + remainingSupplyMinutes) / ((long)consumers * demandPerMinute);
            Status = consumers <= 0 ? SupplyStatus.NoDemand : Minutes <= 0 ? SupplyStatus.Exhausted :
                Minutes < 1440 ? SupplyStatus.Tight : SupplyStatus.Sufficient;
        }

        public long Reserve { get; }
        public int ReferenceConsumers { get; }
        public long Minutes { get; }
        public SupplyStatus Status { get; }
    }

    public sealed class EnvironmentSnapshot
    {
        public const int FoodBaselinePerMinute = 6;
        public const int FoodSatisfactionPerUnit = 4500;
        public const int WaterBaselinePerMinute = 9;
        public const int WaterSatisfactionPerUnit = 5500;

        internal EnvironmentSnapshot(WorldState world)
        {
            EnvironmentState environment = world.Environment;
            Temperature = environment.Temperature;
            Wind = environment.Wind;
            Daylight = environment.Daylight;
            SnowDepth = environment.SnowDepth;
            Weather = environment.Weather;
            IsDaytime = environment.IsDaytime;
            WaterFrozen = environment.WaterFrozen;
            Living = world.LivingCount;

            var inventories = new HashSet<InventoryState>();
            long food = 0, water = 0, fuel = 0;
            AddInventory(inventories, world.Resources.Stockpile, ref food, ref water, ref fuel);
            AddInventory(inventories, world.Market.Inventory, ref food, ref water, ref fuel);
            foreach (var facility in world.Survival.Facilities) { AddInventory(inventories, facility.Inventory, ref food, ref water, ref fuel); }
            foreach (var ground in world.Survival.Ground) { AddInventory(inventories, ground.Inventory, ref food, ref water, ref fuel); }
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Survival.Life != LifeState.Dead) { AddInventory(inventories, npc.Inventory, ref food, ref water, ref fuel); }
            }
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                AddInventory(inventories, shelter.Inventory, ref food, ref water, ref fuel);
                if (!world.Map.IsInside(shelter.Position) || world.Map.GetTile(shelter.Position) != TileType.House ||
                    !world.Map.IsWalkable(shelter.Entrance) || shelter.Position.ManhattanDistance(shelter.Entrance) != 1)
                {
                    continue;
                }
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    if (npc.Survival.Life == LifeState.Dead || npc.Survival.ShelterId != shelter.Id) { continue; }
                    OccupiedShelters++;
                    // Snow melting can spend heat independently of burn time: do not count spent heat twice.
                    RemainingHeatingMinutes += Math.Max(0, Math.Min(shelter.FuelTicks, shelter.HeatBudget / SurvivalRules.HeatPerTick));
                    break;
                }
            }
            FoodReserve = food;
            WaterReserve = water;
            FuelReserve = fuel;
            Food = new SupplyEstimateSnapshot(FoodReserve, Living, FoodSatisfactionPerUnit, FoodBaselinePerMinute);
            Water = new SupplyEstimateSnapshot(WaterReserve, Living, WaterSatisfactionPerUnit, WaterBaselinePerMinute);
            Fuel = new SupplyEstimateSnapshot(FuelReserve, OccupiedShelters, SurvivalRules.FuelTicksPerWood, 1, RemainingHeatingMinutes);
        }

        private static void AddInventory(HashSet<InventoryState> inventories, InventoryState inventory,
            ref long food, ref long water, ref long fuel)
        {
            if (!inventories.Add(inventory)) { return; }
            food += inventory.Count(ItemKind.Food);
            water += inventory.Count(ItemKind.Water);
            fuel += inventory.Count(ItemKind.Wood);
        }

        // Simulation units: Celsius * 100; wind 0..100; daylight 0..10000.
        public int Temperature { get; }
        public int Wind { get; }
        public int Daylight { get; }
        public int SnowDepth { get; }
        public WeatherKind Weather { get; }
        public bool IsDaytime { get; }
        public bool WaterFrozen { get; }
        public int Living { get; }
        public long FoodReserve { get; }
        public long WaterReserve { get; }
        public long FuelReserve { get; }
        public int OccupiedShelters { get; }
        public long RemainingHeatingMinutes { get; }
        public SupplyEstimateSnapshot Food { get; }
        public SupplyEstimateSnapshot Water { get; }
        public SupplyEstimateSnapshot Fuel { get; }
    }
}
