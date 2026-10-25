using System;

namespace MyAiWorld.Sim.World
{
    // Transfers (feeding, birth, growth, death, migration) stay inside this ledger.
    // Only explicitly named sources and sinks change total ecological biomass.
    public sealed class EcologyBalance
    {
        public long InitialBiomass { get; internal set; }
        public long IntroducedBiomass { get; internal set; }
        public long Photosynthesis { get; internal set; }
        public long MaterialFoodConsumed { get; internal set; }
        public long Respiration { get; internal set; }
        public long Decomposed { get; internal set; }
        public long MaterialBiomassProduced { get; internal set; }
        public long PlantEaten { get; internal set; }
        public long CarrionEaten { get; internal set; }
        public long NursingTransferred { get; internal set; }
        public long BirthTransferred { get; internal set; }
        public long GrowthTransferred { get; internal set; }
        public long InitialWater { get; internal set; }
        public long IntroducedWater { get; internal set; }
        public long Precipitation { get; internal set; }
        public long WaterUsed { get; internal set; }
        public long Evaporation { get; internal set; }
        public long Runoff { get; internal set; }
        public long MaterialWaterProduced { get; internal set; }
        public long InitialMineral { get; internal set; }
        public long MineralProduced { get; internal set; }
        public long BirthsBlockedByCapacity { get; internal set; }
        public long PregnanciesLost { get; internal set; }
        internal readonly long[] NodeConversions = new long[(int)ItemKind.Count];
        internal readonly long[] CarcassConversions = new long[(int)ItemKind.Count];
        internal readonly long[] DeathCounts = new long[(int)AnimalDeathCause.Injury + 1];

        public long NodeProduced(ItemKind item) => NodeConversions[(int)item];
        public long CarcassProduced(ItemKind item) => CarcassConversions[(int)item];
        public long Deaths(AnimalDeathCause cause) => DeathCounts[(int)cause];
        public long BiomassError(EcologyState ecology) => CurrentBiomass(ecology) -
            (InitialBiomass + IntroducedBiomass + Photosynthesis + MaterialFoodConsumed - Respiration - Decomposed - MaterialBiomassProduced);
        public long WaterError(EcologyState ecology) => CurrentWater(ecology) -
            (InitialWater + IntroducedWater + Precipitation - WaterUsed - Evaporation - Runoff - MaterialWaterProduced);
        public long MineralError(EcologyState ecology)
        {
            long current = 0;
            foreach (HabitatPatchState patch in ecology.Habitats)
            {
                current += patch.MineralRemaining;
            }
            return current - (InitialMineral - MineralProduced);
        }

        public static long CurrentBiomass(EcologyState ecology)
        {
            long total = 0;
            foreach (HabitatPatchState patch in ecology.Habitats)
            {
                total += patch.PlantBiomass + patch.Litter;
            }
            foreach (AnimalState animal in ecology.Animals)
            {
                total += animal.BodyMass + animal.Energy;
            }
            foreach (CarcassState carcass in ecology.Carcasses)
            {
                total += carcass.Biomass;
            }
            return total;
        }

        public static long CurrentWater(EcologyState ecology)
        {
            long total = 0;
            foreach (HabitatPatchState patch in ecology.Habitats)
            {
                total += (long)patch.Water + patch.SnowWater;
            }
            foreach (AnimalState animal in ecology.Animals)
            {
                total += animal.Hydration;
            }
            return total;
        }

        public static int BiomassPerItem(ItemKind item)
        {
            switch (item)
            {
                case ItemKind.Food:
                case ItemKind.Wood:
                case ItemKind.RawMeat:
                case ItemKind.Hide:
                case ItemKind.Fur: return 100;
                case ItemKind.Feather: return 25;
                case ItemKind.Fiber: return 50;
                case ItemKind.Herb: return 25;
                default: return 0;
            }
        }
    }
}
