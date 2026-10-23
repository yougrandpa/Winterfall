using System;
using System.Collections;
using System.IO;
using System.Linq;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed class SurvivalPresentationTests
    {
        [UnityTest]
        public IEnumerator Production_SelectionSnapshotsArtAndRestore_AreVisibleAndReadOnly()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "SurvivalPresentation-" + Guid.NewGuid().ToString("N"));
            game.Session.SaveDirectory = folder;
            try
            {
                var before = game.Session.ReadModel.Survival;
                for (int i = 0; i < 1800; i++) { game.Runner.StepOnce(); }
                yield return null; yield return null;
                var model = game.Session.ReadModel;
                Assert.That(before.Facilities.Count, Is.Zero);
                Assert.That(model.Survival.Facilities.Count(f => f.Complete), Is.EqualTo(5));
                var art = game.Presenter.Art;
                foreach (var facility in model.Survival.Facilities)
                {
                    Assert.That(art.Get("facility_" + (int)facility.Kind + "_1"), Is.Not.Null);
                    game.Selection.SelectCell(facility.Cell); yield return new WaitForSecondsRealtime(0.25f);
                    Assert.That(game.Hud.ResourceText, Does.Contain("选中"));
                    Assert.That(game.Presenter.transform.Find("Resources/Facility_" + facility.Id).GetComponent<SpriteRenderer>().sprite, Is.Not.Null);
                }
                foreach (string key in new[] { "animal_2_0_0_0", "crop_type_2_3_0", "crop_type_3_3_0", "work_effect_3_0", "ground_stock" }) { Assert.That(art.Get(key), Is.Not.Null, key); }
                game.Selection.SelectCell(model.FarmPlots[0].Cell); yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.ResourceText, Does.Contain("选中农田"));
                ulong hash = StateHasher.Compute(game.Session.World);
                string path = Path.Combine(folder, "world.json"); Assert.That(game.Session.Save(path), Is.True);
                Assert.That(game.Runner.Load(path), Is.True); yield return null; yield return null;
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                Assert.That(game.Presenter.transform.Find("Resources").GetComponentsInChildren<SpriteRenderer>().Count(r => r.name.StartsWith("Facility_")), Is.EqualTo(5));
                game.Selection.Select(game.Session.ReadModel.Npcs[4].Id);
                if (game.Hud.OpenGlobalPanel != 0) { game.Hud.ToggleGlobalPanel(0); }
                yield return new WaitForSecondsRealtime(0.25f);
                string screenshots = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/TestResults/SurvivalVisual")); Directory.CreateDirectory(screenshots);
                game.Selection.SelectCell(game.Session.ReadModel.Survival.Facilities[0].Cell); yield return new WaitForSecondsRealtime(0.25f);
                Capture(game, Path.Combine(screenshots, "survival-1920.png"), 1920, 1080);
                game.Selection.SelectCell(game.Session.ReadModel.FarmPlots[0].Cell); yield return new WaitForSecondsRealtime(0.25f);
                Capture(game, Path.Combine(screenshots, "survival-2560.png"), 2560, 1440);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        private static void Capture(GameBootstrap game, string path, int width, int height)
        {
            var camera = game.PlayerCamera;
            var canvases = Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var oldCameras = canvases.Select(c => c.worldCamera).ToArray();
            var distances = canvases.Select(c => c.planeDistance).ToArray();
            var texture = new RenderTexture(width, height, 24); var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = RenderMode.ScreenSpaceOverlay; canvases[i].worldCamera = oldCameras[i]; canvases[i].planeDistance = distances[i]; }
                Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels);
            }
        }
    }
}
