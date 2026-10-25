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
        public IEnumerator CooperativeBoarHunt_CapturesTelegraphAttackAndRetreat()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>();
            game.Runner.Pause();
            var world = game.Session.World;
            GridPos origin = GridPos.Invalid;
            int bestDistance = int.MaxValue;
            GridPos preferred = new GridPos(world.Map.Width / 4, world.Map.Height / 4);
            for (int y = 3; y < world.Map.Height - 5; y++)
                for (int x = 3; x < world.Map.Width - 9; x++)
                {
                    bool clear = true;
                    for (int dy = 0; dy < 3; dy++)
                        for (int dx = 0; dx < 8; dx++)
                        {
                            var cell = new GridPos(x + dx, y + dy);
                            clear &= world.Map.IsWalkable(cell) && world.Map.GetTile(cell) != TileType.Forest;
                        }

                    var candidate = new GridPos(x, y);
                    if (clear && candidate.ManhattanDistance(preferred) < bestDistance)
                    {
                        origin = candidate;
                        bestDistance = candidate.ManhattanDistance(preferred);
                    }
                }

            Assert.That(origin.IsValid, Is.True, "The fixed seed needs an open review lane.");
            var boar = world.Ecology.Animals.First(a => a.Region == -1 && a.Species == AnimalSpecies.Boar);
            Set(boar, "Position", origin.Offset(5, 0));
            Set(boar, "HabitatId", world.Ecology.HabitatAt(boar.Position).Id);
            var ids = new List<EntityId>();
            world.Tick += 12 * 60 - world.Date.MinuteOfDay; // Controlled noon lighting, before any review commands.
            for (int i = 0; i < 3; i++)
            {
                var npc = world.Entities.Npcs[i];
                ids.Add(npc.Id);
                npc.Plan.GetType().GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(npc.Plan, new object[]{"Controlled review"});
                npc.Position = origin.Offset(i + 1, 1);
                npc.Action.SetIdle("Controlled review", world.Tick);
                foreach (string need in new[]{"Health", "Hunger", "Thirst", "Fatigue"})
                {
                    Set(npc.Needs, need, 10000);
                }

                Set(npc.Survival, "BodyTemperature", 3700);
                Set(npc.Survival, "Wetness", 0);
                npc.Survival.GetType().GetMethod("SetTool", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(npc.Survival, new object[]{i == 1 ? ItemKind.Spear : ItemKind.Bow, 120});
                npc.Inventory.TryAdd(ItemKind.Arrow, 4);
            }

            Set(world.Environment, "Temperature", 2500);
            Set(world.Environment, "Wind", 0);
            Set(world.Environment, "Weather", WeatherKind.Clear);
            Set(world.Environment, "NextWeatherTick", world.Tick + 1000);
            world.Materials.GetType().GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(world.Materials, new object[]{world});
            game.Session.EnqueueCommand(new RequestHuntCommand(boar.Id, ids));
            game.Runner.StepOnce();
            Assert.That(world.Hunting.ForTarget(boar.Id), Is.Not.Null);
            yield return null;
            foreach (EntityId id in ids)
            {
                game.Presenter.FindNpcView(id).SetCell(world.Entities.Npcs.First(n => n.Id == id).Position, true, world.Tick);
            }

            var animalView = game.Presenter.FindAnimalView(boar.Id);
            var motion = typeof(AnimalView).GetField("_motion", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animalView);
            motion.GetType().GetMethod("SetCell").Invoke(motion, new object[]{boar.Position, true, world.Tick});
            game.Selection.SelectAnimal(boar.Id);
            var camera = game.PlayerCamera.GetComponent<MyAiWorld.Presentation.CameraControl.GodCameraController>();
            camera.SetPixelScale(3);
            camera.Focus(game.Presenter.CellCenter(origin.Offset(4, 1)));
            game.Runner.SetSpeed(1);
            bool windup = false, attack = false;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Progress", "Hunting"));
            Directory.CreateDirectory(folder);
            for (int i = 0; i < 60 && (!windup || !attack); i++)
            {
                yield return new WaitForSecondsRealtime(0.1f);
                AttackSnapshot animalAttack = game.Session.ReadModel.Combat.GetAttack(boar.Id);
                if (!windup && animalAttack != null && animalAttack.Kind == CombatAttackKind.Charge && animalAttack.Phase == CombatAttackPhase.Preparing)
                {
                    SurvivalPresentationTests.Capture(game, Path.Combine(folder, "boar-windup.png"), 1920, 1080);
                    windup = true;
                }

                if (!attack && ids.Any(id => game.Session.ReadModel.Combat.GetAttack(id)?.Phase == CombatAttackPhase.Recovery))
                {
                    SurvivalPresentationTests.Capture(game, Path.Combine(folder, "team-attack.png"), 1920, 1080);
                    attack = true;
                }
            }

            Assert.That(windup, Is.True, "The boar must visibly telegraph its charge.");
            Assert.That(attack, Is.True, "A prepared team must actually attack.");
            Assert.That(world.Hunting.ForTarget(boar.Id)?.Stage, Is.Not.EqualTo(HuntStage.Retreating), "The team must still be hunting before the controlled injury.");
            Set(world.Entities.Npcs[0].Needs, "Health", 3900);
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSecondsRealtime(0.1f);
            }

            game.Runner.Pause();
            Assert.That(world.Hunting.ForTarget(boar.Id)?.Stage, Is.EqualTo(HuntStage.Retreating));
            Assert.That(world.Hunting.ForTarget(boar.Id)?.Reason, Does.Contain("受伤"));
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
