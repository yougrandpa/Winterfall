using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                Assert.That(game.Hud.CampaignSummaryText, Does.Contain("1/14"));
                AssertImmutableDtos();
                Assert.Throws<NotSupportedException>(() => ((IList<EntityId>)initial.InitialNpcs)[0] = EntityId.None);
                AssertReserves(game.Session);

                game.Session.EnqueueCommand(new DownAllCommand());
                game.Runner.StepOnce();
                Assert.That(game.Session.ReadModel.Campaign.Downed, Is.EqualTo(5));
                Assert.That(game.Session.ReadModel.Campaign.Result, Is.Null);
                for (int i = 0; i <= SurvivalRules.RescueWindow; i++) { game.Runner.StepOnce(); }
                yield return new WaitForSecondsRealtime(0.25f);

                CampaignSnapshot locked = game.Session.ReadModel.Campaign;
                CampaignResultSnapshot score = locked.Result;
                Assert.That(locked.IsLocked, Is.True);
                Assert.That(score.Grade, Is.EqualTo("F"));
                Assert.That(score.Reason, Is.EqualTo("all_initial_npcs_dead"));
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Assert.That(game.Hud.CampaignSummaryText, Does.Contain("1/14"));
                Assert.That(game.Hud.CampaignText, Does.Contain("原始名单").And.Contain("初始 5").And.Contain("实际食物"));
                Assert.That(game.Hud.CampaignText, Does.Contain("倒地 0").And.Contain("恢复 0").And.Contain("无干预"));
                Assert.That(game.Hud.EventText, Does.Contain("寒冬挑战结束"));
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
                Click(game, FindButton(game, "CancelContinueObservationButton"));
                Assert.That(game.Hud.ContinueObservationConfirmationOpen, Is.False);
                Assert.That(game.Hud.CampaignModalOpen, Is.True);
                Click(game, game.Hud.ContinueObservationButton);
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
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), "The button must not be covered.");
            pointer.pointerCurrentRaycast = hits[0];
            Assert.That(ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler), Is.SameAs(button.gameObject));
        }
    }
}
