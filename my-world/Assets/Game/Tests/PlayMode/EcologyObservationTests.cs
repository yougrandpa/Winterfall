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
