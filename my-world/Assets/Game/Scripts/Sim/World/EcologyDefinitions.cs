using System;

namespace MyAiWorld.Sim.World
{
    public enum AnimalSpecies
    {
        Rabbit = 0,
        Boar = 1,
        Deer = 2,
        Vole = 3,
        Fox = 4,
        Wolf = 5,
        Raven = 6
    }

    public enum AnimalSex
    {
        Female,
        Male
    }

    public enum AnimalLifeStage
    {
        Infant,
        Juvenile,
        Adult,
        Elder
    }

    public enum AnimalBehavior
    {
        Rest,
        Forage,
        Drink,
        Shelter,
        Flee,
        Hunt,
        Defend,
        Scavenge,
        Nurse,
        Migrate,
        Dead
    }

    public enum AnimalDeathCause
    {
        None,
        Age,
        Starvation,
        Dehydration,
        Exposure,
        Predation,
        Hunt,
        Injury
    }

    public enum VegetationKind
    {
        Herbaceous,
        Shrub,
        Tree
    }

    // One game year is 112 days. Energy, body mass and vegetation use the same
    // abstract biomass unit; material units are converted explicitly at harvest.
    public sealed class AnimalDefinition
    {
        private const int Day = GameCalendar.TicksPerDay;
        private const int Year = GameCalendar.DaysPerYear * Day;
        public static readonly AnimalDefinition Rabbit = new AnimalDefinition(30, 3, 8, 250, 3 * Year, 14 * Day, 9 * Day, 7 * Day, 3, 700, 120, 35, 15, -1000, 18, 6, 3, 4, 3, 0, 1);
        public static readonly AnimalDefinition Boar = new AnimalDefinition(90, 10, 35, 800, 9 * Year, 56 * Day, 35 * Day, 14 * Day, 3, 2000, 260, 95, 30, -1500, 25, 10, 5, 7, 8, 0, 2);
        public static readonly AnimalDefinition Deer = new AnimalDefinition(65, 12, 12, 450, 12 * Year, 70 * Day, 70 * Day, 19 * Day, 1, 2500, 350, 85, 35, -2000, 20, 12, 4, 10, 4, 0, 3);
        public static readonly AnimalDefinition Vole = new AnimalDefinition(12, 1, 1, 60, 2 * Year, 7 * Day, 3 * Day, 4 * Day, 4, 250, 55, 18, 8, -500, 15, 3, 2, 3, 11, 0, 0);
        public static readonly AnimalDefinition Fox = new AnimalDefinition(45, 4, 20, 450, 6 * Year, 42 * Day, 16 * Day, 12 * Day, 2, 1100, 200, 60, 20, -2500, 12, 8, 4, 8, 8, 0x09, 1);
        public static readonly AnimalDefinition Wolf = new AnimalDefinition(110, 7, 60, 1100, 10 * Year, 70 * Day, 19 * Day, 18 * Day, 2, 1800, 300, 100, 30, -3000, 14, 12, 6, 11, 8, 0x07, 2);
        public static readonly AnimalDefinition Raven = new AnimalDefinition(22, 2, 4, 120, 15 * Year, 56 * Day, 6 * Day, 10 * Day, 2, 550, 100, 25, 10, -1500, 40, 10, 3, 8, 3, 0, 0);

        private AnimalDefinition(int health, int foodYield, int risk, int injury, int lifespan,
            int maturity, int gestation, int nursing, int litter, int mass, int newbornMass,
            int metabolism, int waterUse, int coldTolerance, int moveTicks, int sight,
            int groupSize, int territoryRadius, int breedingMask, int preyMask, int hideYield)
        {
            Health = health;
            FoodYield = foodYield;
            InjuryRisk = risk;
            InjuryDamage = injury;
            Lifespan = lifespan;
            MaturityTicks = maturity;
            GestationTicks = gestation;
            NursingTicks = nursing;
            LitterSize = litter;
            AdultMass = mass;
            NewbornMass = newbornMass;
            MetabolismPerHour = metabolism;
            WaterPerHour = waterUse;
            ColdTolerance = coldTolerance;
            MoveTicks = moveTicks;
            Sight = sight;
            GroupSize = groupSize;
            TerritoryRadius = territoryRadius;
            BreedingSeasonMask = breedingMask;
            PreyMask = preyMask;
            HideYield = hideYield;
        }

        public int Health { get; }
        public int FoodYield { get; }
        public int InjuryRisk { get; }
        public int InjuryDamage { get; }
        public int Lifespan { get; }
        public int MaturityTicks { get; }
        public int GestationTicks { get; }
        public int NursingTicks { get; }
        public int LitterSize { get; }
        public int AdultMass { get; }
        public int NewbornMass { get; }
        public int MetabolismPerHour { get; }
        public int WaterPerHour { get; }
        public int ColdTolerance { get; }
        public int MoveTicks { get; }
        public int Sight { get; }
        public int GroupSize { get; }
        public int TerritoryRadius { get; }
        public int BreedingSeasonMask { get; }
        public int PreyMask { get; }
        public int HideYield { get; }
        public int EnergyCapacity => AdultMass * 3;
        public bool IsPredator => PreyMask != 0;
        public bool EatsCarrion => IsPredator || this == Raven || this == Boar;
        public bool CanPreyOn(AnimalSpecies species) => (PreyMask & (1 << (int)species)) != 0;
        public bool BreedsIn(Season season) => (BreedingSeasonMask & (1 << (int)season)) != 0;

        public static AnimalDefinition For(AnimalSpecies species)
        {
            switch (species)
            {
                case AnimalSpecies.Rabbit: return Rabbit;
                case AnimalSpecies.Boar: return Boar;
                case AnimalSpecies.Deer: return Deer;
                case AnimalSpecies.Vole: return Vole;
                case AnimalSpecies.Fox: return Fox;
                case AnimalSpecies.Wolf: return Wolf;
                case AnimalSpecies.Raven: return Raven;
                default: throw new ArgumentOutOfRangeException(nameof(species));
            }
        }
    }
}
