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
        }

        public int Id { get; }
        public int Region { get; }
        public GridPos Origin { get; }
        public int LandCells { get; }
        public VegetationState Herbaceous { get; }
        public VegetationState Shrubs { get; }
        public VegetationState Trees { get; }
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
        public long AvailablePlantFood(AnimalSpecies species, Season season)
        {
            if (species == AnimalSpecies.Vole)
            {
                return Herbaceous.Biomass;
            }
            if (species == AnimalSpecies.Raven)
            {
                return Math.Min(Shrubs.Biomass, Shrubs.SeedBank / 10);
            }
            if (AnimalDefinition.For(species).IsPredator)
            {
                return 0;
            }
            long herb = (long)Herbaceous.Biomass * Math.Max(500, 10000 - SnowDepth) / 10000;
            return herb + Shrubs.Biomass + (species == AnimalSpecies.Deer && (season == Season.Winter || SnowDepth > 0) ? Trees.Biomass / 2 : 0);
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
