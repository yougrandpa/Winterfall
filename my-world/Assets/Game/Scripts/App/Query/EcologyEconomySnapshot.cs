using System;
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
            Encounter = animal.Encounter.Phase; Stamina = animal.Encounter.Stamina;
            ReservedBy = animal.ReservedBy;
            Energy = animal.Energy;
            EnergyCapacity = animal.Definition.EnergyCapacity;
            BodyMass = animal.BodyMass;
            Hydration = animal.Hydration;
            CoreTemperature = animal.CoreTemperature;
            AgeTicks = animal.AgeTicks;
            LifeStage = animal.LifeStage;
            Sex = animal.Sex;
            Behavior = animal.Behavior;
            HabitatId = animal.HabitatId;
            GroupId = animal.GroupId;
            TerritoryHabitatId = animal.TerritoryHabitatId;
            MotherId = animal.MotherId;
            FatherId = animal.FatherId;
            MateId = animal.MateId;
            PregnancyFatherId = animal.PregnancyFatherId;
            PregnancyTicks = animal.PregnancyTicks;
            PregnancyLitter = animal.PregnancyLitter;
            MigrationDestination = animal.MigrationDestination;
            LastMigrationTick = animal.LastMigrationTick;
            MigrationPath = new List<GridPos>(animal.MigrationPath).AsReadOnly();
            MigrationPathIndex = animal.MigrationPathIndex;
            Region = animal.Region;
        }

        public EntityId Id { get; }
        public AnimalSpecies Species { get; }
        public GridPos Cell { get; }
        public int Health { get; }
        public bool Fleeing { get; }
        public EncounterPhase Encounter { get; }
        public int Stamina { get; }
        public EntityId ReservedBy { get; }
        public int Energy { get; }
        public int EnergyCapacity { get; }
        public int BodyMass { get; }
        public int Hydration { get; }
        public int CoreTemperature { get; }
        public int AgeTicks { get; }
        public AnimalLifeStage LifeStage { get; }
        public AnimalSex Sex { get; }
        public AnimalBehavior Behavior { get; }
        public int HabitatId { get; }
        public int GroupId { get; }
        public int TerritoryHabitatId { get; }
        public EntityId MotherId { get; }
        public EntityId FatherId { get; }
        public EntityId MateId { get; }
        public EntityId PregnancyFatherId { get; }
        public int PregnancyTicks { get; }
        public int PregnancyLitter { get; }
        public int MigrationDestination { get; }
        public long LastMigrationTick { get; }
        public ReadOnlyCollection<GridPos> MigrationPath { get; }
        public int MigrationPathIndex { get; }
        public int Region { get; }
    }

    public sealed class CarcassSnapshot
    {
        internal CarcassSnapshot(CarcassState carcass)
        {
            Id = carcass.Id; // The original animal ID, not a new display ID.
            Cell = carcass.Position;
            Species = carcass.Species;
            Region = carcass.Region;
            HabitatId = carcass.HabitatId;
            MeatRemaining = carcass.MeatRemaining;
            HideRemaining = carcass.HideRemaining;
            var harvest = new int[(int)ItemKind.Count];
            for (int item = 0; item < harvest.Length; item++) { harvest[item] = carcass.Remaining((ItemKind)item); }
            Harvest = Array.AsReadOnly(harvest);
            Freshness = carcass.Freshness;
            SourceId = carcass.SourceId;
            DeathCause = carcass.DeathCause;
            Natural = carcass.Natural;
            ReservedBy = carcass.ReservedBy;
            CreatedTick = carcass.CreatedTick;
            var items = new int[(int)ItemKind.Count];
            for (int i = 0; i < items.Length; i++) { items[i] = carcass.Inventory.Count((ItemKind)i); }
            Items = Array.AsReadOnly(items);
        }

        public EntityId Id { get; }
        public GridPos Cell { get; }
        public AnimalSpecies Species { get; }
        public int Region { get; }
        public int HabitatId { get; }
        public int MeatRemaining { get; }
        public int HideRemaining { get; }
        public IReadOnlyList<int> Harvest { get; }
        public int Freshness { get; }
        public EntityId SourceId { get; }
        public AnimalDeathCause DeathCause { get; }
        public bool Natural { get; }
        public EntityId ReservedBy { get; }
        public long CreatedTick { get; }
        public ReadOnlyCollection<int> Items { get; }
    }

    public sealed class VegetationSnapshot
    {
        internal VegetationSnapshot(VegetationState vegetation)
        {
            Kind = vegetation.Kind;
            Biomass = vegetation.Biomass;
            Capacity = vegetation.Capacity;
            SeedBank = vegetation.SeedBank;
        }

        public VegetationKind Kind { get; }
        public int Biomass { get; }
        public int Capacity { get; }
        public int SeedBank { get; }
    }

    public sealed class HabitatSnapshot
    {
        internal HabitatSnapshot(HabitatPatchState patch)
        {
            Id = patch.Id;
            Region = patch.Region;
            Origin = patch.Origin;
            LandCells = patch.LandCells;
            Herbaceous = new VegetationSnapshot(patch.Herbaceous);
            Shrubs = new VegetationSnapshot(patch.Shrubs);
            Trees = new VegetationSnapshot(patch.Trees);
            Water = patch.Water;
            WaterCapacity = patch.WaterCapacity;
            SnowWater = patch.SnowWater;
            SnowDepth = patch.SnowDepth;
            Shelter = patch.Shelter;
            SoilFertility = patch.SoilFertility;
            Litter = patch.Litter;
            MineralRemaining = patch.MineralRemaining;
        }

        public int Id { get; }
        public int Region { get; }
        public GridPos Origin { get; }
        public int LandCells { get; }
        public VegetationSnapshot Herbaceous { get; }
        public VegetationSnapshot Shrubs { get; }
        public VegetationSnapshot Trees { get; }
        public int Water { get; }
        public int WaterCapacity { get; }
        public int SnowWater { get; }
        public int SnowDepth { get; }
        public int Shelter { get; }
        public int SoilFertility { get; }
        public long Litter { get; }
        public int MineralRemaining { get; }
        public long PlantBiomass => (long)Herbaceous.Biomass + Shrubs.Biomass + Trees.Biomass;

        public bool Contains(GridPos cell) => Region == -1 && cell.IsValid && Origin.IsValid &&
            cell.X >= Origin.X && cell.Y >= Origin.Y &&
            cell.X < Origin.X + EcologyState.PatchSize && cell.Y < Origin.Y + EcologyState.PatchSize;
    }

    public sealed class AnimalGroupSnapshot
    {
        internal AnimalGroupSnapshot(AnimalGroupState group, EcologyState ecology)
        {
            Id = group.Id;
            Species = group.Species;
            Region = group.Region;
            TerritoryHabitatId = group.TerritoryHabitatId;
            LeaderId = group.LeaderId;
            Dominance = group.Dominance;
            Disputes = group.Disputes;
            var members = new List<EntityId>();
            foreach (AnimalState animal in ecology.Animals)
            {
                if (animal.Health > 0 && animal.GroupId == group.Id) { members.Add(animal.Id); }
            }
            Members = members.AsReadOnly();
        }

        public int Id { get; }
        public AnimalSpecies Species { get; }
        public int Region { get; }
        public int TerritoryHabitatId { get; }
        public EntityId LeaderId { get; }
        public int Dominance { get; }
        public int Disputes { get; }
        public ReadOnlyCollection<EntityId> Members { get; }
    }

    public sealed class EcologySpeciesSnapshot
    {
        internal EcologySpeciesSnapshot(AnimalSpecies species, IReadOnlyList<AnimalSnapshot> animals)
        {
            Species = species;
            var ages = new int[(int)AnimalLifeStage.Elder + 1];
            var groups = new HashSet<int>();
            foreach (AnimalSnapshot animal in animals)
            {
                if (animal.Species != species) { continue; }
                Count++;
                ages[(int)animal.LifeStage]++;
                if (animal.GroupId > 0) { groups.Add(animal.GroupId); }
            }
            AgeCounts = Array.AsReadOnly(ages);
            GroupCount = groups.Count;
        }

        public AnimalSpecies Species { get; }
        public int Count { get; }
        public int GroupCount { get; }
        public ReadOnlyCollection<int> AgeCounts { get; }
    }

    public sealed class EcologyRegionSnapshot
    {
        internal EcologyRegionSnapshot(EcologyRegionState region, EcologyState ecology)
        {
            Id = region.Id;
            Exit = region.Exit;
            HabitatId = region.HabitatId;
            TemperatureOffset = region.TemperatureOffset;
            Immigration = region.Immigration;
            Emigration = region.Emigration;
            Births = region.Births;
            Deaths = region.Deaths;
            var counts = new int[(int)AnimalSpecies.Raven + 1];
            for (int i = 0; i < counts.Length; i++) { counts[i] = ecology.CountInRegion((AnimalSpecies)i, region.Id); }
            SpeciesCounts = Array.AsReadOnly(counts);
        }

        public int Id { get; }
        public GridPos Exit { get; }
        public int HabitatId { get; }
        public int TemperatureOffset { get; }
        public long Immigration { get; }
        public long Emigration { get; }
        public long Births { get; }
        public long Deaths { get; }
        public ReadOnlyCollection<int> SpeciesCounts { get; }
    }

    public sealed class EcologyEconomySnapshot
    {
        internal EcologyEconomySnapshot(WorldState world)
        {
            EcologyState ecology = world.Ecology;
            var animals = new List<AnimalSnapshot>();
            foreach (AnimalState animal in ecology.Animals)
            {
                if (animal.Health > 0 && animal.Region == -1 && animal.Position.IsValid)
                {
                    animals.Add(new AnimalSnapshot(animal));
                }
            }
            Animals = animals.AsReadOnly();
            var species = new List<EcologySpeciesSnapshot>();
            for (int i = 0; i <= (int)AnimalSpecies.Raven; i++)
            {
                species.Add(new EcologySpeciesSnapshot((AnimalSpecies)i, Animals));
            }
            Species = species.AsReadOnly();
            Rabbits = Species[(int)AnimalSpecies.Rabbit].Count;
            Boars = Species[(int)AnimalSpecies.Boar].Count;
            Deer = Species[(int)AnimalSpecies.Deer].Count;
            Voles = Species[(int)AnimalSpecies.Vole].Count;
            Foxes = Species[(int)AnimalSpecies.Fox].Count;
            Wolves = Species[(int)AnimalSpecies.Wolf].Count;
            Ravens = Species[(int)AnimalSpecies.Raven].Count;
            var carcasses = new List<CarcassSnapshot>();
            foreach (CarcassState carcass in ecology.Carcasses)
            {
                if (carcass.Region == -1 && carcass.Position.IsValid) { carcasses.Add(new CarcassSnapshot(carcass)); }
            }
            Carcasses = carcasses.AsReadOnly();
            var habitats = new List<HabitatSnapshot>();
            foreach (HabitatPatchState patch in ecology.Habitats)
            {
                var habitat = new HabitatSnapshot(patch);
                habitats.Add(habitat);
                if (habitat.Region != -1) { continue; }
                LocalHerbaceousBiomass += habitat.Herbaceous.Biomass;
                LocalShrubBiomass += habitat.Shrubs.Biomass;
                LocalTreeBiomass += habitat.Trees.Biomass;
                LocalWater += habitat.Water;
                LocalSnowWater += habitat.SnowWater;
                LocalMineral += habitat.MineralRemaining;
                LocalLitter += habitat.Litter;
            }
            Habitats = habitats.AsReadOnly();
            var groups = new List<AnimalGroupSnapshot>();
            foreach (AnimalGroupState group in ecology.Groups) { groups.Add(new AnimalGroupSnapshot(group, ecology)); }
            Groups = groups.AsReadOnly();
            var regions = new List<EcologyRegionSnapshot>();
            foreach (EcologyRegionState region in ecology.Regions.Regions) { regions.Add(new EcologyRegionSnapshot(region, ecology)); }
            Regions = regions.AsReadOnly();
            RegionsEnabled = ecology.Regions.Enabled;
            Departures = ecology.Regions.Departures;
            Returns = ecology.Regions.Returns;
            Births = ecology.Births;
            NaturalDeaths = ecology.NaturalDeaths;
            HuntKills = ecology.HuntKills;
            PredationDeaths = ecology.PredationDeaths;
            var deaths = new long[(int)AnimalDeathCause.Injury + 1];
            for (int i = 0; i < deaths.Length; i++) { deaths[i] = ecology.Balance.Deaths((AnimalDeathCause)i); }
            DeathCounts = Array.AsReadOnly(deaths);
            // Conservation is across local AND external habitats/residents; migration is a transfer.
            Biomass = EcologyBalance.CurrentBiomass(ecology);
            Water = EcologyBalance.CurrentWater(ecology);
            BiomassError = ecology.Balance.BiomassError(ecology);
            WaterError = ecology.Balance.WaterError(ecology);
            MineralError = ecology.Balance.MineralError(ecology);
            var nodeConversions = new long[(int)ItemKind.Count];
            var carcassConversions = new long[(int)ItemKind.Count];
            var materialTotals = new long[(int)ItemKind.Count];
            long convertedBiomass = 0;
            for (int i = 0; i < nodeConversions.Length; i++)
            {
                ItemKind item = (ItemKind)i;
                nodeConversions[i] = ecology.Balance.NodeProduced(item);
                carcassConversions[i] = ecology.Balance.CarcassProduced(item);
                materialTotals[i] = MaterialBalance.Count(world, item);
                convertedBiomass += (nodeConversions[i] + carcassConversions[i]) * EcologyBalance.BiomassPerItem(item);
            }
            NodeConversions = Array.AsReadOnly(nodeConversions);
            CarcassConversions = Array.AsReadOnly(carcassConversions);
            MaterialTotals = Array.AsReadOnly(materialTotals);
            BiomassConversionError = convertedBiomass - ecology.Balance.MaterialBiomassProduced;
            WaterConversionError = nodeConversions[(int)ItemKind.RawWater] * 100 - ecology.Balance.MaterialWaterProduced;
            MineralConversionError = nodeConversions[(int)ItemKind.Stone] * 100 - ecology.Balance.MineralProduced;
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
        public ReadOnlyCollection<CarcassSnapshot> Carcasses { get; }
        public ReadOnlyCollection<HabitatSnapshot> Habitats { get; }
        public ReadOnlyCollection<AnimalGroupSnapshot> Groups { get; }
        public ReadOnlyCollection<EcologySpeciesSnapshot> Species { get; }
        public ReadOnlyCollection<EcologyRegionSnapshot> Regions { get; }
        public bool RegionsEnabled { get; }
        public long Departures { get; }
        public long Returns { get; }
        public int Rabbits { get; }
        public int Boars { get; }
        public int Deer { get; }
        public int Voles { get; }
        public int Foxes { get; }
        public int Wolves { get; }
        public int Ravens { get; }
        public int Births { get; }
        public int NaturalDeaths { get; }
        public int HuntKills { get; }
        public int PredationDeaths { get; }
        public ReadOnlyCollection<long> DeathCounts { get; }
        public long LocalHerbaceousBiomass { get; }
        public long LocalShrubBiomass { get; }
        public long LocalTreeBiomass { get; }
        public long LocalWater { get; }
        public long LocalSnowWater { get; }
        public long LocalMineral { get; }
        public long LocalLitter { get; }
        public long Biomass { get; }
        public long Water { get; }
        public long BiomassError { get; }
        public long WaterError { get; }
        public long MineralError { get; }
        public ReadOnlyCollection<long> NodeConversions { get; }
        public ReadOnlyCollection<long> CarcassConversions { get; }
        public ReadOnlyCollection<long> MaterialTotals { get; }
        public long BiomassConversionError { get; }
        public long WaterConversionError { get; }
        public long MineralConversionError { get; }
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
