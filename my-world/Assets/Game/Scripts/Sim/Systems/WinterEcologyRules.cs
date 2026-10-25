using System;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class WinterEcologyRules
    {
        public static bool CanBreed(WorldState world, AnimalState animal)
        {
            var date = world.Date;
            switch (animal.Species)
            {
                case AnimalSpecies.Deer: return date.Season == Season.Autumn && date.DayOfSeason <= 14;
                case AnimalSpecies.Boar: return date.Season == Season.Winter && date.DayOfSeason <= 20;
                case AnimalSpecies.Fox: return date.Season == Season.Winter && date.DayOfSeason >= 15;
                case AnimalSpecies.Wolf: return date.Season == Season.Winter && date.DayOfSeason >= 10 && date.DayOfSeason <= 24;
                case AnimalSpecies.Vole:
                    if (date.Season != Season.Winter) { return date.Season == Season.Spring || date.Season == Season.Summer; }
                    var patch = world.Ecology.FindHabitat(animal.HabitatId);
                    return patch != null && patch.SnowDepth >= 1000 && patch.Shelter >= 3000 && animal.CoreTemperature >= 3500 &&
                        patch.AvailablePlantFood(animal.Species, date.Season) >= animal.Definition.EnergyCapacity * 4;
                default: return date.Season == Season.Spring || date.Season == Season.Summer;
            }
        }
        public static int SnowCost(WorldState world, AnimalState animal)
        {
            if (animal.Species == AnimalSpecies.Vole || animal.Species == AnimalSpecies.Raven || animal.Behavior == AnimalBehavior.Shelter || animal.Behavior == AnimalBehavior.Rest) { return 0; }
            var patch = world.Ecology.FindHabitat(animal.HabitatId);
            return patch == null ? 0 : Math.Min(animal.Definition.MetabolismPerHour, patch.SnowDepth / 300);
        }
        public static int MoveInterval(WorldState world, AnimalState animal, int interval)
        {
            if (animal.Species == AnimalSpecies.Vole || animal.Species == AnimalSpecies.Raven) { return interval; }
            var patch = world.Ecology.FindHabitat(animal.HabitatId);
            return interval + (patch == null ? 0 : interval * Math.Min(10000, patch.SnowDepth) / 10000);
        }
    }
}
