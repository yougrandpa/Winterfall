using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App.Observability;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.CameraControl;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    [Category("WinterFarmingE2E")]
    public sealed class WinterFarmingPresentationTests
    {
        private static readonly string Shots = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/Progress/WinterFarming"));
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        private static WorldState VisualFixture()
        {
            var world = WorldFactory.Create(new WorldConfigData(42, 64, 64, 20, GameCalendar.WinterStartTick + 360, 2, new[] { 1, 2, 4, 8 }, 14));
            int index = 0;
            foreach (var bed in world.Survival.Facilities.Where(b => b.Kind == FacilityKind.GrowingBed))
            {
                Set(bed, "Progress", SurvivalRules.ConstructionTicks);
                Set(bed, "Temperature", 800); Set(bed, "FuelTicks", 60); Set(bed, "HeatBudget", 1200);
                Set(bed, "SnowDepth", index++ * 2500);
                foreach (int id in bed.PlotIds)
                {
                    var plot = world.Resources.FarmPlots.Single(p => p.Id == id);
                    Set(plot, "Crop", (CropType)(id % 3 + 1)); Set(plot, "Moisture", 800);
                    Set(plot, "GrowthTicks", plot.GrowthRequired * (id % 3 + 1) / 3);
                    Set(plot, "Damage", id % 4 == 0 ? 900 : 0);
                }
            }
            return world;
        }

        private static void Load(GameBootstrap game, WorldState world, string folder)
        {
            game.Session.SaveDirectory = folder;
            string path = Path.Combine(folder, "fixture.json");
            SaveStore.Write(path, new SessionSaveData { World = WorldSnapshot.Capture(world), HashVersion = StateHasher.Version, StateHash = StateHasher.ToHex(StateHasher.Compute(world)) });
            Assert.That(game.Runner.Load(path), Is.True, game.Session.SaveStatus);
        }

        private static void Click(Button button)
        {
            Assert.That(button.IsInteractable() && button.gameObject.activeInHierarchy, Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), button.name);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), "Button must be visible and receive the real pointer raycast.");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static Button Button(GameBootstrap game, string name) => game.Hud.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        private static IEnumerator Resolution(int width)
        {
#if UNITY_EDITOR
            Assert.That(Type.GetType("MyAiWorld.EditorTools.ReviewGameView,MyAiWorld.Editor").GetMethod("TrySetResolution").Invoke(null, new object[] { width, width * 9 / 16 }), Is.True);
#else
            Screen.SetResolution(width, width * 9 / 16, false);
#endif
            yield return new WaitForSecondsRealtime(0.3f);
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator Policies_RealClicksAtThreeResolutions_PauseSpeedAndPendingSaveRestore()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "WinterFarmingUI-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Shots);
            try
            {
                Load(game, VisualFixture(), folder); yield return null; yield return null;
                var plot = game.Session.ReadModel.FarmPlots.First(p => p.BedId != 0);
                foreach (int width in new[] { 1280, 1920, 2560 })
                {
                    yield return Resolution(width);
                    game.PlayerCamera.GetComponent<GodCameraController>().SetPixelScale(3);
                    game.PlayerCamera.GetComponent<GodCameraController>().Focus(game.Presenter.CellCenter(plot.Cell));
                    game.Selection.SelectCell(plot.Cell); yield return new WaitForSecondsRealtime(0.25f);
                    Assert.That(game.Hud.OpenGlobalPanel, Is.EqualTo(0));
                    Assert.That(game.Hud.FarmingText, Does.Contain("土温").And.Contain("菜床").And.Contain("覆雪"));
                    foreach (var button in game.Hud.FarmPolicyButtons)
                    {
                        var label = button.GetComponentInChildren<Text>();
                        Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width + 1f), label.text + " must fit at " + width);
                    }
                    int policy = width == 1280 ? 2 : width == 1920 ? 3 : 4;
                    ulong hash = StateHasher.Compute(game.Session.World);
                    Click(game.Hud.FarmPolicyButtons[policy]);
                    Assert.That(game.Hud.FarmingFeedbackText, Does.Contain("待执行"));
                    Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                    yield return new WaitForSecondsRealtime(0.15f);
                    Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                    SurvivalPresentationTests.Capture(game, Path.Combine(Shots, "policy-pending-" + width + ".png"), width, width * 9 / 16);
                    Click(Button(game, "StepButton")); yield return new WaitForSecondsRealtime(0.25f);
                    Assert.That(game.Session.World.Resources.FindPlot(plot.Id).Policy, Is.EqualTo((FarmCropPolicy)policy));
                    Assert.That(game.Session.World.Resources.FindPlot(plot.Id).Crop, Is.Not.EqualTo(CropType.None));
                    Assert.That(game.Session.World.Campaign.Assisted, Is.True);
                    Click(game.Hud.BedHeatPolicyButtons[width == 1920 ? 0 : 2]);
                    Click(Button(game, "StepButton")); yield return new WaitForSecondsRealtime(0.25f);
                    Assert.That(game.Hud.FarmingFeedbackText, Does.Contain("供暖方针已生效"));
                    SurvivalPresentationTests.Capture(game, Path.Combine(Shots, "winter-farming-" + width + ".png"), width, width * 9 / 16);
                }
                Click(game.Hud.FarmPolicyButtons[1]);
                string pending = Path.Combine(folder, "pending.json");
                Assert.That(game.Session.Save(pending), Is.True);
                Assert.That(game.Runner.Load(pending), Is.True); yield return null; yield return null;
                Assert.That(game.Session.QueuedCommandCount, Is.EqualTo(1));
                game.Selection.SelectCell(plot.Cell); yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.FarmingFeedbackText, Does.Contain("待执行"));
                Click(Button(game, "StepButton")); yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Session.World.Resources.FindPlot(plot.Id).Policy, Is.EqualTo(FarmCropPolicy.Spinach));
                Assert.That(game.Session.QueuedCommandCount, Is.Zero);
                long tick = game.Session.World.Tick;
                Click(Button(game, "Speedx8")); yield return new WaitForSecondsRealtime(0.4f);
                Click(Button(game, "PauseButton"));
                Assert.That(game.Session.World.Tick, Is.GreaterThan(tick));
                Assert.That(game.Runner.IsPaused, Is.True);
                var renderers = game.Presenter.transform.Find("Resources").GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Count(r => r.name.StartsWith("BedPlot_")), Is.EqualTo(game.Session.ReadModel.FarmPlots.Count(p => p.BedId != 0)));
                Assert.That(renderers.Count(r => r.name.StartsWith("Crop_")), Is.EqualTo(game.Session.ReadModel.FarmPlots.Count));
                Assert.That(renderers.Where(r => r.name.StartsWith("BedPlot_")).All(r => r.sprite != null), Is.True);
                foreach (var crop in new[] { CropType.Spinach, CropType.Kale, CropType.LambsLettuce })
                {
                    Assert.That(game.Presenter.Art.Get("winter_crop_" + (int)crop + "_3_0"), Is.Not.Null);
                    Assert.That(game.Presenter.Art.Get("winter_seed_" + (int)crop), Is.Not.Null);
                }
            }
            finally { game.Runner.Pause(); if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator CropAndBedVisuals_UseDedicatedWinterAtlas_AndStayStableAfterRepeatedLoad()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "WinterFarmingArt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Shots);
            try
            {
                Load(game, VisualFixture(), folder); yield return null; yield return null;
                string path = Path.Combine(folder, "visual.json"); Assert.That(game.Session.Save(path), Is.True);
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(game.Runner.Load(path), Is.True); yield return null; yield return null;
                    var renderers = game.Presenter.transform.Find("Resources").GetComponentsInChildren<SpriteRenderer>();
                    Assert.That(renderers.Count(r => r.name.StartsWith("BedPlot_")), Is.EqualTo(12));
                    Assert.That(renderers.Where(r => r.name.StartsWith("Crop_") && r.sprite != null).All(r => r.sprite.texture.name == "WinterFarmingAtlas"), Is.True);
                }
                yield return Resolution(1920);
                int index = 0;
                foreach (var bed in game.Session.ReadModel.Survival.GrowingBeds)
                {
                    game.Hud.CloseGlobalPanels();
                    game.PlayerCamera.GetComponent<GodCameraController>().SetPixelScale(5);
                    game.PlayerCamera.GetComponent<GodCameraController>().Focus(game.Presenter.CellCenter(bed.Cell));
                    yield return null; yield return null;
                    SurvivalPresentationTests.Capture(game, Path.Combine(Shots, "bed-state-" + index++ + ".png"), 1920, 1080);
                }
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }
    }
}
