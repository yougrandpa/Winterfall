using System.Collections.Generic;
using System.Collections.ObjectModel;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class AnimalSnapshot
    {
        internal AnimalSnapshot(AnimalState animal)
        {
            Id = animal.Id;
            Species = animal.Species;
            Cell = animal.Position;
            Health = animal.Health;
            Fleeing = animal.Fleeing;
            ReservedBy = animal.ReservedBy;
        }

        public EntityId Id { get; }
        public AnimalSpecies Species { get; }
        public GridPos Cell { get; }
        public int Health { get; }
        public bool Fleeing { get; }
        public EntityId ReservedBy { get; }
    }

    public sealed class EcologyEconomySnapshot
    {
        internal EcologyEconomySnapshot(WorldState world)
        {
            var animals = new List<AnimalSnapshot>();
            for (int i = 0; i < world.Ecology.Animals.Count; i++)
            {
                if (world.Ecology.Animals[i].Health > 0)
                {
                    animals.Add(new AnimalSnapshot(world.Ecology.Animals[i]));
                }
            }
            Animals = animals.AsReadOnly();
            Rabbits = world.Ecology.Count(AnimalSpecies.Rabbit);
            Boars = world.Ecology.Count(AnimalSpecies.Boar);
            Deer = world.Ecology.Count(AnimalSpecies.Deer);
            Births = world.Ecology.Births;
            NaturalDeaths = world.Ecology.NaturalDeaths;
            HuntKills = world.Ecology.HuntKills;
            var prices = new List<int>();
            var stocks = new List<int>();
            var bought = new List<long>();
            var sold = new List<long>();
            long materialError = 0;
            for (int i = 0; i < 3; i++)
            {
                ItemKind item = (ItemKind)i;
                prices.Add(world.Market.Price(item));
                stocks.Add(world.Market.Inventory.Count(item));
                bought.Add(world.Market.Bought(item));
                sold.Add(world.Market.Sold(item));
                materialError += System.Math.Abs(world.Materials.Error(world, item));
            }
            for (int i = 3; i < (int)ItemKind.Count; i++) { materialError += System.Math.Abs(world.Materials.Error(world, (ItemKind)i)); }
            Prices = prices.AsReadOnly();
            Stocks = stocks.AsReadOnly();
            Bought = bought.AsReadOnly();
            Sold = sold.AsReadOnly();
            Treasury = world.Market.Treasury;
            CurrencyError = world.Market.CurrencyError(world);
            MaterialError = materialError;
            Filled = world.Market.FilledCount;
            Rejected = world.Market.RejectedCount;
            Volume = world.Market.Volume;
            var ledger = new List<TradeRecord>();
            for (int i = System.Math.Max(0, world.Market.Ledger.Count - 4); i < world.Market.Ledger.Count; i++)
            {
                ledger.Add(world.Market.Ledger[i]);
            }
            RecentTrades = ledger.AsReadOnly();
        }

        public ReadOnlyCollection<AnimalSnapshot> Animals { get; }
        public int Rabbits { get; }
        public int Boars { get; }
        public int Deer { get; }
        public int Births { get; }
        public int NaturalDeaths { get; }
        public int HuntKills { get; }
        public ReadOnlyCollection<int> Prices { get; }
        public ReadOnlyCollection<int> Stocks { get; }
        public ReadOnlyCollection<long> Bought { get; }
        public ReadOnlyCollection<long> Sold { get; }
        public long Treasury { get; }
        public long CurrencyError { get; }
        public long MaterialError { get; }
        public int Filled { get; }
        public int Rejected { get; }
        public long Volume { get; }
        public ReadOnlyCollection<TradeRecord> RecentTrades { get; }
    }
}
