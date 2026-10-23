using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.UI;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed class EcologyObservationTests
    {
        // Reflection is test-fixture setup only; observation has no authority mutation API.
        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private static WorldState CreateWorld(int seed = 42)
        {
            return WorldFactory.Create(new WorldConfigData(seed, 32, 32, 2, 360, 2, new[] { 1, 2, 4, 8 }, 0));
        }

        private static WorldReadModel Observe(WorldState world)
        {
            var model = new WorldReadModel(world.Config);
            model.Initialize(world, new List<SimEvent>());
            return model;
        }

        private static void Depart(WorldState world, AnimalState animal, EcologyRegionState region)
        {
            typeof(MigrationSystem).GetMethod("Depart", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { world, animal, region });
        }

        private static CarcassState Kill(WorldState world, AnimalState animal, AnimalDeathCause cause, EntityId source)
        {
            Set(animal, "DeathCause", cause);
            Set(animal, "LastDamageSourceId", source);
            Set(animal, "Health", 0);
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            return world.Ecology.FindCarcass(animal.Id.Value);
        }

        [Test]
        public void Snapshot_AllDtoCollectionsAndNestedArraysAreReadOnly_QueriesPreserveHash()
        {
            WorldState world = CreateWorld();
            AnimalState animal = world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Fox);
            animal.MigrationPath.Add(animal.Position);
            Kill(world, world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Rabbit), AnimalDeathCause.Predation, animal.Id);
            ulong hash = StateHasher.Compute(world);
            WorldReadModel model = Observe(world);
            EcologyEconomySnapshot snapshot = model.EcologyEconomy;
            foreach (Type type in new[]
            {
                typeof(AnimalSnapshot), typeof(CarcassSnapshot), typeof(VegetationSnapshot), typeof(HabitatSnapshot),
                typeof(AnimalGroupSnapshot), typeof(EcologySpeciesSnapshot), typeof(EcologyRegionSnapshot), typeof(EcologyEconomySnapshot)
            })
            {
                Assert.That(type.IsSealed, Is.True, type.Name);
                foreach (PropertyInfo property in type.GetProperties())
                {
                    Assert.That(property.GetSetMethod(true), Is.Null, type.Name + "." + property.Name);
                    Assert.That(property.PropertyType.IsArray, Is.False, "Do not expose writable arrays: " + property.Name);
                }
            }
            AssertReadOnly(snapshot);
            foreach (var dto in snapshot.Animals) { AssertReadOnly(dto); }
            foreach (var dto in snapshot.Carcasses) { AssertReadOnly(dto); }
            foreach (var dto in snapshot.Groups) { AssertReadOnly(dto); }
            foreach (var dto in snapshot.Species) { AssertReadOnly(dto); }
            foreach (var dto in snapshot.Regions) { AssertReadOnly(dto); }
            model.GetDetails(world.Entities.Npcs[0].Id);
            model.Refresh(world, null, 0);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }

        private static void AssertReadOnly(object dto)
        {
            foreach (PropertyInfo property in dto.GetType().GetProperties())
            {
                if (!(property.GetValue(dto) is IList list)) { continue; }
                Assert.That(list.IsReadOnly, Is.True, property.Name);
                Assert.Throws<NotSupportedException>(() => list.Clear(), property.Name);
                if (list.Count > 0) { Assert.Throws<NotSupportedException>(() => list[0] = list[0], property.Name); }
            }
        }

        [Test]
        public void Snapshot_RetainedAnimalCarcassHabitatGroupsAndCountersDoNotFollowState()
        {
            WorldState world = CreateWorld();
            AnimalState animal = world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Fox);
            animal.MigrationPath.Add(animal.Position);
            Set(animal, "Energy", 321);
            Set(animal, "Hydration", 1234);
            Set(animal, "CoreTemperature", 3678);
            Set(animal, "MotherId", new EntityId(9001));
            Set(animal, "FatherId", new EntityId(9002));
            Set(animal, "MateId", new EntityId(9003));
            Set(animal, "PregnancyFatherId", new EntityId(9003));
            Set(animal, "PregnancyTicks", 120);
            Set(animal, "PregnancyLitter", 2);
            Set(animal, "MigrationDestination", 0);
            CarcassState carcass = Kill(world, world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Rabbit), AnimalDeathCause.Predation, animal.Id);
            Assert.That(carcass.Inventory.TryAdd(ItemKind.Arrow, 1), Is.True);
            WorldReadModel model = Observe(world);
            EcologyEconomySnapshot before = model.EcologyEconomy;
            AnimalSnapshot copied = before.Animals.Single(a => a.Id == animal.Id);
            CarcassSnapshot dead = before.Carcasses.Single(c => c.Id == carcass.Id);
            HabitatSnapshot habitat = before.Habitats.Single(h => h.Id == animal.HabitatId);
            AnimalGroupSnapshot group = before.Groups.Single(g => g.Id == animal.GroupId);
            int oldBiomass = habitat.Herbaceous.Biomass;
            int oldMembers = group.Members.Count;
            Set(animal, "Energy", 12); Set(animal, "Hydration", 20); Set(animal, "CoreTemperature", 3500);
            Set(animal, "PregnancyTicks", 0); Set(animal, "AgeTicks", 0);
            Set(animal, "Behavior", AnimalBehavior.Migrate); animal.MigrationPath.Clear();
            Set(carcass, "MeatRemaining", 0); Set(carcass, "Freshness", 100);
            carcass.Inventory.TryRemove(ItemKind.Arrow, 1);
            Set(world.Ecology.FindHabitat(animal.HabitatId).Herbaceous, "Biomass", 0);
            world.Ecology.Spawn(world, animal.Species, animal.Position, 0);
            model.Refresh(world, null, 0);
            Assert.That(copied.Energy, Is.EqualTo(321)); Assert.That(copied.EnergyCapacity, Is.EqualTo(animal.Definition.EnergyCapacity));
            Assert.That(copied.Hydration, Is.EqualTo(1234)); Assert.That(copied.CoreTemperature, Is.EqualTo(3678));
            Assert.That(copied.MotherId.Value, Is.EqualTo(9001)); Assert.That(copied.FatherId.Value, Is.EqualTo(9002));
            Assert.That(copied.MateId.Value, Is.EqualTo(9003)); Assert.That(copied.PregnancyFatherId.Value, Is.EqualTo(9003));
            Assert.That(copied.PregnancyTicks, Is.EqualTo(120)); Assert.That(copied.PregnancyLitter, Is.EqualTo(2));
            Assert.That(copied.MigrationDestination, Is.Zero); Assert.That(copied.MigrationPath.Count, Is.EqualTo(1));
            Assert.That(copied.AgeTicks, Is.GreaterThan(0)); Assert.That(copied.Region, Is.EqualTo(-1));
            Assert.That(dead.Freshness, Is.EqualTo(10000)); Assert.That(dead.MeatRemaining, Is.GreaterThan(0));
            Assert.That(dead.Items[(int)ItemKind.Arrow], Is.EqualTo(1)); Assert.That(dead.SourceId, Is.EqualTo(animal.Id));
            Assert.That(dead.DeathCause, Is.EqualTo(AnimalDeathCause.Predation));
            Assert.That(habitat.Herbaceous.Biomass, Is.EqualTo(oldBiomass)); Assert.That(group.Members.Count, Is.EqualTo(oldMembers));
            Assert.That(before.Foxes + 1, Is.EqualTo(model.EcologyEconomy.Foxes));
            Assert.That(before, Is.Not.SameAs(model.EcologyEconomy));
        }

        [Test]
        public void Snapshot_ExternalResidentsAndCarcassesAreNotMapAnimals_RealIdsAndCrossingsRemain()
        {
            WorldState world = CreateWorld();
            AnimalState resident = world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Deer);
            EntityId original = resident.Id;
            GridPos originalCell = resident.Position;
            EcologyRegionState region = world.Ecology.Regions.Regions[0];
            int initial = world.Ecology.Count(resident.Species);
            WorldReadModel model = Observe(world);
            EcologyEconomySnapshot before = model.EcologyEconomy;
            Depart(world, resident, region);
            model.Refresh(world, null, 0);
            EcologyEconomySnapshot outside = model.EcologyEconomy;
            Assert.That(resident.Position, Is.EqualTo(GridPos.Invalid));
            Assert.That(outside.Animals.Any(a => a.Id == original), Is.False);
            Assert.That(outside.Deer, Is.EqualTo(initial - 1));
            Assert.That(outside.Regions.Single(r => r.Id == region.Id).SpeciesCounts[(int)resident.Species], Is.EqualTo(1));
            Assert.That(outside.Departures, Is.EqualTo(before.Departures + 1));
            Assert.That(outside.Regions.Single(r => r.Id == region.Id).Immigration, Is.EqualTo(1));
            Assert.That(before.Animals.Single(a => a.Id == original).Cell, Is.EqualTo(originalCell));
            // Even a stale external cell must not become renderable through a query.
            Set(resident, "Position", originalCell); model.Refresh(world, null, 0);
            Assert.That(model.EcologyEconomy.Animals.Any(a => a.Id == original), Is.False);
            Set(resident, "Position", GridPos.Invalid);
            typeof(MigrationSystem).GetMethod("TryReturn", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { world, resident });
            Assert.That(resident.Region, Is.EqualTo(-1));
            model.Refresh(world, null, 0);
            Assert.That(model.EcologyEconomy.Animals.Single(a => a.Id == original).Cell, Is.EqualTo(region.Exit));
            Assert.That(model.EcologyEconomy.Returns, Is.EqualTo(before.Returns + 1));
            CarcassState local = Kill(world, resident, AnimalDeathCause.Age, EntityId.None);
            AnimalState external = world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Vole);
            Depart(world, external, region);
            Kill(world, external, AnimalDeathCause.Exposure, EntityId.None);
            model.Refresh(world, null, 0);
            Assert.That(model.EcologyEconomy.Carcasses.Select(c => c.Id), Is.EquivalentTo(new[] { local.Id }));
            Assert.That(model.EcologyEconomy.Animals.Any(a => a.Id == original || a.Id == external.Id), Is.False);
            Assert.That(outside.Regions.Single(r => r.Id == region.Id).SpeciesCounts[(int)resident.Species], Is.EqualTo(1), "Retained regional counts must be copied.");
        }

        [TestCase(42)]
        [TestCase(12345)]
        public void Snapshot_SevenSpeciesAgesResourceTotalsDeathsAndConversionErrorsMatchAuthority(int seed)
        {
            WorldState world = CreateWorld(seed);
            AnimalState prey = world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Rabbit);
            Kill(world, prey, AnimalDeathCause.Predation, world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Fox).Id);
            ulong hash = StateHasher.Compute(world);
            EcologyEconomySnapshot snapshot = Observe(world).EcologyEconomy;
            Assert.That(snapshot.Species.Count, Is.EqualTo(7));
            foreach (EcologySpeciesSnapshot species in snapshot.Species)
            {
                Assert.That(species.Count, Is.EqualTo(world.Ecology.Count(species.Species)));
                Assert.That(species.AgeCounts.Sum(), Is.EqualTo(species.Count));
                for (int i = 0; i < species.AgeCounts.Count; i++)
                {
                    Assert.That(species.AgeCounts[i], Is.EqualTo(world.Ecology.Animals.Count(a => a.Region == -1 && a.Health > 0 && a.Species == species.Species && a.LifeStage == (AnimalLifeStage)i)));
                }
                Assert.That(species.GroupCount, Is.EqualTo(world.Ecology.Animals.Where(a => a.Region == -1 && a.Health > 0 && a.Species == species.Species).Select(a => a.GroupId).Distinct().Count()));
            }
            Assert.That(snapshot.Animals.Count, Is.EqualTo(snapshot.Species.Sum(s => s.Count)));
            Assert.That(snapshot.PredationDeaths, Is.EqualTo(1));
            for (int i = 0; i < snapshot.DeathCounts.Count; i++) { Assert.That(snapshot.DeathCounts[i], Is.EqualTo(world.Ecology.Balance.Deaths((AnimalDeathCause)i))); }
            Assert.That(snapshot.Biomass, Is.EqualTo(EcologyBalance.CurrentBiomass(world.Ecology)));
            Assert.That(snapshot.Water, Is.EqualTo(EcologyBalance.CurrentWater(world.Ecology)));
            Assert.That(snapshot.LocalHerbaceousBiomass, Is.EqualTo(world.Ecology.Habitats.Where(h => h.Region == -1).Sum(h => (long)h.Herbaceous.Biomass)));
            Assert.That(snapshot.LocalShrubBiomass, Is.EqualTo(world.Ecology.Habitats.Where(h => h.Region == -1).Sum(h => (long)h.Shrubs.Biomass)));
            Assert.That(snapshot.LocalTreeBiomass, Is.EqualTo(world.Ecology.Habitats.Where(h => h.Region == -1).Sum(h => (long)h.Trees.Biomass)));
            Assert.That(snapshot.LocalWater, Is.EqualTo(world.Ecology.Habitats.Where(h => h.Region == -1).Sum(h => (long)h.Water)));
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(snapshot.MaterialTotals[i], Is.EqualTo(MaterialBalance.Count(world, (ItemKind)i)));
                Assert.That(snapshot.NodeConversions[i], Is.EqualTo(world.Ecology.Balance.NodeProduced((ItemKind)i)));
                Assert.That(snapshot.CarcassConversions[i], Is.EqualTo(world.Ecology.Balance.CarcassProduced((ItemKind)i)));
            }
            Assert.That(snapshot.BiomassError, Is.Zero); Assert.That(snapshot.WaterError, Is.Zero); Assert.That(snapshot.MineralError, Is.Zero);
            Assert.That(snapshot.BiomassConversionError, Is.Zero); Assert.That(snapshot.WaterConversionError, Is.Zero); Assert.That(snapshot.MineralConversionError, Is.Zero);
            Set(world.Ecology.Balance, "MaterialBiomassProduced", world.Ecology.Balance.MaterialBiomassProduced + 3);
            Set(world.Ecology.Balance, "MaterialWaterProduced", world.Ecology.Balance.MaterialWaterProduced + 5);
            Set(world.Ecology.Balance, "MineralProduced", world.Ecology.Balance.MineralProduced + 7);
            EcologyEconomySnapshot changed = Observe(world).EcologyEconomy;
            Assert.That(changed.BiomassConversionError, Is.EqualTo(-3)); Assert.That(changed.WaterConversionError, Is.EqualTo(-5)); Assert.That(changed.MineralConversionError, Is.EqualTo(-7));
            Assert.That(snapshot.BiomassConversionError, Is.Zero);
            // Hash was measured before intentional test-fixture corruption.
            Set(world.Ecology.Balance, "MaterialBiomassProduced", world.Ecology.Balance.MaterialBiomassProduced - 3);
            Set(world.Ecology.Balance, "MaterialWaterProduced", world.Ecology.Balance.MaterialWaterProduced - 5);
            Set(world.Ecology.Balance, "MineralProduced", world.Ecology.Balance.MineralProduced - 7);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }

        [Test]
        public void Snapshot_SelectedObjectBindingTextUsesCopiedAnimalCorpseAndHabitatData()
        {
            WorldState world = CreateWorld();
            AnimalState fox = world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Fox);
            Set(fox, "AgeTicks", fox.Definition.MaturityTicks);
            Set(fox, "Behavior", AnimalBehavior.Migrate); Set(fox, "Energy", 432);
            Set(fox, "Hydration", 1234); Set(fox, "CoreTemperature", 3700);
            Set(fox, "MigrationDestination", 0); fox.MigrationPath.Add(fox.Position);
            Set(fox, "PregnancyTicks", 120); Set(fox, "PregnancyLitter", 2);
            Set(fox, "MotherId", new EntityId(9001));
            CarcassState dead = Kill(world, world.Ecology.Animals.First(a => a.Species == AnimalSpecies.Vole), AnimalDeathCause.Predation, fox.Id);
            ulong hash = StateHasher.Compute(world);
            EcologyEconomySnapshot snapshot = Observe(world).EcologyEconomy;
            AnimalSnapshot animal = snapshot.Animals.Single(a => a.Id == fox.Id);
            string body = BindingText("AnimalDescription", animal);
            Assert.That(body, Does.Contain("迁徙").And.Contain("能量 432/" + fox.Definition.EnergyCapacity).And.Contain("水分 1234").And.Contain("体温 37.0℃"));
            Assert.That(body, Does.Contain("年龄 " + fox.AgeTicks / GameCalendar.TicksPerDay + " 天 · 成年").And.Contain("群体 #" + fox.GroupId));
            Assert.That(body, Does.Contain("母亲 #9001").And.Contain("妊娠 120 分 · 胎数 2").And.Contain("迁徙目的地 外部区域 #0"));
            CarcassSnapshot carcass = snapshot.Carcasses.Single(c => c.Id == dead.Id);
            string corpse = BindingText("CarcassDescription", carcass);
            Assert.That(corpse, Does.Contain("死因 被捕食 · 来源 #" + fox.Id.Value).And.Contain("生肉 " + dead.MeatRemaining).And.Contain("新鲜度 100%"));
            HabitatSnapshot habitat = snapshot.Habitats.Single(h => h.Id == fox.HabitatId);
            string resources = BindingText("HabitatDescription", habitat);
            Assert.That(resources, Does.Contain("草本 " + habitat.Herbaceous.Biomass + "/" + habitat.Herbaceous.Capacity).And.Contain("雪水 " + habitat.SnowWater));
            Assert.That(resources, Does.Contain("遮蔽 " + habitat.Shelter / 100 + "%").And.Contain("肥力 " + habitat.SoilFertility / 100 + "%"));
            Assert.That(Enum.GetValues(typeof(AnimalSpecies)).Cast<AnimalSpecies>().Select(ChineseText.Species),
                Is.EqualTo(new[] { "野兔", "野猪", "鹿", "田鼠", "狐狸", "狼", "渡鸦" }));
            Set(fox, "Energy", 0); Set(dead, "Freshness", 0);
            Assert.That(BindingText("AnimalDescription", animal), Is.EqualTo(body));
            Assert.That(BindingText("CarcassDescription", carcass), Is.EqualTo(corpse));
            Set(fox, "Energy", 432); Set(dead, "Freshness", 10000);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }

        private static string BindingText(string method, object snapshot)
        {
            return (string)typeof(GodHud).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { snapshot });
        }

        [UnityTest]
        public IEnumerator Hud_ExistingScrollShowsAllSpeciesAndSelectedAnimalCorpseHabitat_WithoutMutation()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            WorldState world = game.Session.World;
            GridPos cell = world.Map.CollectReachable(world.Center).First(c => world.Ecology.HabitatAt(c) != null);
            AnimalState selected = world.Ecology.Spawn(world, AnimalSpecies.Fox, cell, AnimalDefinition.Fox.MaturityTicks);
            Set(selected, "Energy", 432); Set(selected, "Behavior", AnimalBehavior.Migrate);
            Set(selected, "MigrationDestination", 0); selected.MigrationPath.Add(cell);
            AnimalState doomed = world.Ecology.Spawn(world, AnimalSpecies.Vole, cell, AnimalDefinition.Vole.MaturityTicks);
            CarcassState corpse = Kill(world, doomed, AnimalDeathCause.Predation, selected.Id);
            AnimalState external = world.Ecology.Spawn(world, AnimalSpecies.Raven, cell, 0);
            Depart(world, external, world.Ecology.Regions.Regions[0]);
            game.Session.ReadModel.Refresh(world, null, 0);
            ulong hash = StateHasher.Compute(world);
            if (game.Hud.OpenGlobalPanel != 0) { game.Hud.ToggleGlobalPanel(0); }
            game.Selection.SelectCell(cell); // Calls the production cell-selection binding immediately.
            string text = game.Hud.ResourceText;
            Assert.That(text, Does.Contain("选中动物 #" + selected.Id.Value + " · 狐狸"));
            Assert.That(text, Does.Contain("迁徙").And.Contain("能量 432/" + selected.Definition.EnergyCapacity));
            Assert.That(text, Does.Contain("年龄 " + selected.AgeTicks / GameCalendar.TicksPerDay + " 天 · 成年"));
            Assert.That(text, Does.Contain("群体 #" + selected.GroupId).And.Contain("迁徙目的地 外部区域 #0"));
            Assert.That(text, Does.Contain("选中尸体 #" + corpse.Id.Value + " · 田鼠").And.Contain("死因 被捕食").And.Contain("来源 #" + selected.Id.Value));
            Assert.That(text, Does.Contain("生肉 " + corpse.MeatRemaining).And.Contain("新鲜度 100%"));
            HabitatPatchState patch = world.Ecology.HabitatAt(cell);
            Assert.That(text, Does.Contain("选中栖地 #" + patch.Id).And.Contain("草本 " + patch.Herbaceous.Biomass + "/" + patch.Herbaceous.Capacity));
            Assert.That(text, Does.Contain("遮蔽 " + patch.Shelter / 100 + "%").And.Contain("肥力 " + patch.SoilFertility / 100 + "%"));
            Assert.That(text, Does.Not.Contain("选中动物 #" + external.Id.Value));
            Assert.That(text, Does.Not.Contain("选中动物 #" + corpse.Id.Value));
            foreach (AnimalSpecies species in Enum.GetValues(typeof(AnimalSpecies)))
            {
                Assert.That(game.Hud.EconomyText, Does.Contain(ChineseText.Species(species) + " · 本地 " + world.Ecology.Count(species)));
            }
            Assert.That(game.Hud.EconomyText, Does.Contain("区域往来").And.Contain("本地栖地资源").And.Contain("死亡原因").And.Contain("捕食死亡"));
            var observation = game.Hud.GetComponentsInChildren<Transform>(true).Single(t => t.name == "EcologyObservation");
            Assert.That(observation.GetComponentInParent<ScrollRect>().transform.IsChildOf(game.Hud.transform), Is.True);
            foreach (string rowName in new[] { "RabbitRow", "BoarRow" })
            {
                Transform row = game.Hud.GetComponentsInChildren<Transform>(true).Single(t => t.name == rowName);
                Assert.That(row.Find("Value").GetComponent<Text>().text, Does.Not.Contain("/"));
                Assert.That(row.Find("Track"), Is.Null, "A computational cap is not a population quota.");
            }
            Canvas.ForceUpdateCanvases();
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }
    }
}
