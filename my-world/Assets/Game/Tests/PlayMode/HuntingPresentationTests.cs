using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.Rendering;
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
    public sealed class HuntingPresentationTests
    {
        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        [Test]
        public void AttackFrames_FollowPhaseProgressAndDoNotLoopWithGlobalClock()
        {
            var attack = (CombatAttackState)Activator.CreateInstance(typeof(CombatAttackState), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[]{1L, new EntityId(1), new EntityId(2)}, null);
            Set(attack, "Kind", CombatAttackKind.Bow);
            Set(attack, "StartTick", 360L);
            Set(attack, "ReleaseTick", 362L);
            Set(attack, "RecoveryUntilTick", 363L);
            Set(attack, "FacingX", 1024);
            var snapshot = new NpcSnapshot(new EntityId(1), "Hunter", "Hunter", new GridPos(5, 5), "Hunt", MyAiWorld.Sim.Agents.NpcActionType.Work, 10000, LifeState.Active, 3700, 0, 0, 0, 0, 4, new AttackSnapshot(attack));
            var art = ScriptableObject.CreateInstance<PastoralArtConfig>();
            var root = new GameObject("HuntPhaseFrames");
            try
            {
                var view = root.AddComponent<NpcView>();
                view.Initialize(snapshot.Id, null, null);
                view.Configure(art, "Hunter", -1);
                view.SetCell(snapshot.Cell, true, 360);
                view.Present(snapshot, 361, 0f, 100f);
                Assert.That(view.FrameIndex, Is.Zero);
                view.Present(snapshot, 362, 0f, 100f);
                Assert.That(view.FrameIndex, Is.EqualTo(2));
                view.Present(snapshot, 363, 0f, 100f);
                Assert.That(view.FrameIndex, Is.EqualTo(3));
                view.Present(snapshot, 363, 0f, 101f);
                Assert.That(view.FrameIndex, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(art);
            }
        }

        [UnityTest]
        [Category("EngineSmoke")]
        public IEnumerator SelectedAnimal_FollowsEntityAndQueriesDoNotChangeHash()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AnimalSnapshot animal = game.Session.ReadModel.EcologyEconomy.Animals.First(a => a.Region == -1);
            game.Selection.SelectAnimal(animal.Id);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(game.Hud.OpenGlobalPanel, Is.EqualTo(1));
            Assert.That(game.Hud.HuntingText, Does.Contain("#" + animal.Id.Value));
            var state = game.Session.World.Ecology.Find(animal.Id.Value);
            GridPos destination = state.Position;
            foreach (GridPos cell in new[]{state.Position.Offset(1, 0), state.Position.Offset(0, 1), state.Position.Offset(-1, 0)})
            {
                if (game.Session.World.Map.IsWalkable(cell))
                {
                    destination = cell;
                    break;
                }
            }

            Set(state, "Position", destination);
            Set(state, "HabitatId", game.Session.World.Ecology.HabitatAt(destination).Id);
            game.Session.World.Tick++;
            game.Session.ReadModel.Refresh(game.Session.World, null, 0);
            yield return null;
            ulong hash = StateHasher.Compute(game.Session.World);
            Assert.That(game.Selection.SelectedAnimal, Is.EqualTo(animal.Id));
            Assert.That(game.Presenter.FindAnimalView(animal.Id).Cell, Is.EqualTo(destination));
            game.Session.ReadModel.Hunting.ForTarget(animal.Id);
            game.Hud.HuntingText.ToString();
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
        }

        [UnityTest]
        [Category("EngineSmoke")]
        public IEnumerator HuntButtons_QueueUntilTickAndCancelThroughPointerHandler()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            AnimalSnapshot rabbit = game.Session.ReadModel.EcologyEconomy.Animals.First(a => a.Region == -1 && a.Species == AnimalSpecies.Rabbit);
            var world = game.Session.World;
            var npc = world.Entities.Npcs.First(n => n.Role == "Hunter");
            Set(npc.Needs, "Health", 10000);
            Set(npc.Needs, "Hunger", 10000);
            Set(npc.Needs, "Thirst", 10000);
            Set(npc.Needs, "Fatigue", 10000);
            Set(npc.Survival, "BodyTemperature", 3700);
            Set(world.Environment, "Temperature", 2500);
            Set(world.Environment, "Wind", 0);
            npc.Position = rabbit.Cell;
            npc.Action.SetIdle("Controlled hunt", world.Tick);
            game.Session.ReadModel.Refresh(world, null, 0);
            game.Selection.SelectAnimal(rabbit.Id);
            yield return new WaitForSecondsRealtime(0.25f);
            ulong hash = StateHasher.Compute(world);
            Click(game.Hud.RequestHuntButton);
            Assert.That(game.Session.QueuedCommandCount, Is.EqualTo(1));
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
            game.Runner.StepOnce();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(world.Hunting.ForTarget(rabbit.Id), Is.Not.Null);
            Assert.That(world.Campaign.Assisted, Is.True);
            Assert.That(game.Runner.IsPaused, Is.True);
            Click(game.Hud.CancelHuntButton);
            Assert.That(world.Hunting.ForTarget(rabbit.Id).Stage, Is.Not.EqualTo(HuntStage.Retreating));
            game.Runner.StepOnce();
            yield return null;
            var task = world.Hunting.ForTarget(rabbit.Id);
            Assert.That(task == null || task.Stage == HuntStage.Retreating, Is.True);
        }

        [UnityTest]
        [Category("VisualReview")]
        public IEnumerator AutomaticRetreat_StopButtonStillAcceptsAPlayerOrder()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            var world = game.Session.World;
            var npc = world.Entities.Npcs.First(n => n.Role == "Hunter");
            var rabbit = world.Ecology.Animals.First(a => a.Region == -1 && a.Species == AnimalSpecies.Rabbit &&
                a.Position.ManhattanDistance(npc.Home) > 6);
            foreach (string need in new[] { "Health", "Hunger", "Thirst", "Fatigue" }) { Set(npc.Needs, need, 10000); }
            Set(npc.Survival, "BodyTemperature", 3700);
            Set(npc.Survival, "Wetness", 0);
            Set(world.Environment, "Temperature", 2500);
            Set(world.Environment, "Wind", 0);
            npc.Position = rabbit.Position;
            npc.Action.SetIdle("Controlled automatic hunt", world.Tick);
            game.Session.ReadModel.Refresh(world, null, 0);
            game.Selection.SelectAnimal(rabbit.Id);
            yield return new WaitForSecondsRealtime(0.25f);
            Click(game.Hud.RequestHuntButton);
            game.Runner.StepOnce();
            yield return new WaitForSecondsRealtime(0.25f);
            var task = world.Hunting.ForTarget(rabbit.Id);
            Assert.That(task, Is.Not.Null);
            Assert.That(task.Member(npc.Id), Is.Not.Null);
            Assert.That(task.Stage, Is.Not.EqualTo(HuntStage.Retreating));

            Set(npc.Needs, "Health", 3900);
            game.Runner.StepOnce();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(task.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(game.Hud.CancelHuntButton.IsInteractable(), Is.True,
                "A safety retreat must not prevent the player from stopping the remaining hunt/cover actions.");
            ulong before = StateHasher.Compute(world);
            Click(game.Hud.CancelHuntButton);
            Assert.That(game.Session.QueuedCommandCount, Is.EqualTo(1));
            Assert.That(StateHasher.Compute(world), Is.EqualTo(before));
            game.Runner.StepOnce();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(game.Session.QueuedCommandCount, Is.Zero);
            Assert.That(game.Hud.HuntingOrderFeedbackText, Does.Contain("已停止追猎"));
            Assert.That(game.Session.ReadModel.RecentEvents.Any(e => e.Kind == SimEventKind.CommandRejected &&
                e.Source == rabbit.Id && e.Tick >= world.Tick - 1), Is.False);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Progress", "Hunting"));
            Directory.CreateDirectory(folder);
            SurvivalPresentationTests.Capture(game, Path.Combine(folder, "stop-during-retreat.png"), 1920, 1080);
        }

        [UnityTest]
        public IEnumerator DefaultWorld_StopOrdersApplyAtOneFourAndEightSpeed()
        {
            foreach (int speed in new[] { 1, 4, 8 })
            {
                SceneManager.LoadScene("Bootstrap");
                yield return null;
                yield return null;
                var game = Object.FindObjectOfType<GameBootstrap>();
                game.Runner.Pause();
                var world = game.Session.World;
                // Advance the default winter morning until villagers have finished warming up.
                // No health, temperature, equipment, positions or world configuration are changed.
                for (int i = 0; i < 240 && game.Session.ReadModel.Hunting.Candidates.Count == 0; i++)
                {
                    game.Runner.StepOnce();
                    if (i % 16 == 0) { yield return null; }
                }
                var candidates = game.Session.ReadModel.Hunting.Candidates;
                Assert.That(candidates.Count, Is.GreaterThan(0));
                var rabbit = world.Ecology.Animals.Where(a => a.Species == AnimalSpecies.Rabbit &&
                    world.Ecology.CanHunt(a) && a.ReservedBy.IsNone).OrderBy(a =>
                    candidates.Min(c => world.Entities.Npcs.First(n => n.Id == c.Id).Position.ManhattanDistance(a.Position))).ThenBy(a => a.Id.Value).First();
                game.Selection.SelectAnimal(rabbit.Id);
                yield return new WaitForSecondsRealtime(0.25f);
                Click(game.Hud.RequestHuntButton);
                game.Runner.StepOnce();
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(world.Hunting.ForTarget(rabbit.Id), Is.Not.Null, game.Hud.HuntingOrderFeedbackText);
                ulong hash = StateHasher.Compute(world);
                long issuedAt = game.Session.LastTick;
                Click(game.Hud.CancelHuntButton);
                Assert.That(game.Session.QueuedCommandCount, Is.EqualTo(1));
                Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
                game.Runner.SetSpeed(speed);
                float until = Time.realtimeSinceStartup + 3f;
                while (game.Session.LastTick == issuedAt && Time.realtimeSinceStartup < until) { yield return null; }
                game.Runner.Pause();
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(game.Session.LastTick, Is.GreaterThan(issuedAt), "The live editor must actually advance at " + speed + "x.");
                Assert.That(game.Session.QueuedCommandCount, Is.Zero);
                Assert.That(game.Hud.HuntingOrderFeedbackText, Does.Contain("已停止追猎"));
                var task = world.Hunting.ForTarget(rabbit.Id);
                Assert.That(task == null || task.Stage == HuntStage.Retreating, Is.True);
            }
        }

        [UnityTest]
        [Category("VisualReview")]
        public IEnumerator CooperativeBoarHunt_CapturesTelegraphAttackAndRetreat()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            HuntingFieldE2ETests.LoadScenario(game, HuntingFieldE2ETests.Scenario(false, out EntityId target));
            yield return null; yield return null;
            var world = game.Session.World;
            var ids = world.Entities.Npcs.Select(n => n.Id).ToArray();
            game.Selection.SelectAnimal(target);
            var camera = game.PlayerCamera.GetComponent<MyAiWorld.Presentation.CameraControl.GodCameraController>();
            camera.SetPixelScale(3);
            camera.Focus(game.Presenter.CellCenter(world.Resources.StockpilePosition.Offset(4, 0)));
            // Initial encounter setup: an already defensive boar tests the charge telegraph.
            // The complete natural hunt cases are covered separately by HuntingFieldE2ETests.
            var initialBoar = world.Ecology.Find(target.Value);
            Set(initialBoar, "Position", world.Resources.StockpilePosition.Offset(4, 0));
            Set(initialBoar, "HabitatId", world.Ecology.HabitatAt(initialBoar.Position).Id);
            Assert.That(CombatSystem.RequestCharge(world, initialBoar, world.Entities.Npcs[1], new List<SimEvent>()), Is.True);
            game.Session.EnqueueCommand(new RequestHuntCommand(target, ids));
            game.Runner.StepOnce();
            yield return null;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Progress", "Hunting"));
            Directory.CreateDirectory(folder);
            bool windup = game.Session.ReadModel.Combat.GetAttack(target)?.Phase == CombatAttackPhase.Preparing;
            Assert.That(windup, Is.True);
            SurvivalPresentationTests.Capture(game, Path.Combine(folder, "boar-windup.png"), 1920, 1080);
            game.Runner.SetSpeed(8);
            float readyBy = Time.realtimeSinceStartup + 12;
            while (world.Hunting.ForTarget(target)?.Stage == HuntStage.Gathering && Time.realtimeSinceStartup < readyBy) { yield return null; }
            Assert.That(world.Hunting.ForTarget(target)?.Stage, Is.EqualTo(HuntStage.Approaching));
            game.Runner.SetSpeed(1);
            bool attack = false;
            for (int i = 0; i < 160 && (!windup || !attack); i++)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                AttackSnapshot animalAttack = game.Session.ReadModel.Combat.GetAttack(target);
                if (!windup && animalAttack != null && animalAttack.Kind == CombatAttackKind.Charge && animalAttack.Phase == CombatAttackPhase.Preparing)
                { SurvivalPresentationTests.Capture(game, Path.Combine(folder, "boar-windup.png"), 1920, 1080); windup = true; }
                if (!attack && ids.Any(id => game.Session.ReadModel.Combat.GetAttack(id)?.Phase == CombatAttackPhase.Recovery))
                { SurvivalPresentationTests.Capture(game, Path.Combine(folder, "team-attack.png"), 1920, 1080); attack = true; }
            }
            Assert.That(windup, Is.True, "The boar must visibly telegraph its charge.");
            Assert.That(attack, Is.True, "A prepared team must actually attack.");
            Assert.That(world.Hunting.ForTarget(target), Is.Not.Null);
            Assert.That(world.Hunting.ForTarget(target).Stage, Is.Not.EqualTo(HuntStage.Retreating));
            // This is an injury visualization fixture, separate from the no-override E2E cases.
            Set(world.Entities.Npcs[0].Needs, "Health", 3900);
            yield return new WaitForSecondsRealtime(0.6f);
            game.Runner.Pause();
            Assert.That(world.Hunting.ForTarget(target)?.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(world.Hunting.ForTarget(target)?.Reason, Does.Contain("受伤"));
            SurvivalPresentationTests.Capture(game, Path.Combine(folder, "team-retreat.png"), 1920, 1080);
        }

        private static void Click(Button button)
        {
            Assert.That(button.IsInteractable() && button.gameObject.activeInHierarchy, Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            var raycaster = button.GetComponentInParent<GraphicRaycaster>();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current)
            {position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left, clickCount = 1};
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button));
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        [Category("VisualReview")]
        public IEnumerator HuntingAtlasAndThreeResolutionReview_UseActualCameraAndCanvas()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            foreach (int direction in new[]{0, 1, 2, 3})
            {
                for (int stage = 0; stage < 3; stage++)
                    for (int frame = 0; frame < 4; frame++)
                    {
                        Assert.That(game.Presenter.Art.Get("animal_charge_" + direction + "_" + stage + "_" + frame), Is.Not.Null);
                    }

                for (int species = 0; species < 7; species++)
                {
                    Assert.That(game.Presenter.Art.Get("animal_hurt_" + species + "_" + direction + "_0"), Is.Not.Null);
                }
            }

            var animal = game.Session.ReadModel.EcologyEconomy.Animals.First(a => a.Region == -1 && a.Species == AnimalSpecies.Boar);
            game.Selection.SelectAnimal(animal.Id);
            game.PlayerCamera.GetComponent<MyAiWorld.Presentation.CameraControl.GodCameraController>().Focus(game.Presenter.CellCenter(animal.Cell));
            yield return new WaitForSecondsRealtime(0.25f);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Progress", "Hunting"));
            Directory.CreateDirectory(folder);
            ulong hash = StateHasher.Compute(game.Session.World);
            foreach (Vector2Int size in new[]{new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440)})
            {
                SurvivalPresentationTests.Capture(game, Path.Combine(folder, "hunting-" + size.x + ".png"), size.x, size.y);
                Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            }
        }
    }
}
