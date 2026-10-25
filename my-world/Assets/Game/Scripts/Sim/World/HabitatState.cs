using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public sealed class VegetationState
    {
        internal VegetationState(VegetationKind kind, int capacity, int biomass)
        {
            Kind = kind;
            Capacity = capacity;
            Biomass = biomass;
            SeedBank = capacity > 0 ? 1000 : 0;
        }

        public VegetationKind Kind { get; }
        public int Capacity { get; internal set; }
        public int Biomass { get; internal set; }
        public int SeedBank { get; internal set; }
        public long GrowthRemainder { get; internal set; }
    }

    public sealed class HabitatPatchState
    {
        internal HabitatPatchState(int id, int region, GridPos origin, int cells, int forestCells, ulong rng)
        {
            Id = id;
            Region = region;
            Origin = origin;
            LandCells = cells;
            Herbaceous = new VegetationState(VegetationKind.Herbaceous, cells * 120, cells * 80);
            Shrubs = new VegetationState(VegetationKind.Shrub, cells * 100, cells * 60);
            Trees = new VegetationState(VegetationKind.Tree, forestCells * 500 + cells * 20, forestCells * 350 + cells * 10);
            WaterCapacity = cells * 100;
            Water = cells * 65;
            SoilFertility = 7000;
            MineralRemaining = cells * 40;
            RngState = rng;
            FruitFood = Shrubs.Biomass / 5; SeedFood = Shrubs.Biomass / 20;
            RootFood = Herbaceous.Biomass / 3; BrowseFood = Shrubs.Biomass / 3; TwigFood = Trees.Biomass / 20;
        }

        public int Id { get; }
        public int Region { get; }
        public GridPos Origin { get; }
        public int LandCells { get; }
        public VegetationState Herbaceous { get; }
        public VegetationState Shrubs { get; }
        public VegetationState Trees { get; }
        // Edible shares of existing vegetation, never an additional biomass pool.
        public int FruitFood { get; internal set; }
        public int SeedFood { get; internal set; }
        public int RootFood { get; internal set; }
        public int BrowseFood { get; internal set; }
        public int TwigFood { get; internal set; }
        public int WaterCapacity { get; internal set; }
        public int Water { get; internal set; }
        public int SnowWater { get; internal set; }
        public int SnowDepth { get; internal set; }
        public int SoilFertility { get; internal set; }
        public long Litter { get; internal set; }
        public int MineralRemaining { get; internal set; }
        public ulong RngState { get; internal set; }
        public long LastUpdateTick { get; internal set; }
        public int Shelter => LandCells == 0 ? 0 : Math.Min(10000,
            (int)((long)(Shrubs.Biomass + Trees.Biomass) * 10000 / Math.Max(1, Shrubs.Capacity + Trees.Capacity)));
        public long PlantBiomass => (long)Herbaceous.Biomass + Shrubs.Biomass + Trees.Biomass;
        public long AvailablePlantFood(AnimalSpecies species, Season season) => PlantFood(species, season, int.MaxValue, false);
        internal int ConsumePlantFood(AnimalSpecies species, Season season, int wanted) => PlantFood(species, season, wanted, true);
        public int AvailableFruit => SnowDepth >= 8000 ? 0 : Math.Min(FruitFood, Shrubs.Biomass);
        internal int ConsumeFruit(int wanted)
        {
            int taken = Math.Min(wanted, AvailableFruit); FruitFood -= taken; Shrubs.Biomass -= taken; return taken;
        }
        private int PlantFood(AnimalSpecies species, Season season, int wanted, bool consume)
        {
            if (AnimalDefinition.For(species).IsPredator) { return 0; }
            int total = 0;
            int herbLeft = Herbaceous.Biomass, shrubLeft = Shrubs.Biomass, treeLeft = Trees.Biomass;
            bool buried = species == AnimalSpecies.Vole;
            int access = buried ? 10000 : Math.Max(0, 10000 - SnowDepth);
            bool winter = season == Season.Winter;
            if (species == AnimalSpecies.Rabbit || species == AnimalSpecies.Deer || buried)
            {
                int grass = Math.Max(0, Herbaceous.Biomass - RootFood);
                int taken = (int)Math.Min(wanted - total, (long)grass * access / 10000);
                if (consume) { Herbaceous.Biomass -= taken; } herbLeft -= taken; total += taken;
            }
            if (species == AnimalSpecies.Boar || buried)
            { total += TakeShare(Herbaceous, ref herbLeft, RootFood, access, wanted - total, consume, 0); }
            if (species == AnimalSpecies.Boar || species == AnimalSpecies.Raven || buried)
            { total += TakeShare(Shrubs, ref shrubLeft, SeedFood, buried ? 10000 : access, wanted - total, consume, 1); }
            if (species == AnimalSpecies.Boar || species == AnimalSpecies.Raven)
            { total += TakeShare(Shrubs, ref shrubLeft, FruitFood, access, wanted - total, consume, 2); }
            if (species == AnimalSpecies.Rabbit || species == AnimalSpecies.Deer)
            {
                total += TakeShare(Shrubs, ref shrubLeft, BrowseFood, 10000, wanted - total, consume, 3);
                if (winter || SnowDepth > 0) { total += TakeShare(Trees, ref treeLeft, TwigFood, 10000, wanted - total, consume, 4); }
            }
            return total;
        }
        private int TakeShare(VegetationState vegetation, ref int remaining, int share, int access, int wanted, bool consume, int kind)
        {
            int taken = (int)Math.Min(wanted, (long)Math.Min(share, remaining) * access / 10000);
            remaining -= taken;
            if (!consume) { return taken; }
            vegetation.Biomass -= taken;
            switch (kind)
            {
                case 0: RootFood -= taken; break;
                case 1: SeedFood -= taken; break;
                case 2: FruitFood -= taken; break;
                case 3: BrowseFood -= taken; break;
                case 4: TwigFood -= taken; break;
            }
            return taken;
        }
        // Logging, herb gathering and natural turnover remove the edible portion
        // with its parent biomass; later winter growth cannot revive spent fruit.
        internal void RemoveBiomass(VegetationKind kind, int amount)
        {
            var vegetation = Vegetation(kind);
            int before = vegetation.Biomass;
            int remaining = Math.Max(0, before - amount);
            if (before > 0)
            {
                if (kind == VegetationKind.Herbaceous) { RootFood = (int)((long)RootFood * remaining / before); }
                else if (kind == VegetationKind.Shrub)
                {
                    FruitFood = (int)((long)FruitFood * remaining / before);
                    SeedFood = (int)((long)SeedFood * remaining / before);
                    BrowseFood = (int)((long)BrowseFood * remaining / before);
                }
                else { TwigFood = (int)((long)TwigFood * remaining / before); }
            }
            vegetation.Biomass = remaining;
        }
        public int AvailableWater(int temperature)
        {
            return (temperature >= 0 ? Water : Shelter > 4000 ? Water / 4 : 0) + SnowWater / 2;
        }
        public VegetationState Vegetation(VegetationKind kind)
        {
            switch (kind)
            {
                case VegetationKind.Herbaceous: return Herbaceous;
                case VegetationKind.Shrub: return Shrubs;
                case VegetationKind.Tree: return Trees;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public sealed class AnimalGroupState
    {
        internal AnimalGroupState(int id, AnimalSpecies species, int region, int territory, EntityId leader, ulong rng)
        {
            Id = id;
            Species = species;
            Region = region;
            TerritoryHabitatId = territory;
            LeaderId = leader;
            RngState = rng;
        }

        public int Id { get; }
        public AnimalSpecies Species { get; }
        public int Region { get; internal set; }
        public int TerritoryHabitatId { get; internal set; }
        public EntityId LeaderId { get; internal set; }
        public int Dominance { get; internal set; }
        public int Disputes { get; internal set; }
        public ulong RngState { get; internal set; }
        public long LastCompetitionTick { get; internal set; }
    }

    public sealed class EcologyRegionState
    {
        internal EcologyRegionState(int id, GridPos exit, int patchId, int temperatureOffset)
        {
            Id = id;
            Exit = exit;
            HabitatId = patchId;
            TemperatureOffset = temperatureOffset;
        }

        public int Id { get; }
        public GridPos Exit { get; internal set; }
        public int HabitatId { get; }
        public int TemperatureOffset { get; }
        public long LastUpdateTick { get; internal set; }
        public long Immigration { get; internal set; }
        public long Emigration { get; internal set; }
        public long Births { get; internal set; }
        public long Deaths { get; internal set; }
    }

    // Disabled regions remain finite resident habitats: isolation stops crossings,
    // not their metabolism, and never enables population replacement.
    public sealed class EcologyRegionsState
    {
        internal readonly List<EcologyRegionState> MutableRegions = new List<EcologyRegionState>();
        internal EcologyRegionsState(ulong seed)
        {
            Regions = MutableRegions.AsReadOnly();
            RngState = seed;
        }

        public bool Enabled { get; set; } = true;
        public IReadOnlyList<EcologyRegionState> Regions { get; }
        public ulong RngState { get; internal set; }
        public long Departures { get; internal set; }
        public long Returns { get; internal set; }
        public long LastUpdateTick { get; internal set; }
    }
}
