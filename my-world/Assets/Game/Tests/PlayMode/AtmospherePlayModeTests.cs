using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.Atmosphere;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed class AtmospherePlayModeTests
    {
        [UnityTest]
        public IEnumerator Art_ColdAtlases_ExposeHardAndSoftSprites()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            PastoralArtConfig art = PastoralArtConfig.Load();
            string[] hard = { "tree_0", "tree_5", "snag_0", "fern", "shore_pebbles", "campfire_0", "tent_0",
                "lean_to", "woodpile", "crate_0", "barrel", "alert_low", "alert_down", "snow_0", "mote", "ember",
                "fire_source_0", "fire_source_1", "fire_source_2", "fire_source_3",
                "npc_0_0_0_0_0", "npc_4_3_3_6_3" };
            foreach (string key in hard)
            {
                Sprite sprite = art.Get(key);
                Assert.That(sprite, Is.Not.Null, key);
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point), key);
            }
            string[] soft = { "fog_puff_0", "mist_band_0", "glow_radial", "light_shaft_0" };
            foreach (string key in soft)
            {
                Sprite sprite = art.Get(key);
                Assert.That(sprite, Is.Not.Null, key);
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Bilinear), key);
            }
        }

        [UnityTest]
        public IEnumerator Atmosphere_StaysWithinBudgetAndTogglesIndependently()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            Assert.That(atmosphere, Is.Not.Null);
            Assert.That(atmosphere.LowFogCount + atmosphere.HighFogCount, Is.LessThanOrEqualTo(20));
            Assert.That(atmosphere.HighFogCount, Is.LessThanOrEqualTo(5));
            Assert.That(atmosphere.ParticlePoolCount, Is.LessThanOrEqualTo(200));
            Assert.That(atmosphere.ShaftCount, Is.LessThanOrEqualTo(8));
            Assert.That(game.Presenter.Canopy, Is.Not.Null);
            Assert.That(game.Presenter.Camp, Is.Not.Null);
            Assert.That(game.Presenter.Camp.PropCount, Is.GreaterThanOrEqualTo(6));

            atmosphere.SetLayerEnabled(AtmosphereLayer.Fog, false);
            Assert.That(atmosphere.IsLayerEnabled(AtmosphereLayer.Fog), Is.False);
            Assert.That(atmosphere.IsLayerEnabled(AtmosphereLayer.LightShafts), Is.True);
            atmosphere.SetLayerEnabled(AtmosphereLayer.LightShafts, false);
            Assert.That(atmosphere.IsLayerEnabled(AtmosphereLayer.LightShafts), Is.False);
            game.Presenter.Canopy.SetEnabled(false);
            Assert.That(game.Presenter.Canopy.OcclusionEnabled, Is.False);

            atmosphere.SetLayerEnabled(AtmosphereLayer.Fog, true);
            atmosphere.SetLayerEnabled(AtmosphereLayer.LightShafts, true);
            game.Presenter.Canopy.SetEnabled(true);
            Assert.That(atmosphere.IsLayerEnabled(AtmosphereLayer.Fog), Is.True);
            Assert.That(game.Presenter.Canopy.OcclusionEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator Atmosphere_AnimationTimeDrivesFogAndRespectsPause()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            Transform fog = atmosphere.transform.Find("FogLow_0");
            Assert.That(fog, Is.Not.Null);
            atmosphere.Present(game.Session.ReadModel.Date, 10f);
            Vector3 first = fog.position;
            atmosphere.Present(game.Session.ReadModel.Date, 10f);
            Assert.That(fog.position, Is.EqualTo(first), "Same AnimationTime must not advance drift.");
            atmosphere.Present(game.Session.ReadModel.Date, 13f);
            Assert.That(Vector3.Distance(fog.position, first), Is.GreaterThan(0.05f));
        }

        [UnityTest]
        public IEnumerator Campfire_RealFuelIsBrighterAtNightThanNoon()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Clear, 0, 12 * 60));
            Apply(game, new PrimeSheltersCommand(2));
            atmosphere.Present(game.Session.ReadModel.Date, 5f);
            float day = atmosphere.CampfireIntensity;
            Assert.That(game.Session.ReadModel.Environment.Daylight, Is.GreaterThan(9000));
            Assert.That(atmosphere.ActiveFireCount, Is.EqualTo(2));
            Assert.That(atmosphere.NightAmount, Is.LessThan(0.01f));
            ulong hash = StateHasher.Compute(game.Session.World);
            var falseNight = new GameDateTime(22 * 60, 1, Season.Spring, 1, 22 * 60);
            atmosphere.Present(falseNight, 5f);
            Assert.That(atmosphere.NightAmount, Is.LessThan(0.01f), "A caller date cannot invent darkness.");
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));

            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Clear, 0, 22 * 60));
            atmosphere.Present(game.Session.ReadModel.Date, 5f);
            Assert.That(game.Session.ReadModel.Environment.Daylight, Is.Zero);
            Assert.That(atmosphere.NightAmount, Is.EqualTo(1f));
            Assert.That(day, Is.GreaterThan(0f));
            Assert.That(atmosphere.CampfireIntensity, Is.GreaterThan(day), "Real stove warmth must rise after dark.");
        }

        [UnityTest]
        public IEnumerator CanopyFade_SelectedNpcLowersAlphaAndRestores()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            var faderObject = new GameObject("CanopyFaderTest");
            var treeObject = new GameObject("TreeTest", typeof(SpriteRenderer));
            try
            {
                CanopyFader fader = faderObject.AddComponent<CanopyFader>();
                fader.Initialize(game.Presenter.Atmosphere.Profile);
                treeObject.transform.position = new Vector3(5f, 5f, 0f);
                var renderer = treeObject.GetComponent<SpriteRenderer>();
                renderer.sprite = game.Presenter.Art.Get("tree_0");
                fader.Register(renderer);
                int hits = fader.ApplyFocus(new Vector3(5f, 5f, 0f), true);
                Assert.That(hits, Is.GreaterThanOrEqualTo(1));
                Assert.That(renderer.color.a, Is.EqualTo(fader.SelectedAlpha).Within(0.01f));
                Assert.That(fader.SelectedOccluded, Is.True);
                Assert.That(fader.ApplyFocus(new Vector3(30f, 30f, 0f), false), Is.EqualTo(0));
                Assert.That(renderer.color.a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(treeObject);
                Object.DestroyImmediate(faderObject);
            }
        }

        [UnityTest]
        public IEnumerator Weather_IsDeterministicAndDoesNotTouchSimulationHash()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            using (var a = new GameSession(game.Session.Config))
            using (var b = new GameSession(game.Session.Config))
            {
                foreach (WeatherKind weather in new[] { WeatherKind.Clear, WeatherKind.Snow, WeatherKind.Blizzard })
                {
                    a.EnqueueCommand(new ArrangeEnvironmentCommand(weather, 85));
                    b.EnqueueCommand(new ArrangeEnvironmentCommand(weather, 85));
                    a.Tick(); b.Tick();
                    Assert.That(a.ReadModel.Environment.Weather, Is.EqualTo(b.ReadModel.Environment.Weather));
                    Assert.That(a.ReadModel.Environment.Daylight, Is.EqualTo(b.ReadModel.Environment.Daylight));
                    Assert.That(a.LastStateHash, Is.EqualTo(b.LastStateHash));
                }
            }
            var old = game.Session.ReadModel.Environment;
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Snow, 20));
            var snow = game.Session.ReadModel.Environment;
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(atmosphere.IsSnowing, Is.True);
            float snowIntensity = atmosphere.SnowIntensity;
            int snowParticles = atmosphere.VisibleParticleCount;
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Blizzard, 90));
            ulong hash = StateHasher.Compute(game.Session.World);
            ulong rng = game.Session.World.Environment.RngState;
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(atmosphere.SnowIntensity, Is.GreaterThan(snowIntensity));
            Assert.That(atmosphere.VisibleParticleCount, Is.GreaterThan(snowParticles));
            Assert.That(snow.Weather, Is.EqualTo(WeatherKind.Snow), "Previously published weather must remain immutable.");
            Assert.That(old, Is.Not.SameAs(game.Session.ReadModel.Environment));
            atmosphere.SetLayerEnabled(AtmosphereLayer.Particles, false);
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(atmosphere.VisibleParticleCount, Is.Zero);
            Assert.That(atmosphere.IsSnowing, Is.True, "Hiding precipitation does not change the environment.");
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            Assert.That(game.Session.World.Environment.RngState, Is.EqualTo(rng));
            atmosphere.SetLayerEnabled(AtmosphereLayer.Particles, true);
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Clear, 0));
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(atmosphere.IsSnowing, Is.False);
            Assert.That(atmosphere.SnowIntensity, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Weather_AuthoritativeWindAndSnowControlFogAndShafts()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Clear, 0, 12 * 60));
            var fog = atmosphere.transform.Find("FogLow_0").GetComponent<SpriteRenderer>();
            Assert.That(atmosphere.ShaftCount, Is.GreaterThan(0));
            var shaft = atmosphere.transform.Find("LightShaft_0").GetComponent<SpriteRenderer>();
            atmosphere.Present(game.Session.ReadModel.Date, 0f);
            Vector3 origin = fog.transform.position;
            float clearFog = fog.color.a;
            float clearShaft = shaft.color.a;
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            float calmDrift = Vector3.Distance(origin, fog.transform.position);

            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Snow, 20, 12 * 60));
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(fog.color.a, Is.GreaterThan(clearFog));
            Assert.That(shaft.color.a, Is.LessThan(clearShaft));
            float snowFog = fog.color.a;
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Blizzard, 90, 12 * 60));
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(fog.color.a, Is.GreaterThan(snowFog));
            Assert.That(shaft.enabled, Is.False);
            Assert.That(Vector3.Distance(origin, fog.transform.position), Is.GreaterThan(calmDrift));
            var environment = game.Session.ReadModel.Environment;
            float dark = 1f - environment.Daylight / 10000f;
            Assert.That(atmosphere.NightAmount, Is.EqualTo(dark * dark * (3f - 2f * dark)).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator Atmosphere_PausedBlizzardFreezesSnapshotsDriftFlameAndHash()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Blizzard, 90));
            Apply(game, new PrimeSheltersCommand(2));
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            float clock = game.Runner.AnimationTime;
            atmosphere.Present(game.Session.ReadModel.Date, clock);
            var environment = game.Session.ReadModel.Environment;
            var survival = game.Session.ReadModel.Survival;
            ulong hash = StateHasher.Compute(game.Session.World);
            ulong rng = game.Session.World.Environment.RngState;
            long tick = game.Session.ReadModel.Tick;
            var renderers = atmosphere.GetComponentsInChildren<SpriteRenderer>();
            Vector3[] positions = renderers.Select(r => r.transform.position).ToArray();
            Color[] colors = renderers.Select(r => r.color).ToArray();
            Sprite[] sprites = renderers.Select(r => r.sprite).ToArray();
            yield return null; yield return null;
            Assert.That(game.Runner.AnimationTime, Is.EqualTo(clock));
            Assert.That(game.Session.ReadModel.Tick, Is.EqualTo(tick));
            Assert.That(game.Session.ReadModel.Environment, Is.SameAs(environment));
            Assert.That(environment.Temperature, Is.EqualTo(game.Session.World.Environment.Temperature));
            Assert.That(environment.Wind, Is.EqualTo(90));
            Assert.That(environment.Weather, Is.EqualTo(WeatherKind.Blizzard));
            Assert.That(environment.SnowDepth, Is.EqualTo(game.Session.World.Environment.SnowDepth));
            Assert.That(environment.WaterFrozen, Is.True);
            Assert.That(environment.IsDaytime, Is.EqualTo(environment.Daylight >= 2000));
            Assert.That(game.Session.ReadModel.Survival, Is.SameAs(survival));
            atmosphere.Present(game.Session.ReadModel.Date, game.Runner.AnimationTime);
            for (int i = 0; i < renderers.Length; i++)
            {
                Assert.That(renderers[i].transform.position, Is.EqualTo(positions[i]), renderers[i].name);
                Assert.That(renderers[i].color, Is.EqualTo(colors[i]), renderers[i].name);
                Assert.That(renderers[i].sprite, Is.SameAs(sprites[i]), renderers[i].name);
            }
            Assert.That(atmosphere.ActiveFireCount, Is.EqualTo(2));
            Assert.That(atmosphere.IsSnowing, Is.True);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            Assert.That(game.Session.World.Environment.RngState, Is.EqualTo(rng));
        }

        [UnityTest]
        public IEnumerator ShelterSnapshots_RealFuelExpiresEvenWhenVisualsAreDisabled()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Snow, 30));
            long consumedWood = game.Session.World.Materials.Consumed(ItemKind.Wood);
            Apply(game, new PrimeSheltersCommand(2));
            Assert.That(game.Session.World.Materials.Consumed(ItemKind.Wood), Is.EqualTo(consumedWood + 2));
            Assert.That(game.Session.World.Materials.Error(game.Session.World, ItemKind.Wood), Is.Zero);
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            atmosphere.Present(game.Session.ReadModel.Date, 3f);
            SurvivalReadModel retained = game.Session.ReadModel.Survival;
            ShelterSnapshot first = retained.Shelters[0];
            Assert.That(first.Burning, Is.True);
            Assert.That(first.FuelTicks, Is.EqualTo(SurvivalRules.FuelTicksPerWood - 1));
            Assert.That(first.HeatBudget, Is.EqualTo(first.FuelTicks * SurvivalRules.HeatPerTick));
            Assert.That(first.Wood, Is.Zero);
            Assert.That(first.Items[(int)ItemKind.Wood], Is.EqualTo(first.Wood));
            Assert.That(first.Id, Is.EqualTo(game.Session.World.Survival.Shelters[0].Id));
            Assert.That(first.Cell, Is.EqualTo(game.Session.World.Survival.Shelters[0].Position));
            Assert.That(first.Entrance, Is.EqualTo(game.Session.World.Survival.Shelters[0].Entrance));
            Assert.That(first.Capacity, Is.EqualTo(game.Session.World.Survival.Shelters[0].Capacity));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)first.Items)[(int)ItemKind.Wood] = 99);
            Assert.Throws<NotSupportedException>(() => ((IList<ShelterSnapshot>)retained.Shelters)[0] = retained.Shelters[1]);
            Assert.That(typeof(ShelterSnapshot).GetProperties().All(p => p.GetSetMethod() == null), Is.True);
            int retainedFuel = first.FuelTicks;
            int retainedHeat = first.HeatBudget;
            int retainedTemperature = first.Temperature;
            int rendererCount = FireRenderers(atmosphere).Length;
            Assert.That(rendererCount, Is.EqualTo(6));
            bool master = atmosphere.Profile.masterEnabled;
            try
            {
                ulong hash = StateHasher.Compute(game.Session.World);
                ulong rng = game.Session.World.Environment.RngState;
                atmosphere.Profile.masterEnabled = false;
                atmosphere.Present(game.Session.ReadModel.Date, 3f);
                Assert.That(atmosphere.HasCampfire, Is.True, "Real combustion exists even with visuals hidden.");
                Assert.That(atmosphere.CampfireIntensity, Is.Zero);
                Assert.That(FireRenderers(atmosphere).All(r => !r.enabled), Is.True);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                Assert.That(game.Session.World.Environment.RngState, Is.EqualTo(rng));
                Assert.That(game.Session.ReadModel.Survival.Shelters[0].Temperature, Is.EqualTo(retainedTemperature));
                game.Runner.StepOnce();
                ShelterSnapshot burning = game.Session.ReadModel.Survival.Shelters[0];
                Assert.That(burning.FuelTicks, Is.EqualTo(retainedFuel - 1));
                Assert.That(burning.HeatBudget, Is.EqualTo(retainedHeat - SurvivalRules.HeatPerTick));
                Assert.That(burning.Temperature, Is.GreaterThan(retainedTemperature));
                Assert.That(game.Session.World.Materials.Error(game.Session.World, ItemKind.Wood), Is.Zero);
                atmosphere.Profile.masterEnabled = master;
                atmosphere.Present(game.Session.ReadModel.Date, 3f);
                Assert.That(atmosphere.ActiveFireCount, Is.EqualTo(2));
                Assert.That(FireRenderers(atmosphere).All(r => r.enabled), Is.True);
                Assert.That(FireRenderers(atmosphere).Length, Is.EqualTo(rendererCount));
                int remaining = burning.FuelTicks;
                for (int i = 0; i < remaining; i++) { game.Runner.StepOnce(); }
                atmosphere.Present(game.Session.ReadModel.Date, 3f);
                Assert.That(game.Session.ReadModel.Survival.Shelters[0].FuelTicks, Is.Zero);
                Assert.That(game.Session.ReadModel.Survival.Shelters[0].HeatBudget, Is.Zero);
                Assert.That(atmosphere.HasCampfire, Is.False);
                Assert.That(atmosphere.ActiveFireCount, Is.Zero);
                Assert.That(atmosphere.CampfireIntensity, Is.Zero);
                Assert.That(FireRenderers(atmosphere).All(r => !r.enabled), Is.True);
                Assert.That(first.FuelTicks, Is.EqualTo(retainedFuel));
                Assert.That(first.HeatBudget, Is.EqualTo(retainedHeat));
                Assert.That(first.Temperature, Is.EqualTo(retainedTemperature));
                Apply(game, new PrimeSheltersCommand(2));
                atmosphere.Present(game.Session.ReadModel.Date, 3f);
                Assert.That(atmosphere.ActiveFireCount, Is.EqualTo(2));
                Assert.That(FireRenderers(atmosphere).Length, Is.EqualTo(rendererCount), "Relighting must reuse the source pool.");
            }
            finally { atmosphere.Profile.masterEnabled = master; }
        }

        [UnityTest]
        public IEnumerator Atmosphere_LoadedRevisionImmediatelyRebuildsWithoutDuplicateLights()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            Apply(game, new ArrangeEnvironmentCommand(WeatherKind.Blizzard, 90));
            Apply(game, new PrimeSheltersCommand(2));
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            atmosphere.Present(game.Session.ReadModel.Date, 3f);
            ulong hash = StateHasher.Compute(game.Session.World);
            string folder = Path.Combine(Path.GetTempPath(), "AtmosphereLoad-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "world.json");
            try
            {
                Assert.That(game.Session.Save(path), Is.True, game.Session.SaveStatus);
                Apply(game, new PrimeSheltersCommand(0));
                atmosphere.Present(game.Session.ReadModel.Date, 3f);
                Assert.That(atmosphere.HasCampfire, Is.False);
                for (int i = 0; i < 3; i++)
                {
                    SpriteRenderer[] old = FireRenderers(atmosphere);
                    int revision = game.Session.WorldRevision;
                    Assert.That(game.Runner.Load(path), Is.True, game.Session.SaveStatus);
                    Assert.That(game.Session.WorldRevision, Is.EqualTo(revision + 1));
                    Assert.That(game.Runner.IsPaused, Is.True);
                    Assert.That(game.Runner.AnimationTime, Is.Zero);
                    atmosphere.Present(game.Session.ReadModel.Date, game.Runner.AnimationTime);
                    Assert.That(old.All(r => !r.gameObject.activeSelf && r.transform.parent == null), Is.True);
                    Assert.That(atmosphere.ActiveFireCount, Is.EqualTo(2));
                    Assert.That(FireRenderers(atmosphere).Length, Is.EqualTo(6));
                    Assert.That(FireRenderers(atmosphere).All(r => r.enabled), Is.True);
                    Assert.That(atmosphere.IsSnowing, Is.True);
                    Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                    atmosphere.Present(game.Session.ReadModel.Date, 0f);
                    Assert.That(FireRenderers(atmosphere).Length, Is.EqualTo(6));
                }
                yield return null; yield return null;
                atmosphere = game.Presenter.Atmosphere;
                Assert.That(FireRenderers(atmosphere).Length, Is.EqualTo(6));
                Assert.That(atmosphere.ActiveFireCount, Is.EqualTo(2));
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator CampDressing_ColdStoveNeverAnimatesOrCreatesLight()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            Apply(game, new PrimeSheltersCommand(0));
            var stove = game.Presenter.Camp.GetComponentsInChildren<SpriteRenderer>(true)
                .Single(r => r.name.StartsWith("cold_stove_"));
            Sprite sprite = stove.sprite;
            Assert.That(sprite.rect.height, Is.LessThan(game.Presenter.Art.Get("campfire_0").rect.height));
            Assert.That(game.Presenter.Camp.GetComponentsInChildren<SpriteRenderer>(true)
                .Any(r => r.name.Contains("embers") || r.name.Contains("Glow") || r.name.Contains("Pool")), Is.False);
            game.Presenter.Atmosphere.Present(game.Session.ReadModel.Date, 10f);
            Assert.That(game.Presenter.Atmosphere.HasCampfire, Is.False);
            game.Presenter.Atmosphere.Present(game.Session.ReadModel.Date, 13f);
            yield return null;
            Assert.That(stove.sprite, Is.SameAs(sprite));
        }

        [UnityTest]
        public IEnumerator Villagers_InitialLooksAreStableAndUnique()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            var pairs = new HashSet<string>();
            foreach (var npc in game.Session.ReadModel.Npcs)
            {
                int role = NpcView.RoleIndex(npc.Role);
                int look = NpcView.LookIndex(npc.Id);
                Assert.That(look, Is.GreaterThanOrEqualTo(0));
                Assert.That(look, Is.LessThan(4));
                Assert.That(pairs.Add(role + ":" + look), Is.True, "Duplicate role/look pair for " + npc.Id);
            }
            Assert.That(pairs.Count, Is.EqualTo(game.Session.ReadModel.Npcs.Count));
            Assert.That(NpcView.LookIndex(new EntityId(1)), Is.EqualTo(0));
            Assert.That(NpcView.LookIndex(new EntityId(20)), Is.EqualTo(3));
            Assert.That(NpcView.LookIndex(new EntityId(20)), Is.EqualTo(3));
        }

        private static SpriteRenderer[] FireRenderers(AtmospherePresenter atmosphere)
        {
            return atmosphere.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(r => r.name.StartsWith("ShelterFire", StringComparison.Ordinal)).ToArray();
        }

        private static void Apply(GameBootstrap game, IWorldCommand command)
        {
            int applied = game.Session.AppliedCommandCount;
            game.Session.EnqueueCommand(command);
            game.Runner.StepOnce();
            Assert.That(game.Session.AppliedCommandCount, Is.EqualTo(applied + 1), command.Describe());
        }

        // PlayMode is not a friend assembly of Sim. Test-only setup reaches internal
        // setters here, exclusively from commands applied at the simulation tick boundary.
        private static void SetSimulationProperty(object state, string name, object value)
        {
            PropertyInfo property = state.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null, name);
            MethodInfo setter = property.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null, name);
            setter.Invoke(state, new[] { value });
        }

        private sealed class ArrangeEnvironmentCommand : IWorldCommand
        {
            private readonly WeatherKind _weather;
            private readonly int _wind;
            private readonly int _minute;

            public ArrangeEnvironmentCommand(WeatherKind weather, int wind, int minute = -1)
            {
                _weather = weather;
                _wind = wind;
                _minute = minute;
            }

            public string Describe() => "Arrange actual environment " + _weather + " wind " + _wind;

            public bool Apply(WorldState world, CommandContext context)
            {
                if (_minute >= 0) { world.Tick = world.Tick / GameCalendar.TicksPerDay * GameCalendar.TicksPerDay + _minute; }
                SetSimulationProperty(world.Environment, nameof(EnvironmentState.Weather), _weather);
                SetSimulationProperty(world.Environment, nameof(EnvironmentState.Wind), _wind);
                SetSimulationProperty(world.Environment, nameof(EnvironmentState.Temperature), -1200);
                SetSimulationProperty(world.Environment, nameof(EnvironmentState.SnowDepth), 500);
                SetSimulationProperty(world.Environment, nameof(EnvironmentState.NextWeatherTick), world.Tick + 10000L);
                // EnvironmentSystem derives Daylight during this very tick. No visual override.
                return true;
            }
        }

        private sealed class PrimeSheltersCommand : IWorldCommand
        {
            private readonly int _burningShelters;

            public PrimeSheltersCommand(int burningShelters) { _burningShelters = burningShelters; }
            public string Describe() => "Transfer real wood to " + _burningShelters + " dwelling stoves";

            public bool Apply(WorldState world, CommandContext context)
            {
                Assert.That(world.Survival.Shelters.Count, Is.GreaterThanOrEqualTo(Math.Max(2, _burningShelters)));
                foreach (ShelterState shelter in world.Survival.Shelters)
                {
                    int wood = shelter.Inventory.Count(ItemKind.Wood);
                    if (wood > 0)
                    {
                        Assert.That(shelter.Inventory.TryRemove(ItemKind.Wood, wood), Is.True);
                        Assert.That(world.Resources.Stockpile.TryAdd(ItemKind.Wood, wood), Is.True);
                    }
                    SetSimulationProperty(shelter, nameof(ShelterState.FuelTicks), 0);
                    SetSimulationProperty(shelter, nameof(ShelterState.HeatBudget), 0);
                    SetSimulationProperty(shelter, nameof(ShelterState.Temperature), 800);
                }
                for (int i = 0; i < _burningShelters; i++)
                {
                    Assert.That(world.Resources.Stockpile.TryRemove(ItemKind.Wood, 1), Is.True);
                    Assert.That(world.Survival.Shelters[i].Inventory.TryAdd(ItemKind.Wood, 1), Is.True);
                }
                foreach (var npc in world.Entities.Npcs)
                {
                    npc.Position = npc.Home;
                    npc.Needs.Hunger = npc.Needs.Thirst = npc.Needs.Fatigue = npc.Needs.Health = 9000;
                    SetSimulationProperty(npc.Survival, nameof(npc.Survival.BodyTemperature), 3700);
                    SetSimulationProperty(npc.Plan, nameof(npc.Plan.StepIndex), npc.Plan.Steps.Count);
                    SetSimulationProperty(npc.Plan, nameof(npc.Plan.StepStarted), false);
                    npc.Action.SetIdle("Atmosphere fixture: stay home without fetching fuel", world.Tick + 100000);
                }
                // ThermalSystem alone ignites the wood, charges material consumption and spends heat.
                return true;
            }
        }
    }
}
