using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App.Observability;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.CameraControl;
using MyAiWorld.Sim.Agents;
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
    [Category("HuntingFieldE2E")]
    public sealed class HuntingFieldE2ETests
    {
        [Serializable]
        private sealed class Trace
        {
            public int taskId, target;
            public int[] participants, items;
            public long[] returnTicks;
            public string[] returnCells;
            public string[] stages;
            public long ticks;
            public bool success, prepared, attack, corpse;
            public string reason;
        }
        private static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Progress", "HuntingField"));
        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);

        internal static WorldState Scenario(bool maze, out EntityId target)
        {
            var config = new WorldConfigData(42, 64, 64, 2, (28 + 14) * 1440L + 720, 2, new[] { 1, 4, 8 });
            var map = new WorldMap(64, 64);
            GridPos stock = new GridPos(32, 32), goal = new GridPos(39, 32);
            if (maze)
            {
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) { map.SetTile(new GridPos(x, y), TileType.Rock); }
                for (int x = 2; x <= 32; x++) { map.SetTile(new GridPos(x, 32), TileType.Grass); }
                for (int x = 2; x <= 61; x++)
                { map.SetTile(new GridPos(x, 30), TileType.Grass); map.SetTile(new GridPos(x, 28), TileType.Grass); }
                map.SetTile(new GridPos(33, 32), TileType.Grass);
                map.SetTile(new GridPos(2, 31), TileType.Grass); map.SetTile(new GridPos(61, 29), TileType.Grass);
                goal = new GridPos(2, 28);
            }
            map.SetTile(new GridPos(0, 0), TileType.Grass);
            var world = new WorldState(config, map, new DeterministicRng(42));
            for (int i = 0; i < 2; i++)
            {
                GridPos home = stock.Offset(i, 1);
                map.SetTile(home, TileType.Grass);
                var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity(i == 0 ? "阿林" : "小岚", "Hunter", 25), new Personality(), home, (ulong)(42 + i));
                foreach (string need in new[] { "Health", "Hunger", "Thirst", "Fatigue" }) { Set(npc.Needs, need, 10000); }
                Set(npc, "HuntingSkill", 100);
                Call(npc.Survival, "SetTool", i == 0 ? ItemKind.Spear : ItemKind.Bow, 120);
                npc.Action.SetIdle("Scenario start", long.MaxValue);
                Call(world.Entities, "AddNpc", npc);
            }
            Set(world.Market, "IssuedCurrency", world.Market.IssuedCurrency + world.Entities.Npcs.Sum(n => n.Coins));
            var reachable = map.CollectReachable(stock);
            Call(world.Ecology, "Initialize", world, reachable);
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Region != -1) { continue; }
                Set(animal, "Position", new GridPos(0, 0));
                Set(animal, "HabitatId", world.Ecology.HabitatAt(new GridPos(0, 0)).Id);
            }
            var species = maze ? AnimalSpecies.Deer : AnimalSpecies.Boar;
            var prey = (AnimalState)Call(world.Ecology, "Spawn", world, species, goal, AnimalDefinition.For(species).MaturityTicks + 1);
            target = prey.Id;
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 40);
            world.Resources.Stockpile.TryAdd(ItemKind.Water, 40);
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 16);
            world.Resources.Stockpile.TryAdd(ItemKind.WinterCoat, 2);
            Set(world.Environment, "Temperature", 2500); Set(world.Environment, "Wind", 0);
            Set(world.Environment, "Weather", WeatherKind.Clear); Set(world.Environment, "NextWeatherTick", world.Tick + 10000);
            Set(world.Environment, "Daylight", 10000);
            Call(world.Campaign, "Initialize", world);
            Call(world.Materials, "Initialize", world);
            return world;
        }

        internal static void LoadScenario(GameBootstrap game, WorldState world)
        {
            string directory = Path.Combine(Path.GetTempPath(), "MyAiWorld-HuntingE2E-" + Guid.NewGuid().ToString("N"));
            game.Session.SaveDirectory = directory;
            string file = Path.Combine(directory, "scenario.json");
            SaveStore.Write(file, new SessionSaveData { World = WorldSnapshot.Capture(world), HashVersion = StateHasher.Version, StateHash = StateHasher.ToHex(StateHasher.Compute(world)) });
            Assert.That(game.Runner.Load(file), Is.True, game.Session.SaveStatus);
        }

        private static void Click(Button button)
        {
            Assert.That(button.IsInteractable() && button.gameObject.activeInHierarchy, Is.True, button.name);
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var raycaster = button.GetComponentInParent<GraphicRaycaster>();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button));
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static IEnumerator SelectAndRequest(GameBootstrap game, EntityId prey)
        {
            var animal = game.Session.ReadModel.EcologyEconomy.Animals.Single(a => a.Id == prey);
            game.PlayerCamera.GetComponent<GodCameraController>().Focus(game.Presenter.CellCenter(animal.Cell));
            yield return null; yield return null;
            Vector3 screen = game.PlayerCamera.WorldToScreenPoint(game.Presenter.CellCenter(animal.Cell) + Vector3.up * 0.25f);
            game.Selection.SelectAtScreenPosition(screen);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(game.Selection.SelectedAnimal, Is.EqualTo(prey), "Selection must pick through the rendered world coordinates.");
            ulong hash = StateHasher.Compute(game.Session.World);
            Click(game.Hud.RequestHuntButton);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
            Assert.That(game.Session.QueuedCommandCount, Is.EqualTo(1));
            game.Runner.StepOnce();
            yield return new WaitForSecondsRealtime(0.25f);
        }

        private static void Capture(GameBootstrap game, string name, int width = 1920, int height = 1080)
        {
            Directory.CreateDirectory(Folder);
            SurvivalPresentationTests.Capture(game, Path.Combine(Folder, name + ".png"), width, height);
        }

        private static IEnumerator Follow(GameBootstrap game, EntityId target, string name, bool? expectedSuccess)
        {
            WorldState world = game.Session.World;
            HuntTaskState task = world.Hunting.ForTarget(target);
            Assert.That(task, Is.Not.Null, game.Hud.HuntingOrderFeedbackText);
            int taskId = task.Id;
            Assert.That(task.Members.Count, Is.GreaterThanOrEqualTo(2));
            var stages = new List<string>();
            var participants = task.Members.Select(m => m.NpcId).ToArray();
            bool prepared = false, attack = false, corpse = false;
            long began = world.Tick;
            float deadline = Time.realtimeSinceStartup + 90;
            game.Runner.SetSpeed(8);
            while (!world.Hunting.RecentResults.Any(r => r.Id == taskId) && world.Tick - began < 900 && Time.realtimeSinceStartup < deadline)
            {
                var current = world.Hunting.ForTarget(target);
                if (current != null && !stages.Contains(current.Stage.ToString()))
                {
                    stages.Add(current.Stage.ToString());
                    Capture(game, name + "-" + current.Stage);
                }
                if (current != null && current.Stage == HuntStage.Approaching && !prepared)
                {
                    Assert.That(participants.All(id => world.Entities.Npcs.Single(n => n.Id == id).Inventory.Food > 0 && world.Entities.Npcs.Single(n => n.Id == id).Inventory.Water > 0), Is.True);
                    prepared = true;
                }
                attack |= game.Session.ReadModel.Combat.Attacks.Any(a => participants.Contains(a.Attacker));
                corpse |= game.Session.ReadModel.EcologyEconomy.Carcasses.Any(c => c.Id == target);
                yield return null;
            }
            game.Runner.Pause();
            var result = world.Hunting.RecentResults.SingleOrDefault(r => r.Id == taskId);
            Assert.That(result, Is.Not.Null, "Hunt never finished: " + (world.Hunting.ForTarget(target)?.Reason ?? "no task") + "; tick " + world.Tick);
            Assert.That(prepared, Is.True, "The whole team must pass through actual supply preparation.");
            for (int i = 0; i < result.Participants.Count; i++)
            {
                var npc = world.Entities.Npcs.Single(n => n.Id == result.Participants[i]);
                Assert.That(result.ReturnTicks[i], Is.GreaterThanOrEqualTo(began));
                Assert.That(result.ReturnCells[i] == npc.Home || result.ReturnCells[i].ManhattanDistance(world.Resources.StockpilePosition) <= 2, Is.True, "Task release alone is not a return to the village.");
            }
            Assert.That(world.Resources.Reservations.Any(r => participants.Contains(r.Owner) && r.Kind == ReservationKind.HuntSupply), Is.False);
            if (expectedSuccess.HasValue) { Assert.That(result.Success, Is.EqualTo(expectedSuccess.Value), result.Reason); }
            if (result.Success)
            {
                Assert.That(attack && corpse, Is.True);
                Assert.That(result.Items[(int)ItemKind.RawMeat], Is.GreaterThan(0));
                Assert.That(world.Ecology.HuntKills, Is.GreaterThan(0));
                Assert.That(world.Hunting.ForTarget(target), Is.Null);
            }
            else if (expectedSuccess == false) { Assert.That(result.Reason, Does.Contain("追猎超时").Or.Contain("失去目击")); }
            for (int item = 0; item < (int)ItemKind.Count; item++) { Assert.That(world.Materials.Error(world, (ItemKind)item), Is.Zero, ((ItemKind)item).ToString()); }
            Assert.That(world.Ecology.Balance.BiomassError(world.Ecology), Is.Zero);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(game.Hud.HuntingText, Does.Contain(result.Success ? "狩猎成功" : "狩猎结束"));
            Capture(game, name + "-result");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, name + "-trace.json"), JsonUtility.ToJson(new Trace { taskId = taskId, target = target.Value, participants = participants.Select(id => id.Value).ToArray(), stages = stages.ToArray(), ticks = world.Tick - began, success = result.Success, reason = result.Reason, items = result.Items.ToArray(), returnTicks = result.ReturnTicks.ToArray(), returnCells = result.ReturnCells.Select(c => c.ToString()).ToArray(), prepared = prepared, attack = attack, corpse = corpse }, true));
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator PreparedMixedTeam_KillsRetrievesAndDepositsWithoutRuntimeOverrides()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            LoadScenario(game, Scenario(false, out EntityId prey)); yield return null; yield return null;
            yield return SelectAndRequest(game, prey);
            yield return Follow(game, prey, "success", true);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) }) { Capture(game, "review-" + size.x, size.x, size.y); }
            game.Selection.Select(game.Session.World.Entities.Npcs[0].Id);
            game.Hud.ShowSelectedNpcDetails();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(game.Hud.InspectorText, Does.Contain("短期耐力"));
            Canvas.ForceUpdateCanvases();
            var scroll = game.Hud.GetComponentsInChildren<ScrollRect>(true).First(s => s.gameObject.activeInHierarchy && s.content != null && s.content.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "Facts_condition"));
            var fact = scroll.content.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "Facts_condition");
            float fromTop = scroll.content.rect.yMax - scroll.content.InverseTransformPoint(fact.TransformPoint(fact.rect.center)).y;
            float range = scroll.content.rect.height - scroll.viewport.rect.height;
            scroll.verticalNormalizedPosition = range <= 0 ? 1 : 1 - Mathf.Clamp01((fromTop - scroll.viewport.rect.height / 2) / range);
            yield return null;
            Capture(game, "hunter-details-1280", 1280, 720);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator LongReachableRoute_TimesOutAndActuallyReturnsWithoutRuntimeOverrides()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            LoadScenario(game, Scenario(true, out EntityId prey)); yield return null; yield return null;
            yield return SelectAndRequest(game, prey);
            yield return Follow(game, prey, "failure", false);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator DefaultUnmodifiedWorld_CompletesOneAutomaticTeamExpedition()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            game.Session.SaveDirectory = Path.Combine(Path.GetTempPath(), "MyAiWorld-DefaultHunt-" + Guid.NewGuid().ToString("N"));
            WorldState world = game.Session.World;
            EntityId target = EntityId.None;
            for (int tick = 0; tick < 1440 && target.IsNone; tick++)
            {
                if (tick % 30 == 0 && game.Session.ReadModel.Hunting.Candidates.Count >= 2)
                {
                    var candidates = game.Session.ReadModel.Hunting.Candidates;
                    var prey = world.Ecology.Animals.Where(a => a.Region == -1 && a.Health > 0 && a.ReservedBy.IsNone && (a.Species == AnimalSpecies.Boar || a.Species == AnimalSpecies.Deer))
                        .OrderBy(a => candidates.Min(c => world.Entities.Npcs.Single(n => n.Id == c.Id).Position.ManhattanDistance(a.Position))).ThenBy(a => a.Id.Value).FirstOrDefault();
                    if (prey != null)
                    {
                        yield return SelectAndRequest(game, prey.Id);
                        if (world.Hunting.ForTarget(prey.Id) != null) { target = prey.Id; break; }
                    }
                }
                game.Runner.StepOnce();
                if (tick % 16 == 0) { yield return null; }
            }
            Assert.That(target.IsNone, Is.False, "No default-world two-person expedition could be prepared in one game day. " + game.Hud.HuntingOrderFeedbackText);
            yield return Follow(game, target, "default", null);
        }
    }
}
