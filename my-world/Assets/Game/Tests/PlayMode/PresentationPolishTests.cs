using System;
using System.Collections;
using System.IO;
using System.Linq;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.CameraControl;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed class PresentationPolishTests
    {
        [UnityTest]
        public IEnumerator Camera_AnchoredZoomFollowAndSmallPans_PreservePausedWorld()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            var camera = game.PlayerCamera;
            var controller = camera.GetComponent<GodCameraController>();
            ulong hash = StateHasher.Compute(game.Session.World);
            controller.SetPixelScale(2);
            controller.Focus(new Vector3(32, 32, -10));
            var cursor = new Vector3(Screen.width * 0.6f, Screen.height * 0.4f, 10);
            Vector3 before = camera.ScreenToWorldPoint(cursor);
            controller.ZoomAt(cursor, 1);
            Assert.That(Vector3.Distance(camera.ScreenToWorldPoint(cursor), before), Is.LessThan(0.045f));
            controller.ZoomAt(cursor, -1);
            Assert.That(Vector3.Distance(camera.ScreenToWorldPoint(cursor), before), Is.LessThan(0.045f));
            Vector3 start = camera.transform.position;
            for (int i = 0; i < 100; i++) { controller.Pan(new Vector2(0.001f, 0)); }
            Assert.That(camera.transform.position.x - start.x, Is.EqualTo(0.1f).Within(0.032f), "Subpixel input must accumulate.");
            var id = game.Session.ReadModel.Npcs[0].Id;
            game.Selection.Select(id);
            var view = game.Presenter.FindNpcView(id);
            Assert.That(view.SelectionVisible, Is.True);
            game.Hud.ToggleFollowSelected();
            Assert.That(controller.IsFollowing, Is.True);
            yield return null;
            Assert.That(Vector2.Distance(camera.transform.position, view.transform.position), Is.LessThan(0.045f));
            controller.Pan(Vector2.right);
            Assert.That(controller.IsFollowing, Is.False);
            game.Hud.ToggleFollowSelected();
            game.Selection.Select(game.Session.ReadModel.Npcs[1].Id);
            Assert.That(controller.IsFollowing, Is.False);
            Assert.That(view.SelectionVisible, Is.False);
            game.Hud.ReturnToVillage();
            Assert.That(Vector2.Distance(camera.transform.position, game.Presenter.CellCenter(game.Session.ReadModel.StockpileCell)), Is.LessThan(0.045f));
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
        }

        [UnityTest]
        public IEnumerator Restore_RepeatedLoads_DoNotDuplicateSceneryOrLoseWaterAnimation()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "ArtPolish-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "world.json");
            int landmarks = game.Presenter.transform.Find("WorldGrid/Landmarks").childCount;
            int decorations = game.Presenter.transform.Find("WorldGrid/Decorations").childCount;
            int sprites = game.Presenter.GetComponentsInChildren<SpriteRenderer>().Length;
            try
            {
                Assert.That(game.Session.Save(path), Is.True);
                for (int i = 0; i < 3; i++)
                {
                    game.Selection.Select(game.Session.ReadModel.Npcs[0].Id);
                    game.Hud.ToggleFollowSelected();
                    Assert.That(game.Runner.Load(path), Is.True);
                    yield return null; yield return null;
                    Assert.That(game.Presenter.GetComponentsInChildren<Transform>().Count(t => t.name == "Landmarks"), Is.EqualTo(1));
                    Assert.That(game.Presenter.transform.Find("WorldGrid/Landmarks").childCount, Is.EqualTo(landmarks));
                    Assert.That(game.Presenter.transform.Find("WorldGrid/Decorations").childCount, Is.EqualTo(decorations));
                    Assert.That(game.Presenter.GetComponentsInChildren<SpriteRenderer>().Length, Is.EqualTo(sprites));
                    Assert.That(game.PlayerCamera.GetComponent<GodCameraController>().IsFollowing, Is.False);
                    Assert.That(game.Selection.Selected, Is.Null);
                }
                var ground = game.Presenter.transform.Find("WorldGrid/Ground").GetComponent<Tilemap>();
                Vector3Int cell = default;
                bool found = false;
                foreach (Vector3Int candidate in ground.cellBounds.allPositionsWithin)
                {
                    Sprite sprite = ground.GetSprite(candidate);
                    if (sprite != null && sprite.name.StartsWith("water_", StringComparison.Ordinal))
                    { cell = candidate; found = true; break; }
                }
                Assert.That(found, Is.True);
                Sprite paused = ground.GetSprite(cell);
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(ground.GetSprite(cell), Is.SameAs(paused));
                game.Runner.SetSpeed(1);
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(ground.GetSprite(cell), Is.Not.SameAs(paused));
                game.Runner.Pause();
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator Selection_OverlappingVillagers_PicksFrontmostVisibleBody()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            var back = game.Presenter.FindNpcView(game.Session.ReadModel.Npcs[0].Id);
            var front = game.Presenter.FindNpcView(game.Session.ReadModel.Npcs[1].Id);
            var cell = new MyAiWorld.Sim.Core.GridPos(3, 3);
            back.SetCell(cell, true, -1); back.ApplyAlpha(1);
            front.SetCell(cell, true, -1); front.ApplyAlpha(1);
            back.transform.position += Vector3.up * 0.2f;
            back.GetComponent<SpriteRenderer>().sortingOrder = CreatureMotion.SortOrder(back.transform.position.y);
            Assert.That(game.Presenter.PickNpc(new Vector2(3.5f, 3.9f)), Is.SameAs(front));
            Assert.That(game.Presenter.PickNpc(new Vector2(-100, -100)), Is.Null);
            foreach (string key in new[] { "cottage_0", "cottage_1", "cottage_2", "reeds", "lilies", "selection_ring", "selection_pointer", "water_3_3" })
            {
                Assert.That(game.Presenter.Art.Get(key), Is.Not.Null, key);
            }
        }
    }
}
