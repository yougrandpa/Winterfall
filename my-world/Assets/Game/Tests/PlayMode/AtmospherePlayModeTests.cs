using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator Campfire_IsBrighterAtNightThanNoon()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AtmospherePresenter atmosphere = game.Presenter.Atmosphere;
            atmosphere.SetCampfire(new Vector3(10f, 10f, 0f));
            try
            {
                var noon = new GameDateTime(12 * 60, 1, Season.Spring, 1, 12 * 60);
                var night = new GameDateTime(22 * 60, 1, Season.Spring, 1, 22 * 60);
                atmosphere.Present(noon, 5f);
                float day = atmosphere.CampfireIntensity;
                atmosphere.Present(night, 5f);
                float late = atmosphere.CampfireIntensity;
                Assert.That(day, Is.GreaterThan(0f));
                Assert.That(late, Is.GreaterThan(day), "Campfire warmth must rise after dark.");
            }
            finally
            {
                atmosphere.SetCampfire(game.Presenter.Camp != null ? game.Presenter.Camp.CampfirePosition : null);
            }
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
            ulong hash = StateHasher.Compute(game.Session.World);
            bool snowy = AtmospherePresenter.SnowDay(741103, 3, 1);
            Assert.That(AtmospherePresenter.SnowDay(741103, 3, 1), Is.EqualTo(snowy));
            atmosphere.SetSnowOverride(true);
            atmosphere.Present(game.Session.ReadModel.Date, 2f);
            Assert.That(atmosphere.IsSnowDay, Is.True);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            atmosphere.SetSnowOverride(null);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
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
    }
}
