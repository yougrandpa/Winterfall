using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.UI;
using MyAiWorld.Sim.Ai;
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
    public sealed class WinterCampaignPresentationTests
    {
        [UnityTest]
        public IEnumerator Extinction_ModalLocksControls_ConfirmationContinuesWithoutAdvancingTime_AndLoadRelocks()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "WinterCampaignPresentation-" + Guid.NewGuid().ToString("N"));
            try
            {
                LoadScenario(game, folder, 14, 5);
                yield return new WaitForSecondsRealtime(0.25f);
                CampaignSnapshot initial = game.Session.ReadModel.Campaign;
                EnvironmentSnapshot initialEnvironment = game.Session.ReadModel.Environment;
                Assert.That(game.Hud.CampaignModalOpen, Is.False);
                Assert.That(game.Hud.CampaignSummaryText, Does.Contain("01 / 14"));
                AssertImmutableDtos();
                Assert.Throws<NotSupportedException>(() => ((IList<EntityId>)initial.InitialNpcs)[0] = EntityId.None);
                AssertReserves(game.Session);

                game.Session.EnqueueCommand(new DownAllCommand());
                game.Runner.StepOnce();
                Assert.That(game.Session.ReadModel.Campaign.Downed, Is.EqualTo(5));
                Assert.That(game.Session.ReadModel.Campaign.Result, Is.Null);
                game.Selection.Select(game.Session.ReadModel.Npcs[0].Id);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.NpcSummaryOpen, Is.True);
                Assert.That(game.Hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "SummaryAction").text, Does.Contain("倒地"));
                for (int i = 0; i <= SurvivalRules.RescueWindow; i++) { game.Runner.StepOnce(); }
                yield return new WaitForSecondsRealtime(0.25f);

                CampaignSnapshot locked = game.Session.ReadModel.Campaign;
                CampaignResultSnapshot score = locked.Result;
                Assert.That(locked.IsLocked, Is.True);
                Assert.That(score.Grade, Is.EqualTo("F"));
                Assert.That(score.Reason, Is.EqualTo("all_initial_npcs_dead"));
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Assert.That(game.Hud.CampaignSummaryText, Does.Contain("01 / 14"));
                Assert.That(game.Hud.CampaignText, Does.Contain("原始名单").And.Contain("初始 5").And.Contain("实际食物"));
                Assert.That(game.Hud.CampaignText, Does.Contain("倒地 0").And.Contain("恢复 0").And.Contain("无干预"));
                Assert.That(game.Hud.EventText, Does.Contain("寒冬挑战结束"));
                var resultBody = game.Hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "CampaignResultBody");
                Assert.That(resultBody.gameObject.activeSelf, Is.False);
                string visualFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/TestResults/WinterHudVisual"));
                Directory.CreateDirectory(visualFolder);
                SurvivalPresentationTests.Capture(game, Path.Combine(visualFolder, "winter-settlement-1920.png"), 1920, 1080);
                Click(game, FindButton(game, "ToggleCampaignDetails"));
                Assert.That(resultBody.gameObject.activeSelf, Is.True);
                Assert.That(resultBody.text, Does.Contain("实际食物").And.Contain("成绩已永久冻结"));
                AssertResult(score, game.Session.World.Campaign.Result);
                Assert.That(initial.Phase, Is.EqualTo(CampaignPhase.SurvivalActive));
                Assert.That(initial.Survivors, Is.EqualTo(5));
                Assert.That(initial.Result, Is.Null);
                Assert.That(initialEnvironment.Living, Is.EqualTo(5));
                Assert.That(initialEnvironment, Is.Not.SameAs(game.Session.ReadModel.Environment));

                ulong hash = StateHasher.Compute(game.Session.World);
                long tick = game.Session.World.Tick;
                string lockedPath = Path.Combine(folder, "locked.json");
                Assert.That(game.Session.Save(lockedPath), Is.True, game.Session.SaveStatus);
                Assert.That(game.Hud.ContinueObservationButton.IsInteractable(), Is.True);
                Assert.That(FindButton(game, "CloseCampaignResultButton").gameObject.activeSelf, Is.False);
                foreach (string name in new[] { "PauseButton", "Speedx1", "Speedx2", "Speedx4", "Speedx8", "StepButton" })
                {
                    Assert.That(FindButton(game, name).IsInteractable(), Is.False, name);
                }
                game.Hud.CloseTopmostPanel();
                game.Hud.CloseCampaignResult();
                game.Hud.ConfirmContinueObservation(); // No confirmation has been requested.
                game.Runner.Resume(); game.Runner.TogglePause(); game.Runner.SetSpeed(8); game.Runner.StepOnce();
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Assert.That(game.Session.World.Tick, Is.EqualTo(tick));
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                Assert.That(game.Runner.IsPaused, Is.True);

                Click(game, game.Hud.ContinueObservationButton);
                Assert.That(game.Hud.ContinueObservationConfirmationOpen, Is.True);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                yield return null;
                // Batchmode has no render loop; pump the canvas once before the next hit test.
                if (Camera.main != null) { Camera.main.Render(); }
                Canvas.ForceUpdateCanvases();
                Click(game, FindButton(game, "CancelContinueObservationButton"));
                Assert.That(game.Hud.ContinueObservationConfirmationOpen, Is.False);
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Click(game, game.Hud.ContinueObservationButton);
                yield return null;
                Click(game, game.Hud.ConfirmContinueObservationButton);
                Assert.That(game.Runner.IsPaused, Is.False);
                game.Runner.Pause(); // Assert the zero-minute command before any frame can advance observation.
                Assert.That(game.Hud.CampaignModalOpen, Is.False);
                Assert.That(game.Session.World.Tick, Is.EqualTo(tick));
                Assert.That(game.Session.ReadModel.Campaign.Phase, Is.EqualTo(CampaignPhase.Observation));
                Assert.That(StateHasher.Compute(game.Session.World), Is.Not.EqualTo(hash), "Only the phase unlock changes the hash.");
                Assert.That(locked.Phase, Is.EqualTo(CampaignPhase.SettlementLocked));
                AssertResult(score, game.Session.World.Campaign.Result);
                game.Runner.StepOnce();
                Assert.That(game.Session.World.Tick, Is.EqualTo(tick + 1));
                AssertResult(score, game.Session.World.Campaign.Result);
                ulong observationHash = StateHasher.Compute(game.Session.World);
                Click(game, FindButton(game, "CampaignResultButton"));
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Assert.That(game.Hud.CampaignText, Does.Contain("成绩已永久冻结"));
                game.Hud.CloseTopmostPanel();
                Assert.That(game.Hud.CampaignModalOpen, Is.False);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(observationHash));

                Assert.That(game.Runner.Load(lockedPath), Is.True, game.Session.SaveStatus);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.CampaignModalOpen, Is.True, "A loaded settlement must automatically reopen the modal.");
                Assert.That(game.Hud.ContinueObservationConfirmationOpen, Is.False);
                Assert.That(game.Runner.IsPaused, Is.True);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                AssertResult(game.Session.ReadModel.Campaign.Result, game.Session.World.Campaign.Result);
            }
            finally
            {
                game.Runner.Pause();
                if (Directory.Exists(folder)) { Directory.Delete(folder, true); }
            }
        }

        [UnityTest]
        public IEnumerator ChallengeDay_UsesStartRelativeHalfOpenDays_AndDeadlinePreservesInitialRosterAndScore()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "WinterDeadlinePresentation-" + Guid.NewGuid().ToString("N"));
            try
            {
                LoadScenario(game, folder, 14, 5);
                long start = game.Session.World.Campaign.StartTick;
                CampaignSnapshot initial = game.Session.ReadModel.Campaign;
                long midnight = (start / GameCalendar.TicksPerDay + 1) * GameCalendar.TicksPerDay;
                game.Session.EnqueueCommand(new JumpAndRestCommand(midnight - 1)); game.Runner.StepOnce();
                Assert.That(game.Session.World.Date.MinuteOfDay, Is.Zero);
                Assert.That(game.Session.ReadModel.Campaign.ChallengeDay, Is.EqualTo(1));
                game.Session.EnqueueCommand(new JumpAndRestCommand(start + GameCalendar.TicksPerDay - 2)); game.Runner.StepOnce();
                Assert.That(game.Session.ReadModel.Campaign.ChallengeDay, Is.EqualTo(1));
                game.Runner.StepOnce();
                Assert.That(game.Session.ReadModel.Campaign.ChallengeDay, Is.EqualTo(2));
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Hud.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("DayMark") && i.color == HudTheme.Gold), Is.EqualTo(1));
                Assert.That(initial.ChallengeDay, Is.EqualTo(1));

                game.Session.EnqueueCommand(new SpawnNpcCommand());
                game.Session.EnqueueCommand(new JumpAndRestCommand(game.Session.World.Campaign.DeadlineTick - 2));
                game.Runner.StepOnce();
                Assert.That(game.Session.ReadModel.Campaign.ChallengeDay, Is.EqualTo(14));
                Assert.That(game.Session.ReadModel.Campaign.RemainingMinutes, Is.EqualTo(1));
                Assert.That(game.Session.ReadModel.Campaign.InitialNpcs.Count, Is.EqualTo(5));
                Assert.That(game.Session.ReadModel.NpcCount, Is.EqualTo(6));
                game.Runner.StepOnce();
                yield return new WaitForSecondsRealtime(0.25f);
                CampaignSnapshot campaign = game.Session.ReadModel.Campaign;
                Assert.That(campaign.IsLocked, Is.True);
                Assert.That(campaign.RemainingMinutes, Is.Zero);
                Assert.That(game.Hud.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("DayMark") && i.color == HudTheme.Gold), Is.EqualTo(14));
                Assert.That(campaign.ChallengeDay, Is.EqualTo(14));
                Assert.That(campaign.Result.EndedTick, Is.EqualTo(campaign.DeadlineTick));
                Assert.That(campaign.Result.Grade, Is.EqualTo("S"));
                Assert.That(campaign.Result.InitialCount, Is.EqualTo(5));
                Assert.That(campaign.Result.Survivors, Is.EqualTo(5));
                Assert.That(campaign.Result.Assisted, Is.True);
                Assert.That(campaign.Result.Reason, Is.EqualTo("deadline_reached"));
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Assert.That(game.Hud.CampaignText, Does.Contain("有干预").And.Contain("健康活动 5"));
                AssertResult(campaign.Result, game.Session.World.Campaign.Result);
                AssertReserves(game.Session);
                ulong hash = StateHasher.Compute(game.Session.World);
                game.Hud.RequestContinueObservation(); game.Hud.CloseTopmostPanel();
                Assert.That(game.Hud.ContinueObservationConfirmationOpen, Is.False);
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
            finally
            {
                game.Runner.Pause();
                if (Directory.Exists(folder)) { Directory.Delete(folder, true); }
            }
        }

        [UnityTest]
        public IEnumerator UnlimitedObservation_DoesNotShowSettlement_AndChineseEnumCatalogsAreComplete()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "WinterObservationPresentation-" + Guid.NewGuid().ToString("N"));
            try
            {
                LoadScenario(game, folder, 0, 0);
                game.Runner.StepOnce(); yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Session.ReadModel.Campaign.IsUnlimited, Is.True);
                Assert.That(game.Session.ReadModel.Campaign.Result, Is.Null);
                Assert.That(game.Hud.CampaignSummaryText, Does.Contain("无期限"));
                Assert.That(game.Hud.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("DayMark") && i.gameObject.activeSelf), Is.Zero);
                ulong hash = StateHasher.Compute(game.Session.World);
                game.Hud.ShowCampaignResult();
                Assert.That(game.Hud.CampaignModalOpen, Is.False);
                Assert.That(game.Hud.ContinueObservationButton.gameObject.activeInHierarchy, Is.False);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                Assert.That(game.Session.ReadModel.Environment.Food.Status, Is.EqualTo(SupplyStatus.NoDemand));
                Assert.That(game.Session.ReadModel.Environment.Water.Status, Is.EqualTo(SupplyStatus.NoDemand));
                Assert.That(game.Session.ReadModel.Environment.Fuel.Status, Is.EqualTo(SupplyStatus.NoDemand));
                foreach (ItemKind item in Enum.GetValues(typeof(ItemKind)))
                { if (item != ItemKind.Count) { Assert.That(ChineseText.Item(item), Is.Not.Empty); } }
                foreach (PlanStepType step in Enum.GetValues(typeof(PlanStepType))) { Assert.That(ChineseText.Step(step), Is.Not.Empty); }
                foreach (NpcGoalType goal in Enum.GetValues(typeof(NpcGoalType))) { Assert.That(ChineseText.Goal(goal), Is.Not.Empty); }
                Assert.That(ChineseText.Conditions(PlanStepType.Hunt), Does.Not.Contain("保护数量"));
                AssertReserves(game.Session);
            }
            finally
            {
                game.Runner.Pause();
                if (Directory.Exists(folder)) { Directory.Delete(folder, true); }
            }
        }

        [UnityTest]
        public IEnumerator WinterHud_ThreeResolutions_LeavesWorldOpenAndCapturesDayNightAndDetails()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/TestResults/WinterHudVisual"));
            Directory.CreateDirectory(folder);
            long noon = game.Session.World.Tick / GameCalendar.TicksPerDay * GameCalendar.TicksPerDay + 720;
            game.Session.EnqueueCommand(new JumpAndRestCommand(noon - 1)); game.Runner.StepOnce();
            foreach (int width in new[] { 1280, 1920, 2560 })
            {
                int height = width * 9 / 16;
#if UNITY_EDITOR
                Type.GetType("MyAiWorld.EditorTools.ReviewGameView,MyAiWorld.Editor").GetMethod("TrySetResolution")
                    .Invoke(null, new object[] { width, height });
#else
                Screen.SetResolution(width, height, false);
#endif
                yield return new WaitForSecondsRealtime(0.3f);
                game.Hud.CloseGlobalPanels(); game.Hud.CloseInspector();
                yield return new WaitForSecondsRealtime(0.2f);
                Canvas.ForceUpdateCanvases();
                var rects = game.Hud.GetComponentsInChildren<RectTransform>();
                string[] groups = { "ChallengeInstrument", "EnvironmentInstrument", "SupplyInstrument", "TimeControls", "Navigation", "HelpButton", "ReturnToVillage" };
                float area = 0;
                foreach (string name in groups)
                {
                    Rect bounds = HudBounds(rects.Single(r => r.name == name));
                    area += bounds.width * bounds.height;
                    Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(0), name);
                    Assert.That(bounds.xMax, Is.LessThanOrEqualTo(Screen.width), name);
                    Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(0), name);
                    Assert.That(bounds.yMax, Is.LessThanOrEqualTo(Screen.height), name);
                }
                Assert.That(area / (Screen.width * (float)Screen.height), Is.LessThan(0.1f));
                foreach (Vector2 point in new[] { new Vector2(.5f, .5f), new Vector2(.5f, .96f), new Vector2(.5f, .05f), new Vector2(.95f, .5f) })
                {
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
                        { position = new Vector2(Screen.width * point.x, Screen.height * point.y) }, hits);
                    Assert.That(hits, Is.Empty, "Unoccupied HUD space must pass map input: " + point);
                }
                var dayLabel = FindButton(game, "CampaignResultButton").GetComponentInChildren<Text>();
                Assert.That(dayLabel.text, Does.Contain("/ 14"));
                Assert.That(dayLabel.rectTransform.rect.height, Is.GreaterThanOrEqualTo(dayLabel.preferredHeight), "Challenge day must render inside its button.");
                ulong hash = StateHasher.Compute(game.Session.World);
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "winter-day-" + width + ".png"), width, height);
                game.Selection.Select(game.Session.ReadModel.Npcs[0].Id);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(game.Hud.NpcSummaryOpen, Is.True);
                Assert.That(game.Hud.InspectorOpen, Is.False);
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "winter-summary-" + width + ".png"), width, height);
                Click(game, FindButton(game, "OpenNpcDetails"));
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(game.Hud.InspectorOpen, Is.True);
                Assert.That(game.Hud.NpcSummaryOpen, Is.False);
                Assert.That(game.Hud.GetComponentsInChildren<Image>(true).Single(i => i.name == "Portrait").sprite, Is.Not.Null);
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "winter-inspector-" + width + ".png"), width, height);
                Click(game, FindButton(game, "ResourcesButton"));
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(game.Hud.InspectorOpen, Is.False);
                var guide = game.Hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "SupplyEstimateGuide");
                Assert.That(guide.gameObject.activeSelf, Is.False);
                var scroll = guide.GetComponentInParent<ScrollRect>();
                scroll.verticalNormalizedPosition = 0;
                yield return null;
                Click(game, FindButton(game, "ToggleSupplyEstimate"));
                Assert.That(guide.gameObject.activeSelf, Is.True);
                Assert.That(guide.text, Does.Contain("游戏估算"));
                Click(game, FindButton(game, "ToggleSupplyEstimate"));
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "winter-resources-" + width + ".png"), width, height);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
            game.Hud.CloseGlobalPanels(); game.Hud.CloseInspector();
            game.Session.EnqueueCommand(new JumpAndRestCommand(noon + 600 - 1)); game.Runner.StepOnce();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(game.Session.ReadModel.Environment.IsDaytime, Is.False);
            SurvivalPresentationTests.Capture(game, Path.Combine(folder, "winter-night-2560.png"), 2560, 1440);
        }

        [UnityTest]
        public IEnumerator WinterHud_SupplyStates_AreLegibleAndDoNotMutateWorld()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            ulong hash = StateHasher.Compute(game.Session.World);
            EnvironmentSnapshot original = game.Session.ReadModel.Environment;
            try
            {
                foreach (int reserve in new[] { 100, 1, 0, -1 })
                {
                    // Independent observation fixtures exercise binding without writing to the running world.
                    var world = WorldFactory.Create(new WorldConfigData(42, 32, 32, reserve < 0 ? 0 : 5, GameCalendar.WinterStartTick + 720, 2, new[] { 1 }, reserve < 0 ? 0 : 14));
                    var stores = new HashSet<InventoryState> { world.Resources.Stockpile, world.Market.Inventory };
                    foreach (var npc in world.Entities.Npcs) { stores.Add(npc.Inventory); }
                    foreach (var shelter in world.Survival.Shelters) { stores.Add(shelter.Inventory); }
                    foreach (var inventory in stores)
                    {
                        inventory.TryRemove(ItemKind.Food, inventory.Count(ItemKind.Food));
                    }
                    if (reserve > 0) { Assert.That(world.Resources.Stockpile.TryAdd(ItemKind.Food, reserve), Is.True); }
                    var ctor = typeof(EnvironmentSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
                    var snapshot = (EnvironmentSnapshot)ctor.Invoke(new object[] { world });
                    typeof(WorldReadModel).GetProperty("Environment").GetSetMethod(true).Invoke(game.Session.ReadModel, new object[] { snapshot });
                    yield return new WaitForSecondsRealtime(0.25f);
                    var label = game.Hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "SupplySummary0");
                    var value = game.Hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "StockValue0");
                    Assert.That(value.text, Is.EqualTo(snapshot.Food.Reserve.ToString()));
                    Assert.That(label.text, Does.Contain(ChineseText.Supply(snapshot.Food.Status)));
                    if (reserve == 0)
                    {
                        Assert.That(label.color, Is.EqualTo(HudTheme.Danger));
                        var marks = game.Hud.GetComponentsInChildren<Image>(true).Where(i => i.name.StartsWith("SupplyMark") && i.transform.parent.name == "SupplyButton0").ToArray();
                        Assert.That(marks.Count(i => i.color.r == HudTheme.Danger.r && i.color.g == HudTheme.Danger.g &&
                            i.color.b == HudTheme.Danger.b && i.color.a >= 0.8f), Is.EqualTo(4));
                        Assert.That(marks.Count(i => i.color == Color.clear), Is.EqualTo(4));
                    }
                    if (reserve == 1) { Assert.That(label.color, Is.EqualTo(HudTheme.Gold)); }
                    if (reserve < 0) { Assert.That(label.text, Is.EqualTo("无需供给")); }
                    Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                }
            }
            finally { typeof(WorldReadModel).GetProperty("Environment").GetSetMethod(true).Invoke(game.Session.ReadModel, new object[] { original }); }
        }

        private static Rect HudBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void LoadScenario(GameBootstrap game, string folder, int days, int count)
        {
            var config = new WorldConfigData(42, 32, 32, count, GameCalendar.WinterStartTick + 17, 2, new[] { 1, 2, 4, 8 }, days);
            string path = Path.Combine(folder, "initial.json");
            using (var session = new GameSession(config))
            {
                session.SaveDirectory = folder;
                Assert.That(session.Save(path), Is.True, session.SaveStatus);
            }
            game.Session.SaveDirectory = folder;
            Assert.That(game.Runner.Load(path), Is.True, game.Session.SaveStatus);
            game.Runner.Pause();
        }

        private sealed class DownAllCommand : IWorldCommand
        {
            public string Describe() => "Controlled campaign extinction: down every NPC at the command boundary";
            public bool Apply(WorldState world, CommandContext context)
            {
                foreach (var npc in world.Entities.Npcs)
                {
                    npc.Needs.Health = 0;
                    npc.Action.SetIdle("Controlled extinction", long.MaxValue);
                }
                return true;
            }
        }

        private sealed class JumpAndRestCommand : IWorldCommand
        {
            private readonly long _tick;
            public JumpAndRestCommand(long tick) { _tick = tick; }
            public string Describe() => "Controlled campaign boundary at tick " + _tick;
            public bool Apply(WorldState world, CommandContext context)
            {
                world.Tick = _tick;
                foreach (var npc in world.Entities.Npcs)
                {
                    npc.Position = npc.Home;
                    npc.Needs.Health = 10000;
                    npc.Needs.Hunger = npc.Needs.Thirst = npc.Needs.Fatigue = 9000;
                    npc.Action.SetIdle("Controlled deadline", long.MaxValue);
                }
                return true;
            }
        }

        private static void AssertImmutableDtos()
        {
            foreach (Type type in new[] { typeof(CampaignSnapshot), typeof(CampaignResultSnapshot), typeof(EnvironmentSnapshot), typeof(SupplyEstimateSnapshot) })
            {
                Assert.That(type.IsSealed, Is.True);
                foreach (var property in type.GetProperties()) { Assert.That(property.GetSetMethod(true), Is.Null, type.Name + "." + property.Name); }
            }
        }

        private static void AssertResult(CampaignResultSnapshot actual, CampaignResult expected)
        {
            foreach (var property in typeof(CampaignResult).GetProperties())
            {
                var copied = typeof(CampaignResultSnapshot).GetProperty(property.Name);
                Assert.That(copied, Is.Not.Null, "Every result field must be copied.");
                Assert.That(copied.GetValue(actual), Is.EqualTo(property.GetValue(expected)), property.Name);
            }
        }

        private static void AssertReserves(GameSession session)
        {
            var world = session.World;
            var snapshot = session.ReadModel.Environment;
            var stores = new HashSet<InventoryState> { world.Resources.Stockpile, world.Market.Inventory };
            foreach (var f in world.Survival.Facilities) { stores.Add(f.Inventory); }
            foreach (var g in world.Survival.Ground) { stores.Add(g.Inventory); }
            foreach (var s in world.Survival.Shelters) { stores.Add(s.Inventory); }
            foreach (var npc in world.Entities.Npcs) { if (npc.Survival.Life != LifeState.Dead) { stores.Add(npc.Inventory); } }
            Assert.That(snapshot.FoodReserve, Is.EqualTo(stores.Sum(i => (long)i.Count(ItemKind.Food))));
            Assert.That(snapshot.WaterReserve, Is.EqualTo(stores.Sum(i => (long)i.Count(ItemKind.Water))));
            Assert.That(snapshot.FuelReserve, Is.EqualTo(stores.Sum(i => (long)i.Count(ItemKind.Wood))));
            Assert.That(snapshot.Temperature, Is.EqualTo(world.Environment.Temperature));
            Assert.That(snapshot.Wind, Is.EqualTo(world.Environment.Wind));
            Assert.That(snapshot.Daylight, Is.EqualTo(world.Environment.Daylight));
            Assert.That(snapshot.SnowDepth, Is.EqualTo(world.Environment.SnowDepth));
            Assert.That(snapshot.Weather, Is.EqualTo(world.Environment.Weather));
            Assert.That(snapshot.IsDaytime, Is.EqualTo(world.Environment.IsDaytime));
            Assert.That(snapshot.WaterFrozen, Is.EqualTo(world.Environment.WaterFrozen));
            if (snapshot.Living > 0)
            {
                Assert.That(snapshot.Food.Minutes, Is.EqualTo(snapshot.FoodReserve * 4500 / (snapshot.Living * 6L)));
                Assert.That(snapshot.Water.Minutes, Is.EqualTo(snapshot.WaterReserve * 5500 / (snapshot.Living * 9L)));
            }
            if (snapshot.OccupiedShelters > 0)
            {
                Assert.That(snapshot.Fuel.Minutes, Is.EqualTo((snapshot.FuelReserve * SurvivalRules.FuelTicksPerWood + snapshot.RemainingHeatingMinutes) / snapshot.OccupiedShelters));
            }
        }

        private static Button FindButton(GameBootstrap game, string name) =>
            game.Hud.GetComponentsInChildren<Button>(true).First(button => button.name == name);

        private static void Click(GameBootstrap game, Button button)
        {
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            var raycaster = button.GetComponentInParent<GraphicRaycaster>();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            // Batchmode never assigns a canvas depth to newly activated graphics, so
            // their raycast target cannot be resolved there. Rendered runs still use
            // the full hit test below; headless runs invoke the same button handler.
            if (button.GetComponent<CanvasRenderer>().absoluteDepth < 0)
            {
                button.onClick.Invoke();
                return;
            }
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), "The button must not be covered.");
            pointer.pointerCurrentRaycast = hits[0];
            Assert.That(ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler), Is.SameAs(button.gameObject));
        }
    }
}
