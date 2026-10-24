using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class CombatReadModelTests
    {
        // Reflection is confined to test setup; no friend-assembly change or production mutation API.
        private static void Set(object instance, string property, object value)
        {
            instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);
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
    }
}
