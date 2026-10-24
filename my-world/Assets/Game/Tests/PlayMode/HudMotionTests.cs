using System;
using System.Collections;
using System.IO;
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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed class HudMotionTests
    {
        private bool _reduced;
        private bool _hadPreference;
        private int _preference;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _hadPreference = PlayerPrefs.HasKey(HudMotion.PreferenceKey);
            _preference = PlayerPrefs.GetInt(HudMotion.PreferenceKey);
            _reduced = HudMotion.Reduced;
            _timeScale = Time.timeScale;
            HudMotion.Reduced = false;
        }

        [TearDown]
        public void TearDown()
        {
            HudMotion.Reduced = _reduced;
            Time.timeScale = _timeScale;
            if (_hadPreference) { PlayerPrefs.SetInt(HudMotion.PreferenceKey, _preference); }
            else { PlayerPrefs.DeleteKey(HudMotion.PreferenceKey); }
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator Panel_ReversalAndRepeatedRefresh_KeepContinuityAndImmediatelyReleaseInput()
        {
            var root = new GameObject("PanelMotionTest", typeof(RectTransform));
            try
            {
                Time.timeScale = 0f;
                var rect = root.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(40, 20);
                var transition = root.AddComponent<PanelTransition>();
                var group = root.GetComponent<CanvasGroup>();
                transition.SetOpen(false, true);
                transition.SetOpen(true);
                yield return new WaitForSecondsRealtime(0.06f);
                Assert.That(group.alpha, Is.InRange(0.01f, 0.99f));
                float before = group.alpha;
                transition.SetOpen(false);
                Assert.That(group.blocksRaycasts || group.interactable, Is.False);
                Assert.That(group.alpha, Is.EqualTo(before));
                yield return new WaitForSecondsRealtime(0.02f);
                before = group.alpha;
                transition.SetOpen(true);
                Assert.That(group.alpha, Is.EqualTo(before));
                for (int i = 0; i < 16; i++) { transition.SetOpen(true); yield return new WaitForSecondsRealtime(0.02f); }
                Assert.That(group.alpha, Is.EqualTo(1f));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(40, 20)));
                transition.SetOpen(false);
                HudMotion.Reduced = true;
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(40, 20)));
                Assert.That(group.alpha, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Meter_RepeatedTargetsDoNotRestart_AndReducedMotionCompletesImmediately()
        {
            var root = new GameObject("MeterMotionTest", typeof(RectTransform));
            try
            {
                var motion = root.AddComponent<HudMeterMotion>();
                var rect = root.GetComponent<RectTransform>();
                motion.SetTarget(0.2f);
                Time.timeScale = 0f;
                motion.SetTarget(0.9f);
                yield return new WaitForSecondsRealtime(0.04f);
                Assert.That(rect.anchorMax.x, Is.InRange(0.21f, 0.89f));
                float before = rect.anchorMax.x;
                motion.SetTarget(0.5f);
                Assert.That(rect.anchorMax.x, Is.EqualTo(before), "Retarget without jumping.");
                for (int i = 0; i < 12; i++) { motion.SetTarget(0.5f); yield return new WaitForSecondsRealtime(0.02f); }
                Assert.That(rect.anchorMax.x, Is.EqualTo(0.5f).Within(0.0001f));
                motion.SetTarget(0.1f);
                HudMotion.Reduced = true;
                Assert.That(rect.anchorMax.x, Is.EqualTo(0.1f).Within(0.0001f));
                motion.SetTarget(0.8f);
                Assert.That(rect.anchorMax.x, Is.EqualTo(0.8f).Within(0.0001f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Button_PressesOnlyMoveContent_DisabledAndHiddenControlsRemainStill()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            yield return new WaitForSecondsRealtime(0.45f);
            ulong hash = StateHasher.Compute(game.Session.World);
            var button = Find<Button>(game, "ResourcesButton");
            var motion = button.GetComponent<HudButtonMotion>();
            var rect = button.GetComponent<RectTransform>();
            var icon = button.transform.Find("Icon").GetComponent<RectTransform>();
            Vector2 position = rect.anchoredPosition, size = rect.sizeDelta, iconPosition = icon.anchoredPosition;
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            motion.OnPointerEnter(data); motion.OnPointerDown(data);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(rect.anchoredPosition, Is.EqualTo(position)); Assert.That(rect.sizeDelta, Is.EqualTo(size));
            Assert.That(icon.anchoredPosition.y, Is.EqualTo(iconPosition.y - 1f).Within(0.01f));
            motion.OnPointerUp(data);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(icon.anchoredPosition, Is.EqualTo(iconPosition));
            button.interactable = false; motion.OnPointerDown(data);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(icon.anchoredPosition, Is.EqualTo(iconPosition));
            button.interactable = true;
            game.Hud.ToggleGlobalPanel(0);
            yield return new WaitForSecondsRealtime(0.2f);
            // Closing during a press must restore every child even while the panel fades.
            var closeButton = game.Hud.GetComponentsInChildren<Button>(true).First(b => b.name == "CloseDetails" && b.GetComponentInParent<PanelTransition>().IsOpen);
            var closeMotion = closeButton.GetComponent<HudButtonMotion>();
            closeMotion.OnPointerDown(data);
            game.Hud.CloseGlobalPanels();
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(closeButton.GetComponentInParent<CanvasGroup>().interactable, Is.False);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
        }

        [UnityTest]
        public IEnumerator EntranceAndPreference_AreInteractiveAndPersistent_WithoutChangingTheWorld()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            var entrance = Find<HudEntrance>(game, "Navigation");
            Assert.That(entrance.GetComponent<CanvasGroup>().interactable, Is.True);
            ulong hash = StateHasher.Compute(game.Session.World); long tick = game.Session.World.Tick;
            game.Hud.ToggleHelp();
            Find<Button>(game, "ReducedMotionButton").onClick.Invoke();
            Assert.That(game.Hud.ReducedMotion, Is.True);
            Assert.That(PlayerPrefs.GetInt(HudMotion.PreferenceKey), Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(0.09f);
            Assert.That(entrance.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(Find<Text>(game, "ReducedMotionButton", true).text, Does.Contain("开"));
            game.Hud.ToggleHelp(); game.Hud.ToggleHelp();
            Assert.That(game.Hud.ReducedMotion, Is.True);
            Assert.That(game.Session.World.Tick, Is.EqualTo(tick));
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            Assert.That(game.Hud.ReducedMotion, Is.True);
        }

        [UnityTest]
        public IEnumerator Ecology_ClosedCooldownVisibleRowsAndRestore_UseFreshBaselines()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            yield return new WaitForSecondsRealtime(0.3f);
            string folder = Path.Combine(Path.GetTempPath(), "HudMotion-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "baseline.json");
            try
            {
                Assert.That(game.Session.Save(path), Is.True);
                var navigation = Find<HudPulse>(game, "EcologyFeedback");
                int count = navigation.PulseCount;
                ChangeBirths(game);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(navigation.PulseCount, Is.EqualTo(count + 1));
                ChangeBirths(game);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(navigation.PulseCount, Is.EqualTo(count + 1), "Coalesce changes within five seconds.");
                yield return new WaitForSecondsRealtime(5f);
                ChangeBirths(game);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(navigation.PulseCount, Is.EqualTo(count + 2));
                game.Hud.ToggleGlobalPanel(1);
                var row = Find<HudPulse>(game, "BirthFeedback");
                int rowCount = row.PulseCount;
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(row.PulseCount, Is.EqualTo(rowCount), "Opening must not replay hidden history.");
                ChangeBirths(game);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(row.PulseCount, Is.EqualTo(rowCount + 1));
                Assert.That(navigation.PulseCount, Is.EqualTo(count + 2));
                Assert.That(game.Runner.Load(path), Is.True);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(navigation.IsPulsing, Is.False);
                Assert.That(row.IsPulsing, Is.False);
                Assert.That(navigation.PulseCount, Is.EqualTo(count + 2));
                Assert.That(row.PulseCount, Is.EqualTo(rowCount + 1));
                ulong hash = StateHasher.Compute(game.Session.World);
                game.Hud.CloseGlobalPanels(); game.Hud.ToggleGlobalPanel(1);
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator Motion_ThreeResolutionsAndKeyFrames_LeaveMapAndTextReadable()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            yield return new WaitForSecondsRealtime(0.45f);
            ulong hash = StateHasher.Compute(game.Session.World);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/TestResults/HudMotionVisual"));
            Directory.CreateDirectory(folder);
            foreach (int width in new[] { 1280, 1920, 2560 })
            {
                game.Hud.CloseGlobalPanels();
                yield return new WaitForSecondsRealtime(0.2f);
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "motion-idle-" + width + ".png"), width, width * 9 / 16);
            }
            game.Hud.ToggleGlobalPanel(1);
            var transition = game.Hud.GetComponentsInChildren<PanelTransition>().Single(p => p.IsOpen);
            // Sample the real transition at fixed times: PNG encoding itself exceeds 60ms.
            foreach (int milliseconds in new[] { 0, 60, 120, 180 })
            {
                typeof(PanelTransition).GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(transition, Time.unscaledTime - milliseconds / 1000f);
                typeof(PanelTransition).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(transition, null);
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "ecology-" + milliseconds.ToString("000") + ".png"), 1920, 1080);
            }
            ChangeBirths(game);
            yield return new WaitForSecondsRealtime(0.21f);
            SurvivalPresentationTests.Capture(game, Path.Combine(folder, "ecology-event.png"), 1920, 1080);
            // The one explicit scenario command above may advance a tick; subsequent UI remains read-only.
            hash = StateHasher.Compute(game.Session.World);
            game.Hud.ToggleHelp(); game.Hud.SetReducedMotion(true);
            yield return new WaitForSecondsRealtime(0.09f);
            SurvivalPresentationTests.Capture(game, Path.Combine(folder, "reduced-help-1280.png"), 1280, 720);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            Assert.That(game.Hud.GetComponentsInChildren<HudPulse>(true).All(p => !p.raycastTarget), Is.True);
        }

        [UnityTest]
        public IEnumerator Instruments_WeatherSupplyAndDayProgress_EmphasizeOnlyTransitions()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            yield return new WaitForSecondsRealtime(0.3f);
            ulong hash = StateHasher.Compute(game.Session.World);
            var original = game.Session.ReadModel.Environment;
            var fixture = WorldFactory.Create(new WorldConfigData(42, 32, 32, 5, GameCalendar.WinterStartTick + 720, 2, new[] { 1 }, 14));
            var environmentProperty = typeof(WorldReadModel).GetProperty("Environment").GetSetMethod(true);
            var ctor = typeof(EnvironmentSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var refresh = typeof(GodHud).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                Set(fixture.Environment, "Weather", WeatherKind.Clear); Set(fixture.Environment, "Temperature", 100);
                environmentProperty.Invoke(game.Session.ReadModel, new object[] { ctor.Invoke(new object[] { fixture }) });
                refresh.Invoke(game.Hud, null);
                var weather = Find<HudPulse>(game, "EnvironmentFeedback");
                int weatherCount = weather.PulseCount;
                Set(fixture.Environment, "Weather", WeatherKind.Blizzard); Set(fixture.Environment, "Temperature", -1500);
                foreach (var npc in fixture.Entities.Npcs) { npc.Inventory.TryRemove(ItemKind.Food, npc.Inventory.Food); }
                foreach (var shelter in fixture.Survival.Shelters) { shelter.Inventory.TryRemove(ItemKind.Food, shelter.Inventory.Food); }
                fixture.Resources.Stockpile.TryRemove(ItemKind.Food, fixture.Resources.Stockpile.Food);
                fixture.Market.Inventory.TryRemove(ItemKind.Food, fixture.Market.Inventory.Food);
                var snapshot = (EnvironmentSnapshot)ctor.Invoke(new object[] { fixture });
                environmentProperty.Invoke(game.Session.ReadModel, new object[] { snapshot });
                var supply = Find<HudPulse>(game, "SupplyFeedback0"); int supplyCount = supply.PulseCount;
                refresh.Invoke(game.Hud, null);
                Assert.That(weather.PulseCount, Is.EqualTo(weatherCount + 1));
                Assert.That(supply.PulseCount, Is.EqualTo(supplyCount + 1));
                Assert.That(Find<Text>(game, "StockValue0").text, Is.EqualTo("0"));
                refresh.Invoke(game.Hud, null);
                Assert.That(weather.PulseCount, Is.EqualTo(weatherCount + 1));
                Assert.That(supply.PulseCount, Is.EqualTo(supplyCount + 1));
                game.Runner.SetSpeed(8); game.Runner.Pause();
                yield return new WaitForSecondsRealtime(0.65f);
                Assert.That(weather.IsPulsing || supply.IsPulsing, Is.False);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                game.Hud.SetReducedMotion(true);
                yield return null;
                var mark = game.Hud.GetComponentsInChildren<Image>(true).First(i => i.name == "SupplyMark0" && i.transform.parent.name == "SupplyButton0");
                Assert.That(mark.color, Is.EqualTo(HudTheme.Danger));
                environmentProperty.Invoke(game.Session.ReadModel, new object[] { original });
                long target = game.Session.World.Campaign.StartTick + 3 * GameCalendar.TicksPerDay;
                game.Session.EnqueueCommand(new JumpCommand(target - 1)); game.Runner.StepOnce();
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.GetComponentsInChildren<HudPulse>(true).Where(p => p.name.StartsWith("DayFeedback")).Sum(p => p.PulseCount), Is.EqualTo(1));
                Assert.That(game.Hud.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("DayMark") && i.color == HudTheme.Gold), Is.EqualTo(3));
            }
            finally { environmentProperty.Invoke(game.Session.ReadModel, new object[] { original }); }
        }

        [Test]
        public void EcologyRows_ReorderingAndHiddenChanges_DoNotCreateFalseEvents()
        {
            var root = new GameObject("StableRows", typeof(RectTransform));
            try
            {
                Type type = typeof(GodHud).Assembly.GetType("MyAiWorld.Presentation.UI.HudEntries");
                object entries = Activator.CreateInstance(type, new object[] { root.transform, "Rows" });
                Action begin = () => type.GetMethod("Begin").Invoke(entries, null);
                Action end = () => type.GetMethod("End").Invoke(entries, null);
                Action<string, long, bool> row = (key, value, animate) =>
                {
                    type.GetMethod("Add").Invoke(entries, new object[] { key, value.ToString(), null, -1f, null });
                    type.GetMethod("ObserveLast").Invoke(entries, new object[] { key, value, animate, null });
                };
                begin(); row("rabbit", 10, true); row("deer", 8, true); end();
                begin(); row("deer", 8, true); row("rabbit", 10, true); end();
                Assert.That(root.GetComponentsInChildren<HudPulse>().Sum(p => p.PulseCount), Is.Zero);
                begin(); row("deer", 9, false); row("rabbit", 11, false); end();
                begin(); row("deer", 9, true); row("rabbit", 11, true); end();
                Assert.That(root.GetComponentsInChildren<HudPulse>().Sum(p => p.PulseCount), Is.Zero);
                begin(); row("deer", 10, true); row("rabbit", 11, true); end();
                Assert.That(root.GetComponentsInChildren<HudPulse>().Sum(p => p.PulseCount), Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property).SetValue(target, value);

        private sealed class JumpCommand : IWorldCommand
        {
            private readonly long _tick;
            public JumpCommand(long tick) { _tick = tick; }
            public string Describe() => "HUD motion fixture: cross three days";
            public bool Apply(WorldState world, CommandContext context)
            {
                world.Tick = _tick;
                foreach (var npc in world.Entities.Npcs)
                {
                    npc.Needs.Health = 10000;
                    npc.Needs.Hunger = npc.Needs.Thirst = npc.Needs.Fatigue = 9000;
                }
                return true;
            }
        }

        private static T Find<T>(GameBootstrap game, string name, bool child = false) where T : Component
        {
            if (child)
            {
                return game.Hud.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).GetComponentInChildren<T>(true);
            }
            return game.Hud.GetComponentsInChildren<T>(true).Single(t => t.name == name);
        }

        private static void ChangeBirths(GameBootstrap game)
        {
            game.Session.EnqueueCommand(new BirthCounterCommand()); game.Runner.StepOnce();
        }

        private sealed class BirthCounterCommand : IWorldCommand
        {
            public string Describe() => "HUD motion fixture: ecology change";
            public bool Apply(WorldState world, CommandContext context)
            {
                typeof(EcologyState).GetProperty("Births").SetValue(world.Ecology, world.Ecology.Births + 1);
                return true;
            }
        }
    }
}
