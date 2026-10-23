using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MyAiWorld.Tests.PlayMode
{
    public sealed class CombatPresentationTests
    {
        // Reflection is confined to test setup; no friend-assembly change or production mutation API.
        private static void Set(object instance, string property, object value)
        {
            instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);
        }

        private static T Construct<T>(params object[] arguments)
        {
            return (T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null);
        }

        private static WorldState Arena(out NpcState npc, out AnimalState prey)
        {
            var world = new WorldState(new WorldConfigData(42, 32, 32, 1, 360, 2, new[] { 1, 2, 4, 8 }),
                new WorldMap(32, 32), new DeterministicRng(42));
            npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("Hunter", "Hunter", 25), new Personality(), new GridPos(5, 5), 42);
            world.Entities.AddNpc(npc);
            Set(npc.Survival, "BodyTemperature", 3420); Set(npc.Survival, "Wetness", 1234);
            Set(npc.Survival, "ColdTicks", 12); Set(npc.Survival, "CoatDurability", 80);
            npc.Survival.GetType().GetMethod("SetTool", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(npc.Survival, new object[] { ItemKind.Bow, 120 });
            Assert.That(npc.Inventory.TryAdd(ItemKind.Arrow, 6), Is.True);
            Set(world.Environment, "Daylight", 10000); Set(world.Environment, "Temperature", 1000);
            prey = world.Ecology.Spawn(world, AnimalSpecies.Rabbit, new GridPos(9, 5), 1500);
            Set(prey, "ReservedBy", npc.Id);
            world.Materials.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world.Materials, new object[] { world });
            world.Campaign.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world.Campaign, new object[] { world });
            return world;
        }

        private static void Minute(WorldState world)
        {
            CombatSystem.Update(world, new List<SimEvent>()); Set(world, "Tick", world.Tick + 1);
        }

        [Test]
        public void RealCombat_ReadModelsAreImmutableAndQueriesDoNotChangeHash()
        {
            WorldState world = Arena(out NpcState npc, out AnimalState prey);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.True);
            var model = new WorldReadModel(world.Config); model.Initialize(world, new List<SimEvent>());
            CombatReadModel before = model.Combat;
            NpcSnapshot villager = model.Npcs.Single(); NpcDetails details = model.GetDetails(npc.Id);
            Assert.That(villager.Attack, Is.SameAs(before.GetAttack(npc.Id)));
            Assert.That(details.Attack, Is.SameAs(villager.Attack));
            Assert.That(details.BodyTemperature, Is.EqualTo(3420)); Assert.That(villager.Wetness, Is.EqualTo(1234));
            Assert.That(details.HasWinterCoat, Is.True); Assert.That(villager.Arrows, Is.EqualTo(6));
            Assert.That(villager.Home, Is.EqualTo(npc.Home)); Assert.That(details.Home, Is.EqualTo(npc.Home));
            Assert.Throws<NotSupportedException>(() => ((IList<AttackSnapshot>)before.Attacks).Clear());
            foreach (Type type in new[] { typeof(AttackSnapshot), typeof(ProjectileSnapshot), typeof(NpcSnapshot), typeof(NpcDetails) })
            {
                Assert.That(type.GetProperties().All(p => !p.CanWrite), Is.True, type.Name);
            }
            Minute(world); Minute(world); Minute(world);
            model.Refresh(world, null, 0);
            Assert.That(model.Combat, Is.Not.SameAs(before));
            Assert.That(before.GetAttack(npc.Id).Phase, Is.EqualTo(CombatAttackPhase.Preparing));
            Assert.That(model.Combat.GetAttack(npc.Id).Phase, Is.EqualTo(CombatAttackPhase.Recovery));
            Assert.That(model.Combat.Projectiles.Count, Is.EqualTo(1));
            ProjectileSnapshot arrow = model.Combat.Projectiles[0]; int x = arrow.X;
            ulong hash = StateHasher.Compute(world);
            model.GetDetails(npc.Id); model.Combat.GetAttack(npc.Id); model.GetTimeline(npc.Id);
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
            Minute(world); model.Refresh(world, null, 0);
            Assert.That(arrow.X, Is.EqualTo(x), "Retained DTO must not follow authoritative arrow movement.");
            Assert.That(before.Projectiles.Count, Is.Zero);
        }

        [Test]
        public void LegacyNpcConstructorAndRegionalAnimalQueries_PreserveDefaultsAndDoNotMutateAuthority()
        {
            WorldState world = Arena(out NpcState npc, out AnimalState prey);
            var legacy = new NpcSnapshot(npc.Id, npc.Name, npc.Role, npc.Position, "Idle");
            Assert.That(legacy.Health, Is.EqualTo(10000)); Assert.That(legacy.Life, Is.EqualTo(LifeState.Active));
            Assert.That(legacy.BodyTemperature, Is.EqualTo(3700)); Assert.That(legacy.Attack, Is.Null);
            Assert.That(legacy.Home, Is.EqualTo(npc.Position));
            var model = new WorldReadModel(world.Config); model.Initialize(world, new List<SimEvent>());
            Assert.That(model.IsAnimalNonLocal(prey.Id), Is.False);
            Set(prey, "Region", 1); // A stale but valid cell must not imply local presence.
            model.Refresh(world, null, 0);
            Assert.That(model.IsAnimalNonLocal(prey.Id), Is.True);
            Set(prey, "Health", 0); model.Refresh(world, null, 0);
            ulong hash = StateHasher.Compute(world);
            Assert.That(model.IsAnimalNonLocal(prey.Id), Is.True, "External death must not play at a stale local cell.");
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }

        [Test]
        public void NativeNpcAndAnimalAttackPoses_UseFacingAndDedicatedKeys()
        {
            WorldState world = Arena(out NpcState npc, out AnimalState prey);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.True);
            var art = ScriptableObject.CreateInstance<PastoralArtConfig>();
            var root = new GameObject("CombatPoseTest");
            try
            {
                var view = root.AddComponent<NpcView>(); view.Initialize(npc.Id, null, root.transform.parent);
                view.Configure(art, npc.Role, -1f); view.SetCell(npc.Position, true, world.Tick);
                int[] xs = { 0, 1024, 0, -1024 }, ys = { -1024, 0, 1024, 0 };
                for (int direction = 0; direction < 4; direction++)
                {
                    Set(world.Combat.GetAttack(npc.Id), "FacingX", xs[direction]); Set(world.Combat.GetAttack(npc.Id), "FacingY", ys[direction]);
                    var attack = new AttackSnapshot(world.Combat.GetAttack(npc.Id));
                    var snapshot = new NpcSnapshot(npc.Id, npc.Name, npc.Role, npc.Position, "Hunt", NpcActionType.Work,
                        10000, LifeState.Active, 3700, 0, 0, 0, 0, 6, attack);
                    view.Present(snapshot, world.Tick, 0.3f, 1f);
                    Assert.That(view.CombatStage, Is.Zero); Assert.That(view.FacingDirection, Is.EqualTo(direction));
                    Assert.That(view.SpriteKey, Does.StartWith("npc_attack_2_0_" + direction + "_0_"));
                    Assert.That(view.CurrentSprite, Is.Null, "Missing generated art must not silently become an unrelated old frame.");
                }
                Minute(world); Minute(world); Minute(world);
                var model = new WorldReadModel(world.Config); model.Initialize(world, new List<SimEvent>());
                view.Present(model.Npcs[0], world.Tick, 0f, 2f); Assert.That(view.CombatStage, Is.EqualTo(1));
                Set(world.Combat.GetAttack(npc.Id), "Kind", CombatAttackKind.Spear);
                model.Refresh(world, null, 0); view.Present(model.Npcs[0], world.Tick, 0f, 2.1f);
                Assert.That(view.CombatStage, Is.EqualTo(3));
                var hit = new CombatVisualEvent(world.Tick, 1, 0, prey.Id, npc.Id, CombatEventKind.Contact, 5632, 5632, 0, 7);
                view.ReceiveCombatEvent(hit, CombatAttackKind.Bite, 0, 0, 2.2f); view.Present(model.Npcs[0], world.Tick, 0f, 2.2f);
                Assert.That(view.CombatStage, Is.EqualTo(4));
                var animalObject = new GameObject("AnimalPose"); animalObject.transform.SetParent(root.transform, false);
                var animalView = animalObject.AddComponent<AnimalView>();
                animalView.Initialize(model.EcologyEconomy.Animals.Single(), art, world.Tick, 0f, true);
                var animalAttack = Construct<CombatAttackState>(2L, prey.Id, npc.Id);
                Set(animalAttack, "Kind", CombatAttackKind.Bite); Set(animalAttack, "FacingX", -1024);
                animalView.Present(0.2f, 2.2f, new AttackSnapshot(animalAttack));
                Assert.That(animalView.IsAttacking, Is.True); Assert.That(animalView.SpriteKey, Does.StartWith("animal_attack_0_3_"));
                animalView.BeginDespawn(2.2f);
                Assert.That(animalView.IsDying, Is.False); Assert.That(animalView.Present(1f, 2.2f), Is.True);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(art); }
        }

        [Test]
        public void ProjectilePlayback_SameTickMissAndMultipleSweepsFollowRecordedEndpoints()
        {
            var art = ScriptableObject.CreateInstance<PastoralArtConfig>(); var go = new GameObject("TraceTest");
            try
            {
                var view = go.AddComponent<ProjectileView>(); view.Initialize(7, art);
                var shooter = new EntityId(1);
                view.Enqueue(new CombatTraceSegment(360, 7, shooter, 5632, 5632, 6656, 5632, EntityId.None,
                    CombatEventKind.Flying, 0, 65536), 0f, 0.5f);
                view.Enqueue(new CombatTraceSegment(361, 7, shooter, 6656, 5632, 6912, 5632, EntityId.None,
                    CombatEventKind.Broken, 0, 16384), 0.5f, 0.625f);
                Assert.That(view.Present(0.25f), Is.False);
                Assert.That(view.transform.position.x, Is.EqualTo(6f).Within(0.001f));
                Vector3 paused = view.transform.position; view.Present(0.25f);
                Assert.That(view.transform.position, Is.EqualTo(paused));
                view.Present(0.5625f); Assert.That(view.transform.position.x, Is.EqualTo(6.625f).Within(0.001f));
                view.Present(0.625f); Assert.That(view.PlayedTraceCount, Is.EqualTo(2));
                Assert.That(view.transform.position.x, Is.EqualTo(6.75f).Within(0.001f));
                Assert.That(view.ImpactKey, Is.EqualTo("projectile_miss"));
                Assert.That(view.Present(0.8f), Is.False); Assert.That(view.Present(0.9f), Is.True);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(art); }
        }

        [Test]
        public void ProjectilePlayback_ZeroTimeHitShowsShaftBeforeImpactAndFreezesAtSameClock()
        {
            var art = ScriptableObject.CreateInstance<PastoralArtConfig>(); var go = new GameObject("ImmediateHitTest");
            try
            {
                var view = go.AddComponent<ProjectileView>(); view.Initialize(8, art);
                view.Enqueue(new CombatTraceSegment(360, 8, new EntityId(1), 5632, 5632, 5632, 5632,
                    new EntityId(2), CombatEventKind.Embedded, 0, 0), 0f, 0f);
                Assert.That(view.Present(1f), Is.False); Assert.That(view.ArrowVisible, Is.True);
                Assert.That(view.PlayedTraceCount, Is.Zero); Assert.That(view.ImpactVisible, Is.False);
                Vector3 position = view.transform.position; view.Present(1f);
                Assert.That(view.transform.position, Is.EqualTo(position)); Assert.That(view.PlayedTraceCount, Is.Zero);
                view.Present(1.01f);
                Assert.That(view.PlayedTraceCount, Is.EqualTo(1)); Assert.That(view.ImpactKey, Is.EqualTo("projectile_hit"));
                Assert.That(view.ImpactVisible, Is.True); Assert.That(view.ArrowVisible, Is.False);
                Assert.That(view.Present(1.3f), Is.True);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(art); }
        }

        [UnityTest]
        public IEnumerator Presenter_DrainsEightFramesOnce_PausesAndDoesNotLoseCompletedArrows()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            var queue = (List<TickResult>)typeof(GameSession).GetField("_combatFrames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game.Session);
            int baseline = game.Presenter.ConsumedCombatFrameCount, traceBaseline = game.Presenter.ConsumedCombatTraceCount;
            long tick = game.Session.LastTick + 1; var shooter = game.Session.ReadModel.Npcs[0].Id;
            for (int i = 0; i < 8; i++)
            {
                var trace = new CombatTraceSegment(tick + i, 900001 + i, shooter, 5632, 5632, 6144, 5632,
                    EntityId.None, CombatEventKind.Broken, 0, 32768);
                var multiTick = new CombatTraceSegment(tick + i, 900100, shooter, 5632 + i * 512, 5632,
                    6144 + i * 512, 5632, EntityId.None, i == 7 ? CombatEventKind.Broken : CombatEventKind.Flying, 0, 32768);
                var events = new List<CombatVisualEvent>();
                if (i == 0) { events.Add(new CombatVisualEvent(tick, 900200, 0, shooter, game.Session.ReadModel.Npcs[1].Id,
                    CombatEventKind.Started, 5632, 5632, 0, 0)); }
                if (i == 1) { events.Add(new CombatVisualEvent(tick + 1, 900200, 0, shooter, game.Session.ReadModel.Npcs[1].Id,
                    CombatEventKind.Contact, 5632, 6656, 0, 7)); }
                queue.Add(new TickResult(tick + i, game.Session.LastStateHash, 0, new List<SimEvent>(), new[] { trace, multiTick }, events));
            }
            ulong hash = StateHasher.Compute(game.Session.World);
            yield return null;
            Assert.That(queue, Is.Empty); Assert.That(game.Presenter.ConsumedCombatFrameCount - baseline, Is.EqualTo(8));
            Assert.That(game.Presenter.ConsumedCombatTraceCount - traceBaseline, Is.EqualTo(16));
            Assert.That(game.Presenter.FindProjectileView(900001), Is.Not.Null);
            Assert.That(game.Presenter.FindProjectileView(900100).PendingTraceCount, Is.EqualTo(8));
            Assert.That(game.Presenter.FindNpcView(shooter).CombatStage, Is.EqualTo(2));
            Assert.That(game.Presenter.FindNpcView(shooter).FacingDirection, Is.EqualTo(2), "Completed catch-up melee must use recorded facing.");
            Vector3 paused = game.Presenter.FindProjectileView(900001).transform.position;
            float clock = game.Runner.AnimationTime;
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(game.Runner.AnimationTime, Is.EqualTo(clock));
            Assert.That(game.Presenter.FindProjectileView(900001).transform.position, Is.EqualTo(paused));
            Assert.That(game.Presenter.ConsumedCombatFrameCount - baseline, Is.EqualTo(8));
            // Deterministic render-clock sampling: all eight same-tick-completed arrows get their own playback slot.
            for (int sample = 0; sample < 40; sample++)
            {
                Set(game.Runner, "AnimationTime", game.Runner.AnimationTime + 0.125f);
                game.Presenter.SendMessage("LateUpdate");
                if (sample == 3)
                {
                    Assert.That(game.Presenter.FindNpcView(shooter).CombatStage, Is.EqualTo(3));
                    Assert.That(game.Presenter.FindNpcView(shooter).FacingDirection, Is.EqualTo(2));
                }
            }
            for (int i = 0; i < 8; i++) { Assert.That(game.Presenter.FindProjectileView(900001 + i), Is.Null); }
            Assert.That(game.Presenter.FindProjectileView(900100), Is.Null);
            Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
        }

        [Test]
        public void WinterInitialPopulation_IsOpaqueAndRegionalResidentsDespawnWithoutDeath()
        {
            var config = new WorldConfigData(42, 32, 32, 2, GameCalendar.WinterStartTick, 2, new[] { 1, 2, 4, 8 });
            using (var session = new GameSession(config))
            {
                var root = new GameObject("WinterPresentationTest");
                try
                {
                    var runner = root.AddComponent<SimulationRunner>(); runner.enabled = false;
                    runner.Initialize(session, config); runner.Pause();
                    var presenter = root.AddComponent<WorldPresenter>(); presenter.enabled = false;
                    presenter.Initialize(session, runner); presenter.SendMessage("LateUpdate");
                    foreach (NpcSnapshot npc in session.ReadModel.Npcs)
                    {
                        Assert.That(presenter.FindNpcView(npc.Id).GetComponent<SpriteRenderer>().color.a, Is.EqualTo(1f));
                    }
                    AnimalSnapshot local = session.ReadModel.EcologyEconomy.Animals.First(a => presenter.FindAnimalView(a.Id) != null);
                    AnimalView view = presenter.FindAnimalView(local.Id); Assert.That(view.Opacity, Is.EqualTo(1f));
                    AnimalState animal = session.World.Ecology.Find(local.Id.Value);
                    Set(animal, "Region", 1); Set(session.World, "Tick", session.World.Tick + 1);
                    session.ReadModel.Refresh(session.World, null, 0);
                    ulong hash = StateHasher.Compute(session.World); presenter.SendMessage("LateUpdate");
                    Assert.That(presenter.FindAnimalView(local.Id), Is.Null);
                    Assert.That(view.IsDespawning, Is.True); Assert.That(view.IsDying, Is.False);
                    Assert.That(StateHasher.Compute(session.World), Is.EqualTo(hash));
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        private static ProjectileState AddArrow(WorldState world, EntityId shooter, GridPos cell, bool embedded, EntityId target)
        {
            long id = world.Combat.NextProjectileId, attackId = world.Combat.NextAttackId;
            Set(world.Combat, "NextProjectileId", id + 1); Set(world.Combat, "NextAttackId", attackId + 1);
            var arrow = Construct<ProjectileState>(id, shooter);
            Set(arrow, "AttackId", attackId); Set(arrow, "Phase", embedded ? ProjectilePhase.Embedded : ProjectilePhase.Flying);
            Set(arrow, "X", cell.X * 1024 + 512); Set(arrow, "Y", cell.Y * 1024 + 512);
            Set(arrow, "VelocityX", embedded ? 0 : 1024); Set(arrow, "ReleasedTick", world.Tick - (embedded ? 1 : 0));
            Set(arrow, "LastAdvancedTick", world.Tick); Set(arrow, "RemainingTicks", embedded ? 3 : 4);
            Set(arrow, "Damage", 11); Set(arrow, "Integrity", 1); Set(arrow, "LastWalkablePosition", cell);
            Set(arrow, "EmbeddedTarget", embedded ? target : EntityId.None); Set(arrow, "IgnoreInitialShooterOverlap", !embedded);
            var arrows = (List<ProjectileState>)typeof(CombatState).GetField("MutableProjectiles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world.Combat);
            arrows.Add(arrow); Set(world.Combat, "ShotsReleased", world.Combat.ShotsReleased + 1);
            // Explicit fixture source preserves the same material invariant checked by production save validation.
            typeof(MaterialBalance).GetMethod("Produce", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world.Materials, new object[] { ItemKind.Arrow, 1 });
            return arrow;
        }

        [UnityTest]
        public IEnumerator InFlightAndEmbeddedArrows_RepeatedLoadsRebuildWithoutReplayingDamageOrChangingUiQueryHash()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause();
            string folder = Path.Combine(Path.GetTempPath(), "CombatPresentation-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "world.json");
            try
            {
                WorldState world = game.Session.World;
                NpcSnapshot shooter = game.Session.ReadModel.Npcs[0], target = game.Session.ReadModel.Npcs[1];
                ProjectileState flying = AddArrow(world, shooter.Id, shooter.Cell, false, EntityId.None);
                ProjectileState embedded = AddArrow(world, shooter.Id, target.Cell, true, target.Id);
                game.Session.ReadModel.Refresh(world, null, 0); yield return null;
                Assert.That(game.Presenter.FindProjectileView(flying.Id), Is.Not.Null);
                Assert.That(game.Presenter.FindProjectileView(embedded.Id).IsEmbedded, Is.True);
                ulong hash = StateHasher.Compute(world);
                Assert.That(game.Session.Save(path), Is.True, game.Session.SaveStatus);
                // Leave actual combat facts queued before load: Load must discard them, not reapply old flashes.
                game.Runner.StepOnce(); game.Runner.StepOnce();
                for (int load = 0; load < 3; load++)
                {
                    Assert.That(game.Runner.Load(path), Is.True, game.Session.SaveStatus); yield return null; yield return null;
                    Assert.That(game.Runner.IsPaused, Is.True); Assert.That(game.Presenter.ProjectileViewCount, Is.EqualTo(2));
                    Assert.That(game.Presenter.transform.GetComponentsInChildren<Transform>().Count(t => t.name == "CombatViews"), Is.EqualTo(1));
                    Assert.That(game.Presenter.PresentedCombatEventCount, Is.Zero); Assert.That(game.Presenter.CombatImpactViewCount, Is.Zero);
                    Assert.That(game.Presenter.FindProjectileView(flying.Id).transform.position,
                        Is.EqualTo(ProjectileView.Position(flying.X, flying.Y)));
                    Assert.That(game.Presenter.FindNpcView(target.Id).CombatStage, Is.EqualTo(-1));
                    game.Selection.Select(target.Id); yield return null;
                    game.Session.ReadModel.GetDetails(target.Id); game.Session.ReadModel.Combat.GetAttack(target.Id);
                    string inspector = game.Hud.InspectorText; Assert.That(inspector, Is.Not.Null);
                    Assert.That(StateHasher.Compute(game.Session.World), Is.EqualTo(hash));
                }
                int traceCount = game.Presenter.ConsumedCombatTraceCount;
                game.Runner.StepOnce(); game.Runner.StepOnce(); yield return null;
                Assert.That(game.Presenter.ConsumedCombatTraceCount, Is.GreaterThan(traceCount), "Restored flight must use real subsequent production sweeps.");
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [UnityTest]
        public IEnumerator GeneratedCombatAtlas_ProvidesDedicatedFramesWithoutChangingOldActionSlots()
        {
            SceneManager.LoadScene("Bootstrap"); yield return null; yield return null;
            var game = Object.FindObjectOfType<GameBootstrap>(); game.Runner.Pause(); var art = game.Presenter.Art;
            foreach (string key in new[] { "projectile_arrow", "projectile_hit", "projectile_miss", "npc_2_0_0_6_0" })
            {
                Assert.That(art.Get(key), Is.Not.Null, "Generate the appended combat atlas before visual verification: " + key);
            }
            for (int role = 0; role < 5; role++)
            for (int look = 0; look < 4; look++)
            for (int direction = 0; direction < 4; direction++)
            for (int stage = 0; stage < 5; stage++)
            for (int frame = 0; frame < 4; frame++)
            {
                string key = "npc_attack_" + role + "_" + look + "_" + direction + "_" + stage + "_" + frame;
                Assert.That(art.Get(key), Is.Not.Null, key);
            }
            for (int species = 0; species < 7; species++)
            for (int direction = 0; direction < 4; direction++)
            for (int frame = 0; frame < 4; frame++)
            {
                string key = "animal_attack_" + species + "_" + direction + "_" + frame;
                Assert.That(art.Get(key), Is.Not.Null, key);
            }
        }
    }
}
