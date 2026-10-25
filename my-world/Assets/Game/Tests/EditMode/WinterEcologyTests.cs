using System.Collections.Generic;
using System.Linq;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class WinterEcologyTests
    {
        private static WorldState World() => WorldFactory.Create(new WorldConfigData(42, 32, 32, 0, GameCalendar.WinterStartTick, 2, new[] { 1 }));
        [TestCase(AnimalSpecies.Rabbit)] [TestCase(AnimalSpecies.Boar)] [TestCase(AnimalSpecies.Deer)]
        [TestCase(AnimalSpecies.Vole)] [TestCase(AnimalSpecies.Fox)] [TestCase(AnimalSpecies.Wolf)] [TestCase(AnimalSpecies.Raven)]
        public void ForageQueryAndConsumptionAgree_AndDebitExactlyOneBiomassPool(AnimalSpecies species)
        {
            var w = World(); var patch = w.Ecology.Habitats.First(p => p.LandCells > 0);
            patch.SnowDepth = 5000;
            long before = patch.PlantBiomass; long available = patch.AvailablePlantFood(species, Season.Winter);
            int consumed = patch.ConsumePlantFood(species, Season.Winter, int.MaxValue);
            Assert.That(consumed, Is.EqualTo(available)); Assert.That(before - patch.PlantBiomass, Is.EqualTo(consumed));
            Assert.That(patch.Herbaceous.Biomass, Is.GreaterThanOrEqualTo(0)); Assert.That(patch.Shrubs.Biomass, Is.GreaterThanOrEqualTo(0));
        }
        [Test]
        public void EmptyEdibleShares_CannotBeReplacedByWholeTreeOrShrubMass()
        {
            var w = World(); var p = w.Ecology.Habitats.First(h => h.LandCells > 0);
            p.FruitFood = p.SeedFood = p.RootFood = p.BrowseFood = p.TwigFood = 0; p.SnowDepth = 20000;
            Assert.That(p.PlantBiomass, Is.GreaterThan(0));
            foreach (var species in new[] { AnimalSpecies.Boar, AnimalSpecies.Deer, AnimalSpecies.Raven, AnimalSpecies.Rabbit })
            { Assert.That(p.AvailablePlantFood(species, Season.Winter), Is.Zero); }
        }
        [Test]
        public void WinterNodeRegeneration_CannotCreateFruitFromBareShrubBiomass()
        {
            var w = World();
            foreach (var patch in w.Ecology.Habitats) { patch.FruitFood = 0; patch.SnowDepth = 0; }
            foreach (var node in w.Resources.Nodes) { if (node.Item == ItemKind.Food) { node.Amount = 0; } }
            w.Materials.Initialize(w);
            for (int i = 0; i < 1000; i++) { w.Ecology.UpdateResources(w); w.Tick++; }
            Assert.That(w.Resources.Nodes.Where(n => n.Item == ItemKind.Food).Sum(n => n.Amount), Is.Zero);
        }
        [Test]
        public void WinterBreeding_IsSpeciesAndDaySpecific_WithSpringBirthsForLargeMammals()
        {
            var w = World();
            foreach (var animal in w.Ecology.Animals)
            {
                if (animal.Species == AnimalSpecies.Rabbit || animal.Species == AnimalSpecies.Deer || animal.Species == AnimalSpecies.Fox || animal.Species == AnimalSpecies.Wolf || animal.Species == AnimalSpecies.Raven)
                { Assert.That(WinterEcologyRules.CanBreed(w, animal), Is.False); }
                if (animal.Species == AnimalSpecies.Boar) { Assert.That(WinterEcologyRules.CanBreed(w, animal), Is.True); Assert.That(animal.Definition.GestationTicks, Is.GreaterThan(28 * 1440)); }
            }
            w.Tick += 20 * 1440;
            foreach (var animal in w.Ecology.Animals.Where(a => a.Species == AnimalSpecies.Fox || a.Species == AnimalSpecies.Wolf))
            { Assert.That(WinterEcologyRules.CanBreed(w, animal), Is.True); }
        }
        [Test]
        public void DeepSnow_SlowsWalkingAndEscapeWithTheSameRule_ButNotSubniveanVoles()
        {
            var w = World();
            foreach (var animal in w.Ecology.Animals)
            {
                w.Ecology.FindHabitat(animal.HabitatId).SnowDepth = 10000;
                int factor = animal.Species == AnimalSpecies.Vole || animal.Species == AnimalSpecies.Raven ? 1 : 2;
                Assert.That(WinterEcologyRules.MoveInterval(w, animal, 1), Is.EqualTo(factor));
                Assert.That(WinterEcologyRules.MoveInterval(w, animal, 10), Is.EqualTo(10 * factor));
            }
        }
        [Test]
        public void WinterFoodShares_AreSavedAndHashed()
        {
            var w = World(); var p = w.Ecology.Habitats.First(h => h.FruitFood > 0);
            ulong hash = StateHasher.Compute(w); p.FruitFood--;
            Assert.That(StateHasher.Compute(w), Is.Not.EqualTo(hash));
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(w)); Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(w)));
        }

        [Test]
        public void RemovingParentBiomass_RemovesItsEdibleSharesPermanently()
        {
            var w = World(); var p = w.Ecology.Habitats.First(h => h.Trees.Biomass > 0);
            p.RemoveBiomass(VegetationKind.Tree, p.Trees.Biomass);
            p.RemoveBiomass(VegetationKind.Shrub, p.Shrubs.Biomass);
            p.RemoveBiomass(VegetationKind.Herbaceous, p.Herbaceous.Biomass);
            Assert.That(p.FruitFood + p.SeedFood + p.RootFood + p.TwigFood + p.BrowseFood, Is.Zero);
            p.Shrubs.Biomass = 1000; p.Trees.Biomass = 1000;
            Assert.That(p.AvailablePlantFood(AnimalSpecies.Boar, Season.Winter), Is.Zero);
            Assert.That(p.AvailablePlantFood(AnimalSpecies.Deer, Season.Winter), Is.Zero);
        }

        [Test]
        public void WinterGestationSnapshot_ResumesEveryTickWithoutSpawningReplacementPrey()
        {
            var w = World(); var mother = w.Ecology.Animals.First(a => a.Species == AnimalSpecies.Boar && a.Sex == AnimalSex.Female);
            var father = w.Ecology.Animals.First(a => a.Species == AnimalSpecies.Boar && a.Sex == AnimalSex.Male);
            mother.PregnancyFatherId = father.Id; mother.PregnancyLitter = 1; mother.PregnancyTicks = 60;
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(w)); var engine = new SimulationEngine();
            int births = w.Ecology.Births;
            for (int i = 0; i < 360; i++)
            { Assert.That(engine.Tick(restored, null).StateHash, Is.EqualTo(engine.Tick(w, null).StateHash), "minute " + i); }
            Assert.That(w.Ecology.Births, Is.EqualTo(births));
        }

        [Test]
        public void VoleWinterBreeding_RequiresSnowShelterAndAccessibleFood()
        {
            var w = World(); var vole = w.Ecology.Animals.First(a => a.Species == AnimalSpecies.Vole);
            var p = w.Ecology.FindHabitat(vole.HabitatId);
            p.Herbaceous.Biomass = p.Herbaceous.Capacity; p.RootFood = p.Herbaceous.Biomass / 3;
            p.Shrubs.Biomass = p.Shrubs.Capacity; p.Trees.Biomass = p.Trees.Capacity;
            p.SnowDepth = 2000; vole.CoreTemperature = 3800;
            Assert.That(WinterEcologyRules.CanBreed(w, vole), Is.True);
            p.SnowDepth = 0; Assert.That(WinterEcologyRules.CanBreed(w, vole), Is.False);
            p.SnowDepth = 2000; p.Herbaceous.Biomass = p.RootFood = p.SeedFood = 0;
            Assert.That(WinterEcologyRules.CanBreed(w, vole), Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void OnlyIntactBedProtection_StopsCropRaids_EvenWhenPlotIsReserved(bool protectedBed)
        {
            var w = World(); var plot = w.Resources.FarmPlots.First(p => p.BedId != 0);
            var bed = GrowingBedSystem.Bed(w, plot);
            bed.Progress = protectedBed ? SurvivalRules.ConstructionTicks : 0;
            plot.Crop = CropType.Spinach; plot.GrowthTicks = plot.GrowthRequired; plot.Moisture = 1000;
            plot.Damage = SurvivalRules.CropDeathDamage - 10; plot.ReservedBy = new EntityId(99999);
            foreach (var p in w.Ecology.Habitats)
            {
                p.Herbaceous.Biomass = p.Shrubs.Biomass = p.Trees.Biomass = 0;
                p.RootFood = p.FruitFood = p.SeedFood = p.BrowseFood = p.TwigFood = 0;
            }
            foreach (var node in w.Resources.Nodes) { if (node.Item == ItemKind.Food) { node.Amount = 0; } }
            var rabbit = w.Ecology.Animals.First(a => a.Species == AnimalSpecies.Rabbit);
            foreach (var animal in w.Ecology.Animals) { animal.LastNeedsTick = w.Tick; animal.NextDecisionTick = animal.NextMoveTick = long.MaxValue; }
            rabbit.Position = plot.Position; rabbit.HabitatId = w.Ecology.HabitatAt(plot.Position).Id;
            rabbit.Energy = 500; rabbit.LastNeedsTick = w.Tick - 60;
            EcologySystem.Update(w, new List<SimEvent>());
            Assert.That(plot.Crop, Is.EqualTo(protectedBed ? CropType.Spinach : CropType.None));
            Assert.That(plot.LostCrop, Is.EqualTo(!protectedBed));
        }
    }
}
