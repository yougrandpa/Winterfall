using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.UI;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed partial class BootstrapPlayModeTests
    {
        [UnityTest]
        public IEnumerator Journal_MenusTabsAndEscapePriority_KeepPausedWorldUnchanged()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            long tick = game.Session.LastTick; ulong hash = StateHasher.Compute(game.Session.World);
            game.Selection.Select(game.Session.ReadModel.Npcs[0].Id);
            yield return new WaitForSecondsRealtime(0.2f);
            foreach (string tab in new[] { "RelationshipsTab", "PlanTab", "OverviewTab" })
            {
                ClickHudButton(game, tab);
                Assert.That(FindHudButton(game, tab).image.color, Is.EqualTo(HudTheme.Ink));
            }
            ClickHudButton(game, "PlanTab");
            game.Selection.Select(game.Session.ReadModel.Npcs[1].Id);
            Assert.That(game.Hud.InspectorTab, Is.Zero, "A new villager must open on overview.");
            for (int i = 0; i < 3; i++)
            {
                ClickHudButton(game, new[] { "ResourcesButton", "EconomyButton", "EventsButton" }[i]);
                Assert.That(game.Hud.OpenGlobalPanel, Is.EqualTo(i));
                Assert.That(FindHudButton(game, new[] { "ResourcesButton", "EconomyButton", "EventsButton" }[i]).image.color, Is.EqualTo(HudTheme.Ink));
                Assert.That(game.Hud.GetComponentsInChildren<PanelTransition>().Count(p =>
                    new[] { "ResourceOverview", "EcologyEconomy", "EventLog" }.Contains(p.name) && p.IsOpen), Is.EqualTo(1));
            }
            ClickHudButton(game, "SaveMenuButton"); yield return new WaitForSecondsRealtime(0.2f);
            game.Hud.CloseTopmostPanel();
            Assert.That(game.Hud.SaveMenuOpen, Is.False);
            Assert.That(game.Hud.OpenGlobalPanel, Is.EqualTo(2)); Assert.That(game.Hud.InspectorOpen, Is.True);
            game.Hud.CloseTopmostPanel();
            Assert.That(game.Hud.OpenGlobalPanel, Is.EqualTo(-1)); Assert.That(game.Hud.InspectorOpen, Is.True);
            game.Hud.CloseTopmostPanel();
            Assert.That(game.Hud.InspectorOpen, Is.False); Assert.That(game.Selection.Selected, Is.Null);
            // Closing immediately releases raycasts, including during the fade-out.
            foreach (var panel in game.Hud.GetComponentsInChildren<PanelTransition>())
            {
                if (!panel.IsOpen)
                {
                    Assert.That(panel.GetComponent<CanvasGroup>().blocksRaycasts, Is.False, panel.name);
                    Assert.That(panel.GetComponent<CanvasGroup>().interactable, Is.False, panel.name);
                }
            }
            var raycaster = game.Hud.GetComponentInChildren<GraphicRaycaster>();
            var results = new List<RaycastResult>();
            raycaster.Raycast(new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * 0.15f, Screen.height * 0.5f) }, results);
            Assert.That(results, Is.Empty, "Hidden left panels must allow map input.");
            yield return new WaitForSecondsRealtime(0.22f);
            Assert.That(game.Session.LastTick, Is.EqualTo(tick)); Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
        }

        [UnityTest]
        public IEnumerator Journal_SaveAvailabilityFeedbackAndLoad_ClearsOldSelection()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "Journal-" + Guid.NewGuid().ToString("N"));
            game.Session.SaveDirectory = folder;
            try
            {
                ClickHudButton(game, "SaveMenuButton"); yield return new WaitForSecondsRealtime(0.2f);
                foreach (string name in new[] { "LoadButton", "LoadAutoButton", "LoadBackupButton", "LoadAutoBackupButton" })
                { Assert.That(FindHudButton(game, name).interactable, Is.False, name); }
                ClickHudButton(game, "SaveButton");
                Assert.That(FindHudButton(game, "LoadButton").interactable, Is.True);
                Assert.That(game.Hud.ToastVisible, Is.True);
                yield return new WaitForSecondsRealtime(4.2f);
                Assert.That(game.Hud.ToastVisible, Is.False);
                Assert.That(game.Hud.SaveStatusText, Does.Contain("存档成功"));
                ClickHudButton(game, "SaveButton");
                Assert.That(FindHudButton(game, "LoadBackupButton").interactable, Is.True);
                game.Session.Save(game.Session.AutoSavePath, true);
                game.Session.Save(game.Session.AutoSavePath, true);
                yield return new WaitForSecondsRealtime(0.25f);
                ClickHudButton(game, "LoadAutoButton"); ClickHudButton(game, "LoadAutoBackupButton");
                game.Selection.Select(game.Session.ReadModel.Npcs[0].Id);
                ClickHudButton(game, "LoadButton"); yield return null; yield return null;
                Assert.That(game.Selection.Selected, Is.Null); Assert.That(game.Hud.InspectorOpen, Is.False);
                Assert.That(game.Runner.IsPaused, Is.True);
                ulong hash = StateHasher.Compute(game.Session.World); long tick = game.Session.LastTick;
                File.WriteAllText(game.Session.ManualSavePath, "broken");
                ClickHudButton(game, "LoadButton");
                Assert.That(game.Hud.ToastVisible, Is.True); Assert.That(game.Hud.SaveStatusText, Does.Contain("当前世界已保留"));
                yield return new WaitForSecondsRealtime(4.2f);
                Assert.That(game.Hud.ToastVisible, Is.True, "Failures persist until dismissed.");
                ClickHudButton(game, "DismissFeedback"); Assert.That(game.Hud.ToastVisible, Is.False);
                Assert.That(game.Hud.SaveStatusText, Does.Contain("读档失败"));
                Assert.That(game.Session.LastTick, Is.EqualTo(tick)); Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator Journal_ThreeResolutions_PanelsButtonsAndScrollableContentFit()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            foreach (Vector2Int resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
            {
#if UNITY_EDITOR
                var review = Type.GetType("MyAiWorld.EditorTools.ReviewGameView,MyAiWorld.Editor");
                Assert.That(review, Is.Not.Null);
                Assert.That(review.GetMethod("TrySetResolution").Invoke(null, new object[] { resolution.x, resolution.y }), Is.True);
#else
                Screen.SetResolution(resolution.x, resolution.y, false);
#endif
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.That(Screen.width, Is.EqualTo(resolution.x)); Assert.That(Screen.height, Is.EqualTo(resolution.y));
                game.Selection.Select(game.Session.ReadModel.Npcs[0].Id);
                yield return new WaitForSecondsRealtime(0.2f);
                foreach (string tab in new[] { "OverviewTab", "RelationshipsTab", "PlanTab" }) { ClickHudButton(game, tab); }
                foreach (string nav in new[] { "ResourcesButton", "EconomyButton", "EventsButton" })
                {
                    ClickHudButton(game, nav); yield return new WaitForSecondsRealtime(0.2f);
                    Canvas.ForceUpdateCanvases();
                    var open = game.Hud.GetComponentsInChildren<PanelTransition>().Where(p => p.IsOpen).ToArray();
                    Assert.That(open.Length, Is.EqualTo(2));
                    Rect left = ScreenRect(open.Single(p => p.name != "NpcInspector").GetComponent<RectTransform>());
                    Rect right = ScreenRect(open.Single(p => p.name == "NpcInspector").GetComponent<RectTransform>());
                    Assert.That(left.xMax, Is.LessThan(right.xMin));
                    Assert.That(left.yMin, Is.GreaterThan(Screen.height * 0.12f));
                    Assert.That(left.yMax, Is.LessThan(Screen.height * 0.87f));
                    Assert.That(right.xMax, Is.LessThan(Screen.width));
                }
                ClickHudButton(game, "SaveMenuButton"); yield return new WaitForSecondsRealtime(0.2f);
                ClickHudButton(game, "SaveClose");
                game.Hud.CloseGlobalPanels(); game.Hud.CloseInspector();
            }
        }

        [UnityTest]
        public IEnumerator Journal_LongIdentityFailureAndFullMemory_AreScrollableWithoutChangingWorld()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            var model = game.Session.ReadModel;
            var source = model.Npcs[0];
            string longName = string.Concat(Enumerable.Repeat("住在向日葵田边的林间观察员", 8));
            string longReason = string.Concat(Enumerable.Repeat("前方道路暂时无法通行，等待物资与邻居的帮助。", 16));
            // A synthetic observation snapshot stresses typography without altering Sim.
            var npc = new NpcState(source.Id, new NpcIdentity(longName, source.Role, 28),
                new Personality { Diligence = 50, Caution = 50, Sociability = 50, Curiosity = 50 }, source.Cell, 1);
            typeof(NpcPlanState).GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(npc.Plan, new object[] { longReason });
            var constructor = typeof(SocialEvent).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            for (int i = 0; i < 32; i++)
            {
                var memory = constructor.Invoke(new object[] { (long)i + 1, model.Tick, source.Id, model.Npcs[1].Id,
                    SocialKind.FoodGift, 10, 20, "reciprocate_help", 1L });
                typeof(NpcSocialState).GetMethod("Remember", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(npc.Social, new[] { memory });
            }
            var detailsConstructor = typeof(NpcDetails).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var details = (NpcDetails)detailsConstructor.Invoke(new object[] { npc, model.Tick, model.GetDetails(source.Id).NeedsHistory, null });
            var dictionary = (IDictionary)typeof(WorldReadModel).GetField("_details", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(model);
            dictionary[source.Id] = details;
            ulong hash = StateHasher.Compute(game.Session.World); long tick = game.Session.LastTick;
            game.Selection.Select(source.Id); yield return new WaitForSecondsRealtime(0.25f);
            var name = game.Hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "Name");
            Assert.That(name.text, Is.EqualTo(longName));
            var nameScroll = name.GetComponentInParent<ScrollRect>();
            Assert.That(nameScroll.content.rect.height, Is.GreaterThan(nameScroll.viewport.rect.height));
            nameScroll.verticalNormalizedPosition = 0;
            ClickHudButton(game, "RelationshipsTab"); yield return null;
            Assert.That(game.Hud.InspectorText, Does.Contain("#32"));
            var relationships = game.Hud.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "关系记忆");
            Assert.That(relationships.content.rect.height, Is.GreaterThan(relationships.viewport.rect.height));
            relationships.verticalNormalizedPosition = 0;
            yield return null; Canvas.ForceUpdateCanvases();
            var oldest = relationships.content.GetComponentsInChildren<Text>().Last(t => t.name == "Description");
            Assert.That(ScreenRect(oldest.rectTransform).yMin, Is.GreaterThanOrEqualTo(ScreenRect(relationships.viewport).yMin - 2));
            ClickHudButton(game, "PlanTab"); yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(game.Hud.InspectorText, Does.Contain(longReason));
            var plan = game.Hud.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "行动计划");
            Assert.That(plan.content.rect.height, Is.GreaterThan(plan.viewport.rect.height));
            foreach (Text label in plan.content.GetComponentsInChildren<Text>())
            { Assert.That(label.rectTransform.rect.height + 2, Is.GreaterThanOrEqualTo(label.preferredHeight), label.name); }
            ClickHudButton(game, "EventsButton"); ClickHudButton(game, "TimelineSelected");
            Assert.That(game.Hud.EventText, Does.Contain("1 / 11 页"));
            for (int i = 0; i < 10; i++) { ClickHudButton(game, "TimelineOlder"); }
            Assert.That(game.Hud.EventText, Does.Contain("11 / 11 页"));
            Assert.That(FindHudButton(game, "TimelineOlder").interactable, Is.False);
            Assert.That(game.Session.LastTick, Is.EqualTo(tick)); Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
