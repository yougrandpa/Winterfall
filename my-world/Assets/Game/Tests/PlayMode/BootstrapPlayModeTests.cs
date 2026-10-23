using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Presentation.UI;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed partial class BootstrapPlayModeTests
    {
        [Test]
        public void NpcView_FinalHorizontalSegment_UsesDirectionalWalkUntilArrival()
        {
            var root = new GameObject("NpcAnimationRegression");
            try
            {
                PastoralArtConfig art = PastoralArtConfig.Load();
                var view = root.AddComponent<NpcView>();
                var id = new EntityId(1);
                view.Initialize(id, art.Get("npc_0_0_0_0"), null);
                view.Configure(art, "Farmer", 0f);
                view.SetCell(new GridPos(2, 3), true, 0);
                // CompleteStep already changed the authoritative action to Idle.
                var right = new NpcSnapshot(id, "Test", "Farmer", new GridPos(3, 3), "Arrived");
                view.Present(right, 1, 0.25f, 1f);
                Assert.That(view.CurrentSprite.name, Does.StartWith("npc_0_1_1_"));
                Sprite first = view.CurrentSprite;
                view.Present(right, 1, 0.75f, 1.125f);
                Assert.That(view.CurrentSprite, Is.Not.SameAs(first), "Walking must advance animation frames.");
                view.Present(right, 2, 0f, 1.25f);
                Assert.That(view.CurrentSprite.name, Does.StartWith("npc_0_1_0_"));

                var left = new NpcSnapshot(id, "Test", "Farmer", new GridPos(2, 3), "Moving", NpcActionType.Move);
                view.Present(left, 3, 0.5f, 1.5f);
                Assert.That(view.CurrentSprite.name, Does.StartWith("npc_0_3_1_"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CreatureMotion_StationaryTicks_DoNotReplayPreviousMove()
        {
            var motion = new CreatureMotion();
            motion.SetCell(new GridPos(2, 3), true, 0);
            motion.SetCell(new GridPos(3, 3), false, 1);
            motion.Sample(0.9f);

            for (long tick = 2; tick <= 5; tick++)
            {
                motion.SetCell(new GridPos(3, 3), false, tick);
                foreach (float alpha in new[] { 0f, 0.25f, 0.75f, 1f })
                {
                    Assert.That(motion.Sample(alpha), Is.EqualTo(new Vector3(3.5f, 3.5f, 0f)),
                        "A stationary tick must not rewind and replay an old movement segment.");
                    Assert.That(motion.Moving, Is.False);
                }
            }
        }

        [Test]
        public void CreatureMotion_RepeatedFrames_KeepTheSameInterpolationEndpoints()
        {
            var motion = new CreatureMotion();
            motion.SetCell(new GridPos(2, 3), true, 0);
            motion.SetCell(new GridPos(3, 3), false, 1);
            Assert.That(motion.Sample(0.25f).x, Is.EqualTo(2.75f));
            motion.SetCell(new GridPos(3, 3), false, 1);
            Assert.That(motion.Sample(0.75f).x, Is.EqualTo(3.25f));
            Assert.That(motion.Moving, Is.True);
        }

        [Test]
        public void CreatureMotion_ConsecutiveMoves_StartAtPreviousTickCell()
        {
            var motion = new CreatureMotion();
            motion.SetCell(new GridPos(2, 3), true, 0);
            motion.SetCell(new GridPos(3, 3), false, 1);
            motion.Sample(0.9f);
            motion.SetCell(new GridPos(4, 3), false, 2);
            Assert.That(motion.Sample(0f), Is.EqualTo(new Vector3(3.5f, 3.5f, 0f)));
            Assert.That(motion.Sample(0.5f), Is.EqualTo(new Vector3(4f, 3.5f, 0f)));
            Assert.That(motion.Direction, Is.EqualTo(1));
        }

        [Test]
        public void CreatureMotion_InstantPlacement_DiscardsPreviousMove()
        {
            var motion = new CreatureMotion();
            motion.SetCell(new GridPos(2, 3), true, 0);
            motion.SetCell(new GridPos(3, 3), false, 1);
            motion.Sample(0.5f);
            motion.SetCell(new GridPos(8, 9), true, 1);
            Assert.That(motion.Position, Is.EqualTo(new Vector3(8.5f, 9.5f, 0f)));
            Assert.That(motion.Moving, Is.False);
            Assert.That(motion.Sample(0f), Is.EqualTo(motion.Position));
            motion.SetCell(new GridPos(8, 9), false, 8);
            Assert.That(motion.Sample(0f), Is.EqualTo(new Vector3(8.5f, 9.5f, 0f)));
        }

        [UnityTest]
        public IEnumerator Bootstrap_CreatesWorldCameraAndNpcViews()
        {
            var root = new GameObject("BootstrapSmokeTest");
            var bootstrap = root.AddComponent<GameBootstrap>();

            yield return null;
            yield return null;

            Assert.That(bootstrap.Session, Is.Not.Null);
            Assert.That(bootstrap.Runner, Is.Not.Null);
            Assert.That(bootstrap.Presenter, Is.Not.Null);
            Assert.That(bootstrap.PlayerCamera, Is.Not.Null);
            Assert.That(bootstrap.Hud, Is.Not.Null);
            Assert.That(bootstrap.NpcViewCount, Is.EqualTo(20));
            Assert.That(Object.FindObjectOfType<EventSystem>(), Is.Not.Null);
            Text[] labels = Object.FindObjectsOfType<Text>();
            Assert.That(labels.Length, Is.GreaterThanOrEqualTo(8));
            for (int i = 0; i < labels.Length; i++)
            {
                Assert.That(labels[i].font, Is.Not.Null, "HUD text has no font.");
            }

            bootstrap.Runner.Pause();
            long pausedTick = bootstrap.Session.ReadModel.Tick;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(bootstrap.Session.ReadModel.Tick, Is.EqualTo(pausedTick));

            bootstrap.Runner.StepOnce();
            Assert.That(bootstrap.Session.ReadModel.Tick, Is.EqualTo(pausedTick + 1));

            bootstrap.Runner.SetSpeed(8);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(bootstrap.Session.ReadModel.Tick, Is.GreaterThan(pausedTick + 1));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootstrapScene_HasConfiguredGameBootstrap()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;

            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.ConfigAsset, Is.Not.Null);
            Assert.That(bootstrap.Session, Is.Not.Null);
            Assert.That(bootstrap.NpcViewCount, Is.EqualTo(20));
        }

        [UnityTest]
        public IEnumerator Bootstrap_SelectedNpcShowsSurvivalPlanInventoryAndNeedHistory()
        {
            var root = new GameObject("SurvivalHudSmokeTest");
            var bootstrap = root.AddComponent<GameBootstrap>();
            yield return null;
            yield return null;

            try
            {
                bootstrap.Runner.Pause();
                var readModel = bootstrap.Session.ReadModel;
                var id = readModel.Npcs[0].Id;
                ulong beforeSelection = readModel.StateHash;
                bootstrap.Selection.Select(id);
                yield return null;

                Assert.That(readModel.StateHash, Is.EqualTo(beforeSelection), "Selecting an NPC must not change simulation state.");
                Assert.That(readModel.ResourceNodes.Count, Is.GreaterThan(0));
                Assert.That(readModel.FarmPlots.Count, Is.GreaterThan(0));
                Assert.That(bootstrap.Presenter.ResourceNodeViewCount, Is.EqualTo(readModel.ResourceNodes.Count));
                Assert.That(bootstrap.Presenter.FarmPlotViewCount, Is.EqualTo(readModel.FarmPlots.Count));
                Assert.That(bootstrap.Hud.ResourceText, Is.Not.Empty);
                Assert.That(bootstrap.Hud.InspectorOpen, Is.True);
                Assert.That(bootstrap.Hud.InspectorText, Does.Contain("背包"));
                ClickHudButton(bootstrap, "PlanTab");
                Assert.That(bootstrap.Hud.InspectorTab, Is.EqualTo(2));
                Assert.That(bootstrap.Hud.InspectorText, Does.Contain("行动计划"));
                ClickHudButton(bootstrap, "OverviewTab");

                for (int i = 0; i < 800; i++)
                {
                    bootstrap.Runner.StepOnce();
                }

                yield return null;
                var details = readModel.GetDetails(id);
                Assert.That(details.Inventory, Is.Not.Empty);
                Assert.That(details.Goal, Is.Not.Empty);
                Assert.That(details.Plan, Is.Not.Empty);
                Assert.That(details.Food, Is.InRange(0, 10000));
                Assert.That(details.Water, Is.InRange(0, 10000));
                Assert.That(details.Energy, Is.InRange(0, 10000));
                Assert.That(details.NeedsHistory.Count, Is.InRange(2, 24));
                Assert.That(bootstrap.Hud.HistoryText, Is.Not.Empty);
            }
            finally
            {
                Object.Destroy(root);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator BootstrapHud_PointerRaycastsReachTimeAndSpawnControls()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;

            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            var runner = bootstrap.Runner;
            var session = bootstrap.Session;
            Assert.That(runner.IsPaused, Is.False);
            ClickHudButton(bootstrap, "PauseButton");
            Assert.That(runner.IsPaused, Is.True);
            long pausedTick = session.ReadModel.Tick;
            ulong pausedHash = StateHasher.Compute(session.World);

            ClickHudButton(bootstrap, "PauseButton");
            Assert.That(runner.IsPaused, Is.False, "Pause button should resume a paused session.");
            ClickHudButton(bootstrap, "PauseButton");
            Assert.That(runner.IsPaused, Is.True);
            Assert.That(session.ReadModel.Tick, Is.EqualTo(pausedTick));

            foreach (int speed in new[] { 1, 2, 4, 8 })
            {
                ClickHudButton(bootstrap, "Speedx" + speed);
                Assert.That(runner.SpeedMultiplier, Is.EqualTo(speed));
                Assert.That(FindHudButton(bootstrap, "Speedx" + speed).image.color, Is.EqualTo(HudTheme.Ink));
                Assert.That(runner.IsPaused, Is.False, "Choosing speed should resume the runner.");
                Assert.That(session.ReadModel.Tick, Is.EqualTo(pausedTick), "Speed selection must not execute a simulation tick.");
                Assert.That(StateHasher.Compute(session.World), Is.EqualTo(pausedHash));
                ClickHudButton(bootstrap, "PauseButton");
                Assert.That(runner.IsPaused, Is.True);
            }

            ClickHudButton(bootstrap, "StepButton");
            Assert.That(session.ReadModel.Tick, Is.EqualTo(pausedTick + 1));
            Assert.That(runner.IsPaused, Is.True, "Single step should preserve paused mode.");

            bootstrap.Hud.ToggleDebug();
            Assert.That(bootstrap.Hud.DebugVisible, Is.True, "F1 debug panel must contain the spawn control.");
            Button spawnButton = FindHudButton(bootstrap, "SpawnButton");
            Assert.That(spawnButton, Is.Not.Null);
            Assert.That(spawnButton.IsInteractable(), Is.True);
            Canvas.ForceUpdateCanvases();
            int npcCount = session.ReadModel.NpcCount;
            int appliedCommands = session.AppliedCommandCount;
            long beforeSpawnTick = session.ReadModel.Tick;
            ulong beforeSpawnHash = StateHasher.Compute(session.World);
            Assert.That(session.QueuedCommandCount, Is.Zero);
            // The debug panel is a separate overlay; exercise its command handler
            // directly so the test does not depend on Game View pixel size.
            spawnButton.onClick.Invoke();
            Assert.That(session.QueuedCommandCount, Is.EqualTo(1));
            Assert.That(session.ReadModel.NpcCount, Is.EqualTo(npcCount));
            Assert.That(session.World.Entities.Npcs.Count, Is.EqualTo(npcCount));
            Assert.That(session.World.Tick, Is.EqualTo(beforeSpawnTick));
            Assert.That(StateHasher.Compute(session.World), Is.EqualTo(beforeSpawnHash),
                "Spawn click must enqueue a command without changing authoritative state.");

            yield return null;
            Assert.That(session.QueuedCommandCount, Is.EqualTo(1), "Paused frames must not drain the command queue.");
            Assert.That(StateHasher.Compute(session.World), Is.EqualTo(beforeSpawnHash));
            ClickHudButton(bootstrap, "StepButton");
            Assert.That(session.ReadModel.Tick, Is.EqualTo(beforeSpawnTick + 1));
            Assert.That(session.QueuedCommandCount, Is.Zero);
            Assert.That(session.AppliedCommandCount, Is.EqualTo(appliedCommands + 1));
            Assert.That(session.ReadModel.NpcCount, Is.EqualTo(npcCount + 1));
            Assert.That(session.World.Entities.Npcs.Count, Is.EqualTo(npcCount + 1));
            yield return null;
            Assert.That(bootstrap.NpcViewCount, Is.EqualTo(npcCount + 1));
        }

        [UnityTest]
        public IEnumerator Bootstrap_EcologyEconomySnapshotsAndViews_UpdateWithoutMutatingOldSnapshots()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            bootstrap.Runner.Pause();
            var model = bootstrap.Session.ReadModel;
            var before = model.EcologyEconomy;
            var oldCell = before.Animals[0].Cell;
            int oldCount = before.Animals.Count;
            Assert.That(oldCount, Is.EqualTo(24));
            Assert.That(bootstrap.Presenter.AnimalViewCount, Is.EqualTo(oldCount));
            ulong hash = model.StateHash;
            bootstrap.Selection.Select(model.Npcs[2].Id);
            Assert.That(model.StateHash, Is.EqualTo(hash));
            Assert.That(bootstrap.Hud.InspectorText, Does.Contain("钱包"));
            Assert.That(bootstrap.Hud.InspectorText, Does.Contain("狩猎工具 弓"));
            for (int tick = 0; tick < 800; tick++)
            {
                bootstrap.Runner.StepOnce();
            }
            yield return new WaitForSecondsRealtime(0.25f);
            var after = model.EcologyEconomy;
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(before.Animals[0].Cell, Is.EqualTo(oldCell));
            Assert.That(before.Animals.Count, Is.EqualTo(oldCount));
            Assert.That(after.Filled, Is.GreaterThan(0));
            Assert.That(after.HuntKills, Is.GreaterThan(0));
            Assert.That(after.RecentTrades.Count, Is.GreaterThan(0));
            Assert.That(after.CurrencyError, Is.Zero);
            Assert.That(after.MaterialError, Is.Zero);
            Assert.That(bootstrap.Presenter.AnimalViewCount, Is.EqualTo(after.Animals.Count));
            Assert.That(bootstrap.Hud.EconomyText, Does.Contain("野兔"));
            Assert.That(bootstrap.Hud.EconomyText, Does.Contain("野猪"));
            Assert.That(bootstrap.Hud.EconomyText, Does.Contain("单价"));
            Assert.That(bootstrap.Hud.EconomyText, Does.Contain("成交"));
            Assert.That(bootstrap.Hud.EconomyText, Does.Not.Contain("守恒误差"));
            Assert.That(bootstrap.Hud.EconomyText, Does.Contain("#"));
            string log = System.IO.File.ReadAllText(bootstrap.Session.LogPath);
            Assert.That(log, Does.Contain("\"type\":\"trade\""));
            Assert.That(log, Does.Contain("\"walletBefore\""));
            Assert.That(log, Does.Contain("\"marketAfter\""));
        }

        [UnityTest]
        public IEnumerator Bootstrap_PanelsAndInspectorTransitions_DoNotMutateSimulation()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            bootstrap.Runner.Pause();
            ulong hash = StateHasher.Compute(bootstrap.Session.World);
            long tick = bootstrap.Session.ReadModel.Tick;

            bootstrap.Hud.ToggleGlobalPanel(0);
            Assert.That(bootstrap.Hud.OpenGlobalPanel, Is.EqualTo(0));
            yield return new WaitForSecondsRealtime(0.22f);
            Assert.That(StateHasher.Compute(bootstrap.Session.World), Is.EqualTo(hash));
            Assert.That(bootstrap.Session.ReadModel.Tick, Is.EqualTo(tick));

            bootstrap.Hud.ToggleGlobalPanel(1);
            Assert.That(bootstrap.Hud.OpenGlobalPanel, Is.EqualTo(1), "Only one global detail panel may be open.");
            bootstrap.Hud.ToggleGlobalPanel(1);
            Assert.That(bootstrap.Hud.OpenGlobalPanel, Is.EqualTo(-1));

            EntityId id = bootstrap.Session.ReadModel.Npcs[0].Id;
            bootstrap.Selection.Select(id);
            yield return new WaitForSecondsRealtime(0.22f);
            Assert.That(bootstrap.Hud.InspectorOpen, Is.True);
            Assert.That(StateHasher.Compute(bootstrap.Session.World), Is.EqualTo(hash));
            Assert.That(bootstrap.Session.ReadModel.Tick, Is.EqualTo(tick));

            bootstrap.Selection.Select(null);
            yield return new WaitForSecondsRealtime(0.22f);
            Assert.That(bootstrap.Hud.InspectorOpen, Is.False);
            Assert.That(bootstrap.Hud.InspectorText, Is.Empty);
            Assert.That(StateHasher.Compute(bootstrap.Session.World), Is.EqualTo(hash));
        }

        [UnityTest]
        public IEnumerator Bootstrap_WorldCellSelection_DoesNotOpenResourcePanel()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            bootstrap.Runner.Pause();
            GridPos center = bootstrap.Session.World.Center;

            bootstrap.Selection.SelectCell(center);
            yield return null;
            Assert.That(bootstrap.Hud.OpenGlobalPanel, Is.EqualTo(-1), "Selecting a world cell must not open a HUD panel.");

            bootstrap.Hud.ToggleGlobalPanel(0);
            bootstrap.Selection.SelectCell(bootstrap.Session.ReadModel.ResourceNodes[0].Cell);
            yield return new WaitForSecondsRealtime(0.22f);
            Assert.That(bootstrap.Hud.OpenGlobalPanel, Is.EqualTo(0), "An open resource panel should stay open.");
            Assert.That(bootstrap.Hud.ResourceText, Does.Contain("选中资源"));
        }

        [UnityTest]
        public IEnumerator Bootstrap_AnimationsFreezeWhenPausedAndUiTransitionsStillAnimate()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            bootstrap.Runner.Pause();
            // Let the first paused presentation sample settle before comparing frames.
            yield return null;
            float frozen = bootstrap.Runner.AnimationTime;
            NpcView view = bootstrap.Presenter.GetComponentInChildren<NpcView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.CurrentSprite, Is.Not.Null);
            Assert.That(view.CurrentSprite.name, Does.StartWith("npc_"));
            int frame = view.FrameIndex;
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(bootstrap.Runner.AnimationTime, Is.EqualTo(frozen).Within(0.001f),
                "Paused simulation must freeze world animation.");
            Assert.That(view.FrameIndex, Is.EqualTo(frame));

            bootstrap.Hud.ToggleGlobalPanel(0);
            yield return new WaitForSecondsRealtime(0.2f);
            PanelTransition transition = null;
            foreach (PanelTransition candidate in bootstrap.Hud.GetComponentsInChildren<PanelTransition>(true))
            {
                if (candidate.name == "ResourceOverview") { transition = candidate; break; }
            }

            Assert.That(transition, Is.Not.Null);
            Assert.That(transition.IsOpen, Is.True);
            CanvasGroup group = transition.GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.GreaterThan(0.5f), "UI transitions must run while the simulation is paused.");
            Assert.That(bootstrap.Runner.AnimationTime, Is.EqualTo(frozen).Within(0.001f));

            bootstrap.Runner.SetSpeed(2);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(bootstrap.Runner.AnimationTime, Is.GreaterThan(frozen));
            Assert.That(view.FrameIndex, Is.InRange(0, 3));
        }

        [UnityTest]
        public IEnumerator Bootstrap_Hud_ChineseGlyphsExistInBundledFont()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            bootstrap.Runner.Pause();
            bootstrap.Selection.Select(bootstrap.Session.ReadModel.Npcs[0].Id);
            bootstrap.Hud.ToggleGlobalPanel(1);
            bootstrap.Hud.ToggleDebug();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Font font = UiFactory.DefaultFont;
            Assert.That(font, Is.Not.Null);
            foreach (Text label in Object.FindObjectsOfType<Text>())
            {
                string text = label.text;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] > 0x7f)
                    {
                        Assert.That(font.HasCharacter(text[i]), Is.True,
                            "Missing Chinese glyph '" + text[i] + "' in " + label.name + ".");
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator Bootstrap_DebugPanelSpawnsNpcAtTickBoundary()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            bootstrap.Runner.Pause();
            int before = bootstrap.Session.ReadModel.NpcCount;
            bootstrap.Hud.ToggleDebug();
            Button spawn = FindHudButton(bootstrap, "SpawnButton");
            Assert.That(spawn, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            spawn.onClick.Invoke();
            Assert.That(bootstrap.Session.QueuedCommandCount, Is.EqualTo(1));
            bootstrap.Runner.StepOnce();
            yield return null;
            Assert.That(bootstrap.Session.ReadModel.NpcCount, Is.EqualTo(before + 1));
            Assert.That(bootstrap.Presenter.NpcViewCount, Is.EqualTo(before + 1));
        }

        private static Button FindHudButton(GameBootstrap bootstrap, string name)
        {
            foreach (Button candidate in bootstrap.Hud.GetComponentsInChildren<Button>(true))
            {
                if (candidate.name == name) { return candidate; }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator Bootstrap_SaveButtons_RestoreViewsAndKeepDamagedLoadAtomic()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var bootstrap = Object.FindObjectOfType<GameBootstrap>(); bootstrap.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "MyAiWorld-PlayM4-" + Guid.NewGuid().ToString("N"));
            bootstrap.Session.SaveDirectory = folder;
            try
            {
                ClickHudButton(bootstrap, "SaveMenuButton");
                yield return new WaitForSecondsRealtime(0.2f);
                ClickHudButton(bootstrap, "SaveButton");
                ulong expected = bootstrap.Session.LastStateHash;
                bootstrap.Session.SpawnDebugNpc(); bootstrap.Runner.StepOnce(); yield return null;
                Assert.That(bootstrap.NpcViewCount, Is.EqualTo(21));
                ClickHudButton(bootstrap, "LoadButton"); yield return null; yield return null;
                Assert.That(bootstrap.Session.LastStateHash, Is.EqualTo(expected));
                Assert.That(bootstrap.NpcViewCount, Is.EqualTo(20)); Assert.That(bootstrap.Runner.IsPaused, Is.True);
                foreach (var view in bootstrap.Presenter.GetComponentsInChildren<NpcView>())
                {
                    Assert.That(view.GetComponent<SpriteRenderer>().color.a, Is.EqualTo(1f), "Restored NPCs must be immediately visible while paused.");
                }
                foreach (var view in bootstrap.Presenter.GetComponentsInChildren<AnimalView>())
                {
                    Assert.That(view.Opacity, Is.EqualTo(1f), "Restored animals must be immediately visible while paused.");
                }
                Assert.That(bootstrap.Hud.SaveStatusText, Does.Contain("读档成功"));
                ClickHudButton(bootstrap, "SaveButton");
                ClickHudButton(bootstrap, "LoadBackupButton");
                Assert.That(bootstrap.Session.LastStateHash, Is.EqualTo(expected));
                File.WriteAllText(bootstrap.Session.ManualSavePath, "broken");
                ClickHudButton(bootstrap, "LoadButton");
                Assert.That(bootstrap.Session.LastStateHash, Is.EqualTo(expected));
                Assert.That(bootstrap.Hud.SaveStatusText, Does.Contain("当前世界已保留"));
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator Bootstrap_SocialInspectorTimeline_ShowReciprocalCauseAndSurviveLoad()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var bootstrap = Object.FindObjectOfType<GameBootstrap>(); bootstrap.Runner.Pause();
            var session = bootstrap.Session;
            session.EnqueueCommand(new ArrangeSocialCommand(false));
            for (int i = 0; i < 5; i++) { bootstrap.Runner.StepOnce(); }
            Assert.That(session.World.Social.Gifts, Is.EqualTo(1));
            long cause = session.World.Social.Timeline.Last().Sequence;
            session.EnqueueCommand(new ArrangeSocialCommand(true));
            for (int i = 0; i < 5; i++) { bootstrap.Runner.StepOnce(); }
            var evt = session.World.Social.Timeline.Last();
            Assert.That(evt.CauseSequence, Is.EqualTo(cause));
            bootstrap.Selection.Select(evt.Actor);
            ClickHudButton(bootstrap, "RelationshipsTab");
            ClickHudButton(bootstrap, "EventsButton"); ClickHudButton(bootstrap, "TimelineSelected");
            yield return null;
            Assert.That(bootstrap.Hud.InspectorText, Does.Contain("主要关系"));
            Assert.That(bootstrap.Hud.InspectorText, Does.Contain("近期记忆"));
            Assert.That(bootstrap.Hud.EventText, Does.Contain("回报帮助 #" + cause));
            ulong hash = session.LastStateHash;
            ClickHudButton(bootstrap, "TimelineAll"); ClickHudButton(bootstrap, "TimelineSelected");
            Assert.That(FindHudButton(bootstrap, "TimelineOlder").interactable, Is.False);
            Assert.That(FindHudButton(bootstrap, "TimelineNewer").interactable, Is.False);
            Assert.That(session.LastStateHash, Is.EqualTo(hash));
            string folder = Path.Combine(Path.GetTempPath(), "MyAiWorld-SocialM4-" + Guid.NewGuid().ToString("N"));
            try
            {
                session.SaveDirectory = folder; Assert.That(session.Save(), Is.True);
                Assert.That(bootstrap.Runner.Load(), Is.True); yield return null; yield return null;
                bootstrap.Selection.Select(evt.Actor); bootstrap.Hud.SetTimelineFilter(true);
                Assert.That(bootstrap.Hud.EventText, Does.Contain("回报帮助 #" + cause));
                Assert.That(session.LastStateHash, Is.EqualTo(hash));
                Assert.That(session.Save(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "TestResults", "M4-ReciprocityReview.json"))), Is.True, session.SaveStatus);
                string logText = File.ReadAllText(session.LogPath);
                Assert.That(logText, Does.Contain("session_start"));
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        private sealed class ArrangeSocialCommand : IWorldCommand
        {
            private readonly bool _returnGift;
            public ArrangeSocialCommand(bool returnGift) { _returnGift = returnGift; }
            public string Describe() => "Arrange conserved social integration scenario";
            public bool Apply(WorldState world, CommandContext context)
            {
                var a = world.Entities.Npcs[0]; var b = world.Entities.Npcs[1]; var c = world.Entities.Npcs[2];
                foreach (var npc in world.Entities.Npcs)
                {
                    npc.Needs.Hunger = npc.Needs.Thirst = npc.Needs.Fatigue = 9000;
                    if (!_returnGift && npc.Inventory.Food > 0)
                    {
                        int food = npc.Inventory.Food;
                        npc.Inventory.TryRemove(ItemKind.Food, food); world.Resources.Stockpile.TryAdd(ItemKind.Food, food);
                    }
                    npc.Action.SetIdle("waiting", world.Tick + 100000);
                }
                var cells = world.Map.CollectReachable(world.Center);
                a.Position = world.Center; b.Position = cells.First(pos => pos.ManhattanDistance(a.Position) == 1); c.Position = b.Position;
                if (!_returnGift)
                {
                    Assert.That(world.Resources.Stockpile.TryRemove(ItemKind.Food, 3), Is.True);
                    a.Inventory.TryAdd(ItemKind.Food, 3); b.Needs.Hunger = 5500; a.Action.SetIdle("ready", world.Tick);
                }
                else
                {
                    b.Inventory.TryRemove(ItemKind.Food, 1); world.Resources.Stockpile.TryAdd(ItemKind.Food, 1);
                    a.Inventory.TryRemove(ItemKind.Food, 2); b.Inventory.TryAdd(ItemKind.Food, 2);
                    a.Needs.Hunger = c.Needs.Hunger = 5500; b.Action.SetIdle("ready", world.Tick);
                }
                return true;
            }
        }

        private static void ClickHudButton(GameBootstrap bootstrap, string name)
        {
            Button button = FindHudButton(bootstrap, name);
            Assert.That(button, Is.Not.Null, "Missing HUD button " + name);
            Assert.That(button.IsInteractable(), Is.True, name + " is not interactable.");
            Canvas.ForceUpdateCanvases();
            var raycaster = button.GetComponentInParent<GraphicRaycaster>();
            Assert.That(raycaster, Is.Not.Null);
            var rect = button.GetComponent<RectTransform>();
            Vector2 center = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera,
                rect.TransformPoint(rect.rect.center));
            Assert.That(center.x, Is.InRange(0f, (float)Screen.width), name + " is horizontally off screen.");
            Assert.That(center.y, Is.InRange(0f, (float)Screen.height), name + " is vertically off screen.");

            EventSystem eventSystem = EventSystem.current;
            Assert.That(eventSystem, Is.Not.Null);
            var pointer = new PointerEventData(eventSystem)
            {
                position = center,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            var graphics = new List<RaycastResult>();
            raycaster.Raycast(pointer, graphics);
            Assert.That(graphics.Count, Is.GreaterThan(0), name + " is missing from GraphicRaycaster results.");
            Assert.That(graphics[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button),
                name + " is covered by another graphic on its canvas.");

            var allHits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, allHits);
            Assert.That(allHits.Count, Is.GreaterThan(0));
            Assert.That(allHits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button),
                name + " is blocked by another UI raycast target.");
            pointer.pointerCurrentRaycast = allHits[0];
            GameObject receiver = ExecuteEvents.ExecuteHierarchy(allHits[0].gameObject, pointer,
                ExecuteEvents.pointerClickHandler);
            Assert.That(receiver, Is.SameAs(button.gameObject), name + " did not receive the pointer click.");
        }
    }
}
