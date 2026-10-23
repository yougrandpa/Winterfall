using System;
using System.Collections.Generic;
using System.Linq;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class EnvironmentThermalTests
    {
        private static WorldState Empty(int count = 1, bool withShelter = true)
        {
            var config = new WorldConfigData(42, 16, 16, count, 97 * 1440 + 720, 2, new[] { 1 });
            var world = new WorldState(config, new WorldMap(16, 16), new DeterministicRng(42));
            var entrance = new GridPos(8, 8);
            if (withShelter)
            {
                var shelter = new ShelterState(world.Survival.NextId++, entrance.Offset(0, 1), entrance, 4);
                world.Map.SetTile(shelter.Position, TileType.House);
                world.Survival.MutableShelters.Add(shelter);
            }
            for (int i = 0; i < count; i++)
            {
                var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("Test" + i, "Builder", 25),
                    new Personality(), entrance, (ulong)(42 + i));
                if (withShelter)
                {
                    npc.Survival.ShelterId = world.Survival.Shelters[0].Id;
                }
                world.Entities.AddNpc(npc);
                world.Market.IssuedCurrency += npc.Coins;
            }
            world.Environment.Temperature = -1500;
            world.Environment.Weather = WeatherKind.Clear;
            world.Environment.Daylight = 10000;
            world.Environment.NextWeatherTick = long.MaxValue;
            world.Materials.Initialize(world);
            return world;
        }

        private static void Conserved(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString());
            }
        }

        [TestCase(42)]
        [TestCase(20261020)]
        [TestCase(731)]
        [TestCase(120045)]
        public void Factory_TwentyNpcs_HaveRealReachableHomesAndFiniteFirstNightFuel(int seed)
        {
            var config = new WorldConfigData(seed, 64, 64, 20, 84 * 1440 + 360, 2, new[] { 1 });
            WorldState world = WorldFactory.Create(config);
            Assert.That(world.Entities.Npcs.Count, Is.EqualTo(20));
            Assert.That(world.Environment.WaterFrozen, Is.True);
            Assert.That(world.Survival.Shelters.Sum(s => s.Capacity), Is.GreaterThanOrEqualTo(20));
            bool distinctSpawn = false;
            foreach (NpcState npc in world.Entities.Npcs)
            {
                ShelterState shelter = world.Survival.FindShelter(npc.Survival.ShelterId);
                Assert.That(shelter, Is.Not.Null);
                Assert.That(world.Map.GetTile(shelter.Position), Is.EqualTo(TileType.House));
                Assert.That(shelter.Position.ManhattanDistance(shelter.Entrance), Is.EqualTo(1));
                Assert.That(npc.Home, Is.EqualTo(shelter.Entrance));
                Assert.That(world.Map.IsReachable(world.Center, npc.Home), Is.True);
                Assert.That(world.Map.IsReachable(npc.Position, npc.Home), Is.True);
                distinctSpawn |= npc.Position != npc.Home;
            }
            Assert.That(distinctSpawn, Is.True);
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                Assert.That(world.Entities.Npcs.Count(n => n.Survival.ShelterId == shelter.Id), Is.LessThanOrEqualTo(shelter.Capacity));
                Assert.That(shelter.Inventory.Count(ItemKind.Wood), Is.EqualTo(SurvivalRules.FirstNightWood));
                Assert.That(shelter.FuelTicks, Is.Zero);
                Assert.That(shelter.HeatBudget, Is.Zero);
            }
            Conserved(world);
        }

        [Test]
        public void FirstNight_TwentyResidentsStayWarm_DeterministicallyUntilFiniteFuelRunsOut()
        {
            var config = new WorldConfigData(731, 64, 64, 20, 97 * 1440 + 360, 2, new[] { 1 });
            WorldState first = WorldFactory.Create(config);
            WorldState second = WorldFactory.Create(config);
            foreach (WorldState world in new[] { first, second })
            {
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    npc.Position = npc.Home;
                    npc.Action.SetTimed(NpcActionType.Sleep, "First night", npc.Home, world.Tick + 1920);
                }
            }
            for (int i = 0; i < SurvivalRules.FirstNightWood * SurvivalRules.FuelTicksPerWood; i++)
            {
                foreach (WorldState world in new[] { first, second })
                {
                    EnvironmentSystem.Update(world);
                    ThermalSystem.UpdateWorld(world);
                    foreach (NpcState npc in world.Entities.Npcs)
                    {
                        ThermalSystem.UpdateNpc(world, npc);
                        Assert.That(npc.Survival.BodyTemperature, Is.EqualTo(3700));
                        Assert.That(npc.Survival.IsCold, Is.False);
                    }
                    Conserved(world);
                    world.Tick++;
                }
                for (int index = 0; index < first.Survival.Shelters.Count; index++)
                {
                    ShelterState a = first.Survival.Shelters[index];
                    ShelterState b = second.Survival.Shelters[index];
                    Assert.That(a.Temperature, Is.EqualTo(b.Temperature));
                    Assert.That(a.FuelTicks, Is.EqualTo(b.FuelTicks));
                    Assert.That(a.HeatBudget, Is.EqualTo(b.HeatBudget));
                    Assert.That(a.Inventory.Count(ItemKind.Wood), Is.EqualTo(b.Inventory.Count(ItemKind.Wood)));
                }
            }
            Assert.That(first.Survival.Shelters.Sum(s => s.Inventory.Count(ItemKind.Wood)), Is.Zero);
            Assert.That(first.Survival.Shelters.Sum(s => s.FuelTicks), Is.Zero);
            Assert.That(first.Materials.Consumed(ItemKind.Wood), Is.EqualTo(first.Survival.Shelters.Count * SurvivalRules.FirstNightWood));
        }

        [Test]
        public void Factory_MissingMappedHouse_AddsRealHousingBeforeSpawning()
        {
            WorldState world = WorldFactory.Create(new WorldConfigData(42, 64, 64, 40, 360, 2, new[] { 1 }));
            Assert.That(world.Entities.Npcs.Count, Is.EqualTo(40));
            Assert.That(world.Survival.Shelters.Count, Is.GreaterThanOrEqualTo(10));
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                Assert.That(world.Map.GetTile(shelter.Position), Is.EqualTo(TileType.House));
                Assert.That(world.Map.IsReachable(world.Center, shelter.Entrance), Is.True);
            }
            Conserved(world);
        }

        [Test]
        public void Factory_FullShelter_RejectsSpawnWithoutAllocatingOrMutatingRng()
        {
            WorldState world = Empty(4);
            int nextId = world.NextEntityId;
            ulong rng = world.Rng.State;
            Assert.That(WorldFactory.SpawnNpc(world, new GridPos(7, 8), true, null), Is.Null);
            Assert.That(world.NextEntityId, Is.EqualTo(nextId));
            Assert.That(world.Rng.State, Is.EqualTo(rng));
            world.Entities.Npcs[0].Survival.Life = LifeState.Dead;
            NpcState arrival = WorldFactory.SpawnNpc(world, new GridPos(7, 8), true, null);
            Assert.That(arrival, Is.Not.Null);
            Assert.That(arrival.Home, Is.EqualTo(world.Survival.Shelters[0].Entrance));
            Assert.That(arrival.Position, Is.EqualTo(new GridPos(7, 8)));
        }

        [Test]
        public void EmptyWorld_NoNpcsOrShelters_UpdatesNormally()
        {
            WorldState world = Empty(0, false);
            world.Environment.NextWeatherTick = 0;
            for (int i = 0; i < 1440; i++)
            {
                EnvironmentSystem.Update(world);
                ThermalSystem.UpdateWorld(world);
                WaterSystem.Update(world);
                world.Tick++;
            }
            Assert.That(world.LivingCount, Is.Zero);
            Assert.That(world.Survival.Shelters.Count, Is.Zero);
            Assert.That(ThermalSystem.NeedsFuel(world), Is.False);
            Conserved(world);
        }

        [Test]
        public void Solar_WinterDaysAreShorter_WithBoundedSymmetricIntegerSunlight()
        {
            int summer = 0;
            int winter = 0;
            for (int minute = 0; minute < 1440; minute++)
            {
                int summerLight = EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(41 * 1440 + minute));
                int winterLight = EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(97 * 1440 + minute));
                Assert.That(summerLight, Is.InRange(0, 10000));
                Assert.That(winterLight, Is.InRange(0, 10000));
                Assert.That(winterLight, Is.EqualTo(EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(97 * 1440 + (1440 - minute) % 1440))));
                if (summerLight >= 2000)
                {
                    summer++;
                }
                if (winterLight >= 2000)
                {
                    winter++;
                }
            }
            Assert.That(winter, Is.LessThan(summer));
            Assert.That(EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(97 * 1440 + 720)), Is.EqualTo(10000));
            Assert.That(EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(97 * 1440 + 360)), Is.Zero);
            Assert.That(EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(41 * 1440 + 360)), Is.GreaterThan(2000));
        }

        [Test]
        public void Weather_FourteenWinterDays_ReplaysExactlyAndKeepsAllFieldsBounded()
        {
            WorldState first = Empty(0, false);
            WorldState second = Empty(0, false);
            first.Environment.NextWeatherTick = 0;
            second.Environment.NextWeatherTick = 0;
            ulong runtimeRng = first.Rng.State;
            var weather = new HashSet<WeatherKind>();
            for (int i = 0; i < 14 * 1440; i++)
            {
                EnvironmentSystem.Update(first);
                EnvironmentSystem.Update(second);
                EnvironmentState a = first.Environment;
                EnvironmentState b = second.Environment;
                Assert.That(a.Temperature, Is.EqualTo(b.Temperature));
                Assert.That(a.Wind, Is.EqualTo(b.Wind));
                Assert.That(a.Daylight, Is.EqualTo(b.Daylight));
                Assert.That(a.SnowDepth, Is.EqualTo(b.SnowDepth));
                Assert.That(a.Weather, Is.EqualTo(b.Weather));
                Assert.That(a.NextWeatherTick, Is.EqualTo(b.NextWeatherTick));
                Assert.That(a.RngState, Is.EqualTo(b.RngState));
                Assert.That(a.Temperature, Is.InRange(EnvironmentState.MinTemperature, EnvironmentState.MaxTemperature));
                Assert.That(a.Wind, Is.InRange(0, 100));
                Assert.That(a.Daylight, Is.InRange(0, 10000));
                Assert.That(a.SnowDepth, Is.InRange(0, EnvironmentState.MaxSnowDepth));
                Assert.That(a.NextWeatherTick, Is.GreaterThan(first.Tick));
                Assert.That(a.RngState, Is.Not.Zero);
                weather.Add(a.Weather);
                first.Tick++;
                second.Tick++;
            }
            Assert.That(weather.Count, Is.EqualTo(4));
            Assert.That(first.Rng.State, Is.EqualTo(runtimeRng));
        }

        [Test]
        public void Snow_BlizzardAddsFiniteSnow_AndActualWarmthMeltsWithoutProducingInventory()
        {
            WorldState world = Empty(0, false);
            world.Environment.Weather = WeatherKind.Blizzard;
            world.Environment.SnowDepth = EnvironmentState.MaxSnowDepth - 2;
            EnvironmentSystem.Update(world);
            Assert.That(world.Environment.SnowDepth, Is.EqualTo(EnvironmentState.MaxSnowDepth));
            world.Tick = 41 * 1440 + 720;
            world.Environment.Weather = WeatherKind.Clear;
            world.Environment.Temperature = 1000;
            EnvironmentSystem.Update(world);
            Assert.That(world.Environment.SnowDepth, Is.LessThan(EnvironmentState.MaxSnowDepth));
            world.Environment.SnowDepth = 1;
            EnvironmentSystem.Update(world);
            Assert.That(world.Environment.SnowDepth, Is.Zero);
            Assert.That(world.Resources.Stockpile.Count(ItemKind.RawWater), Is.Zero);
            Conserved(world);
        }

        [Test]
        public void Furnace_OneWoodHasFiniteHeatAndDuration_ThenRoomCoolsGradually()
        {
            WorldState world = Empty();
            ShelterState shelter = world.Survival.Shelters[0];
            shelter.Inventory.TryAdd(ItemKind.Wood, 1);
            world.Materials.Initialize(world);
            for (int i = 0; i < SurvivalRules.FuelTicksPerWood; i++)
            {
                ThermalSystem.UpdateWorld(world);
                Assert.That(shelter.FuelTicks, Is.EqualTo(SurvivalRules.FuelTicksPerWood - i - 1));
                Assert.That(shelter.HeatBudget, Is.EqualTo(shelter.FuelTicks * SurvivalRules.HeatPerTick));
                Assert.That(shelter.Temperature, Is.LessThanOrEqualTo(SurvivalRules.ShelterTargetTemperature));
                Conserved(world);
                world.Tick++;
            }
            Assert.That(shelter.Inventory.Count(ItemKind.Wood), Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.Wood), Is.EqualTo(1));
            int hot = shelter.Temperature;
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.Temperature, Is.LessThan(hot));
            Assert.That(hot - shelter.Temperature, Is.LessThan(20));
            for (int i = 0; i < 960; i++)
            {
                ThermalSystem.UpdateWorld(world);
                world.Tick++;
            }
            Assert.That(shelter.FuelTicks, Is.Zero);
            Assert.That(shelter.HeatBudget, Is.Zero);
            Assert.That(shelter.Temperature, Is.LessThan(1200));
            Assert.That(shelter.Temperature, Is.GreaterThanOrEqualTo(world.Environment.Temperature));
            Conserved(world);
        }

        [Test]
        public void Furnace_PublicStockCannotTeleportIntoShelter_AndEmptyHouseDoesNotBurn()
        {
            WorldState world = Empty();
            ShelterState shelter = world.Survival.Shelters[0];
            world.Resources.Stockpile.TryAdd(ItemKind.Wood, 10);
            world.Materials.Initialize(world);
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.FuelTicks, Is.Zero);
            Assert.That(ThermalSystem.NeedsFuel(world), Is.True);
            Assert.That(world.Resources.Stockpile.Count(ItemKind.Wood), Is.EqualTo(10));
            shelter.Inventory.TryAdd(ItemKind.Wood, 1);
            world.Materials.Produce(ItemKind.Wood, 1);
            world.Entities.Npcs[0].Survival.Life = LifeState.Dead;
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.Inventory.Count(ItemKind.Wood), Is.EqualTo(1));
            Assert.That(shelter.FuelTicks, Is.Zero);
            Assert.That(ThermalSystem.NeedsFuel(world), Is.False);
            Conserved(world);
        }

        [Test]
        public void Furnace_ExhaustedHeatBudget_CannotHeatUsingRemainingFuelTicks()
        {
            WorldState world = Empty();
            ShelterState shelter = world.Survival.Shelters[0];
            shelter.FuelTicks = 10;
            shelter.HeatBudget = 0;
            int temperature = shelter.Temperature;
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.Temperature, Is.LessThan(temperature));
            Assert.That(shelter.HeatBudget, Is.Zero);
            Assert.That(shelter.FuelTicks, Is.EqualTo(9));
        }

        [Test]
        public void Shelter_RequiresActualHouseEntranceAndCapacity_NotArbitraryHomeCell()
        {
            WorldState world = Empty(5);
            Assert.That(ThermalSystem.IsAtShelter(world, world.Entities.Npcs[0]), Is.True);
            Assert.That(ThermalSystem.IsAtShelter(world, world.Entities.Npcs[4]), Is.False);
            NpcState npc = world.Entities.Npcs[0];
            npc.Position = npc.Home.Offset(1, 0);
            Assert.That(ThermalSystem.IsAtShelter(world, npc), Is.False);
            npc.Position = npc.Home;
            world.Map.SetTile(world.Survival.Shelters[0].Position, TileType.Grass);
            Assert.That(ThermalSystem.IsAtShelter(world, npc), Is.False);
            world.Map.SetTile(world.Survival.Shelters[0].Position, TileType.House);
            world.Map.SetTile(npc.Home, TileType.Rock);
            Assert.That(ThermalSystem.IsAtShelter(world, npc), Is.False);
        }

        [TestCase(LifeState.Active)]
        [TestCase(LifeState.Downed)]
        [TestCase(LifeState.Recovering)]
        public void Thermal_AllLivingStatesCoolAndTakeColdDamage_EvenWithFullNeeds(LifeState life)
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            npc.Position = npc.Home.Offset(3, 0);
            npc.Survival.Life = life;
            npc.Survival.BodyTemperature = 3400;
            npc.Needs.Hunger = 10000;
            npc.Needs.Thirst = 10000;
            npc.Needs.Fatigue = 10000;
            int health = npc.Needs.Health;
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(npc.Survival.BodyTemperature, Is.LessThan(3400));
            Assert.That(npc.Survival.ColdTicks, Is.EqualTo(1));
            Assert.That(npc.Needs.Health, Is.LessThan(health - 2));
            Assert.That(npc.Needs.Hunger, Is.EqualTo(10000));
            Assert.That(npc.Needs.Thirst, Is.EqualTo(10000));
            Assert.That(npc.Needs.Fatigue, Is.EqualTo(10000));
        }

        [Test]
        public void Thermal_DoesNotRepeatDeprivationDamage_OrChangeDeadBodies()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            world.Survival.Shelters[0].Temperature = 1800;
            npc.Needs.Hunger = 0;
            npc.Needs.Health = 1000;
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(npc.Needs.Health, Is.EqualTo(1000));
            npc.Survival.Life = LifeState.Dead;
            npc.Survival.BodyTemperature = 3400;
            npc.Survival.Wetness = 6000;
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(npc.Survival.BodyTemperature, Is.EqualTo(3400));
            Assert.That(npc.Survival.Wetness, Is.EqualTo(6000));
            Assert.That(npc.Needs.Health, Is.EqualTo(1000));
        }

        [Test]
        public void Thermal_BlizzardWetnessWindAndCoat_ProduceDifferentExposure()
        {
            WorldState dry = Empty();
            WorldState snowy = Empty();
            WorldState coated = Empty();
            WorldState windy = Empty();
            foreach (WorldState world in new[] { dry, snowy, coated, windy })
            {
                world.Entities.Npcs[0].Position = new GridPos(4, 4);
            }
            snowy.Environment.Weather = WeatherKind.Blizzard;
            coated.Environment.Weather = WeatherKind.Blizzard;
            coated.Entities.Npcs[0].Survival.CoatDurability = SurvivalRules.ToolDurability;
            windy.Environment.Wind = 100;
            for (int i = 0; i < 60; i++)
            {
                foreach (WorldState world in new[] { dry, snowy, coated, windy })
                {
                    ThermalSystem.UpdateNpc(world, world.Entities.Npcs[0]);
                    world.Tick++;
                }
            }
            Assert.That(snowy.Entities.Npcs[0].Survival.Wetness, Is.GreaterThan(dry.Entities.Npcs[0].Survival.Wetness));
            Assert.That(coated.Entities.Npcs[0].Survival.Wetness, Is.LessThan(snowy.Entities.Npcs[0].Survival.Wetness));
            Assert.That(coated.Entities.Npcs[0].Survival.BodyTemperature, Is.GreaterThan(snowy.Entities.Npcs[0].Survival.BodyTemperature));
            Assert.That(windy.Entities.Npcs[0].Survival.BodyTemperature, Is.LessThan(dry.Entities.Npcs[0].Survival.BodyTemperature));
        }

        [Test]
        public void Thermal_CoatWearsOnlyOnExposureBoundary_AndBreakConsumesOneCoat()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            npc.Survival.CoatDurability = 1;
            world.Tick = SurvivalRules.CoatWearInterval - npc.Id.Value;
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(1));
            npc.Position = new GridPos(4, 4);
            world.Tick++;
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(1));
            world.Tick += SurvivalRules.CoatWearInterval - 1;
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(npc.Survival.CoatDurability, Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.WinterCoat), Is.EqualTo(1));
            ThermalSystem.UpdateNpc(world, npc);
            Assert.That(world.Materials.Consumed(ItemKind.WinterCoat), Is.EqualTo(1));
        }

        [Test]
        public void Thermal_SevereLongExposure_ClampsCoreWetnessAndColdDuration()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            npc.Position = new GridPos(4, 4);
            npc.Survival.BodyTemperature = 2001;
            npc.Survival.ColdTicks = SurvivalRules.MaxColdTicks - 1;
            npc.Survival.Wetness = 9999;
            world.Environment.Wind = 100;
            world.Environment.Weather = WeatherKind.Blizzard;
            for (int i = 0; i < 600; i++)
            {
                ThermalSystem.UpdateNpc(world, npc);
                Assert.That(npc.Survival.BodyTemperature, Is.InRange(2000, 3700));
                Assert.That(npc.Survival.Wetness, Is.InRange(0, 10000));
                Assert.That(npc.Survival.ColdTicks, Is.InRange(0, SurvivalRules.MaxColdTicks));
                Assert.That(npc.Needs.Health, Is.InRange(0, 10000));
                world.Tick++;
            }
            Assert.That(npc.Survival.BodyTemperature, Is.EqualTo(2000));
            Assert.That(npc.Survival.Wetness, Is.EqualTo(10000));
            Assert.That(npc.Survival.ColdTicks, Is.EqualTo(SurvivalRules.MaxColdTicks));
        }

        [Test]
        public void Health_BedRecoveryCannotCancelHypothermia_ThenWarmthAllowsRecovery()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            ShelterState shelter = world.Survival.Shelters[0];
            shelter.Temperature = 0;
            npc.Survival.Life = LifeState.Recovering;
            npc.Survival.BodyTemperature = 3400;
            npc.Needs.Health = 1500;
            ThermalSystem.UpdateNpc(world, npc);
            int afterCold = npc.Needs.Health;
            Assert.That(HealthSystem.Update(world, npc, new List<SimEvent>()), Is.False);
            Assert.That(npc.Needs.Health, Is.EqualTo(afterCold));
            Assert.That(npc.Survival.Life, Is.EqualTo(LifeState.Recovering));
            shelter.Temperature = 1800;
            npc.Survival.Wetness = 6000;
            for (int i = 0; i < 180; i++)
            {
                ThermalSystem.UpdateNpc(world, npc);
                HealthSystem.Update(world, npc, new List<SimEvent>());
                world.Tick++;
            }
            Assert.That(npc.Survival.BodyTemperature, Is.EqualTo(3700));
            Assert.That(npc.Survival.Wetness, Is.Zero);
            Assert.That(npc.Needs.Health, Is.GreaterThan(afterCold));
            Assert.That(npc.Survival.ColdTicks, Is.Zero);
        }

        [Test]
        public void Health_RescueTransportPreservesColdCoreAndWetness_WithoutTeleportingHome()
        {
            WorldState world = Empty(2);
            NpcState target = world.Entities.Npcs[0];
            NpcState rescuer = world.Entities.Npcs[1];
            target.Position = target.Home.Offset(3, 0);
            rescuer.Position = target.Position;
            target.Survival.Life = LifeState.Downed;
            target.Survival.RescueDeadline = world.Tick + SurvivalRules.RescueWindow;
            target.Survival.Rescuer = rescuer.Id;
            target.Survival.BodyTemperature = 3000;
            target.Survival.Wetness = 7000;
            rescuer.Inventory.TryAdd(ItemKind.Medicine, 1);
            world.Materials.Initialize(world);
            ThermalSystem.UpdateNpc(world, target);
            int core = target.Survival.BodyTemperature;
            int wetness = target.Survival.Wetness;
            GridPos previous = target.Position;
            var step = new PlanStep(PlanStepType.Rescue, target.Position, target.Id.Value);
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out bool ready), Is.True);
            Assert.That(ready, Is.True);
            Assert.That(HealthSystem.CommitRescue(world, rescuer, step, new List<SimEvent>(), out bool complete), Is.True);
            Assert.That(complete, Is.False);
            Assert.That(target.Survival.Life, Is.EqualTo(LifeState.Recovering));
            Assert.That(target.Survival.BodyTemperature, Is.EqualTo(core));
            Assert.That(target.Survival.Wetness, Is.EqualTo(wetness));
            Assert.That(target.Position, Is.EqualTo(previous));
            world.Tick++;
            Assert.That(HealthSystem.PrepareRescue(world, rescuer, step, new GridPathfinder(), out ready), Is.True);
            Assert.That(target.Position.ManhattanDistance(previous), Is.EqualTo(1));
            Assert.That(target.Position, Is.Not.EqualTo(target.Home));
            ThermalSystem.UpdateNpc(world, target);
            Assert.That(target.Survival.BodyTemperature, Is.LessThan(core));
            Conserved(world);
        }

        [Test]
        public void Thermal_SafetyWarmthAndFuelQueries_AreReadOnlyAndReactToConditions()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            ShelterState shelter = world.Survival.Shelters[0];
            shelter.Temperature = 1800;
            Assert.That(ThermalSystem.IsSafe(world, npc), Is.True);
            Assert.That(ThermalSystem.ShouldSeekWarmth(world, npc), Is.False);
            Assert.That(ThermalSystem.NeedsFuel(world), Is.True);
            shelter.Inventory.TryAdd(ItemKind.Wood, 2);
            Assert.That(ThermalSystem.NeedsFuel(world), Is.False);
            npc.Position = new GridPos(4, 4);
            world.Environment.Daylight = 0;
            ulong rng = world.Rng.State;
            ulong weatherRng = world.Environment.RngState;
            Assert.That(ThermalSystem.IsSafe(world, npc), Is.False);
            Assert.That(ThermalSystem.ShouldSeekWarmth(world, npc), Is.True);
            Assert.That(npc.Survival.BodyTemperature, Is.EqualTo(3700));
            Assert.That(npc.Survival.Wetness, Is.Zero);
            Assert.That(shelter.Inventory.Count(ItemKind.Wood), Is.EqualTo(2));
            Assert.That(shelter.FuelTicks, Is.Zero);
            Assert.That(shelter.Temperature, Is.EqualTo(1800));
            Assert.That(world.Rng.State, Is.EqualTo(rng));
            Assert.That(world.Environment.RngState, Is.EqualTo(weatherRng));
        }

        [Test]
        public void Water_SubzeroBlocksSurfaceAndWellCollection_ThawRespectsFacilityCondition()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            var surface = new ResourceNodeState(1, npc.Position, ItemKind.RawWater, 30, 12);
            var wellNode = new ResourceNodeState(2, npc.Position.Offset(1, 0), ItemKind.RawWater, 40, 20);
            world.Resources.AddNode(surface);
            world.Resources.AddNode(wellNode);
            var well = new FacilityState(world.Survival.NextId++, FacilityKind.Well, npc.Position.Offset(2, 0), wellNode.Position)
            {
                Progress = SurvivalRules.ConstructionTicks,
                WellNodeId = wellNode.Id
            };
            world.Survival.MutableFacilities.Add(well);
            world.Materials.Initialize(world);
            Assert.That(WaterSystem.SourceAvailable(world, surface), Is.False);
            Assert.That(WaterSystem.SourceAvailable(world, wellNode), Is.False);
            Assert.That(WaterSystem.CollectionAmount(world, npc), Is.Zero);
            Assert.That(surface.Amount, Is.EqualTo(30));
            Assert.That(npc.Inventory.Count(ItemKind.RawWater), Is.Zero);
            world.Environment.Temperature = 0;
            Assert.That(world.Environment.WaterFrozen, Is.False);
            Assert.That(WaterSystem.SourceAvailable(world, surface), Is.True);
            Assert.That(WaterSystem.SourceAvailable(world, wellNode), Is.True);
            Assert.That(WaterSystem.CollectionAmount(world, npc), Is.GreaterThan(0));
            well.Condition = 0;
            Assert.That(WaterSystem.SourceAvailable(world, wellNode), Is.False);
            Assert.That(WaterSystem.SourceAvailable(world, surface), Is.True);
            world.Environment.Temperature = -1;
            Assert.That(WaterSystem.SourceAvailable(world, new ResourceNodeState(3, npc.Position, ItemKind.Food, 3, 12)), Is.True);
            Assert.That(WaterSystem.SourceAvailable(world, null), Is.False);
            Conserved(world);
        }

        [Test]
        public void Water_FrozenIrrigationPreservesFiniteWater_AndThawConsumesExactlyOnce()
        {
            WorldState world = Empty();
            world.Tick = 30;
            var cistern = new FacilityState(world.Survival.NextId++, FacilityKind.Cistern, new GridPos(10, 10), new GridPos(10, 9))
            {
                Progress = SurvivalRules.ConstructionTicks
            };
            var irrigation = new FacilityState(world.Survival.NextId++, FacilityKind.Irrigation, new GridPos(11, 10), new GridPos(11, 9))
            {
                Progress = SurvivalRules.ConstructionTicks
            };
            world.Survival.MutableFacilities.Add(cistern);
            world.Survival.MutableFacilities.Add(irrigation);
            cistern.Inventory.TryAdd(ItemKind.RawWater, 1);
            var plot = new FarmPlotState(1, new GridPos(12, 10)) { Crop = CropType.Bean };
            world.Resources.AddPlot(plot);
            world.Materials.Initialize(world);
            WaterSystem.Update(world);
            Assert.That(plot.Moisture, Is.Zero);
            Assert.That(cistern.Inventory.Count(ItemKind.RawWater), Is.EqualTo(1));
            world.Environment.Temperature = 1;
            WaterSystem.Update(world);
            Assert.That(plot.Moisture, Is.EqualTo(900));
            Assert.That(cistern.Inventory.Count(ItemKind.RawWater), Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.RawWater), Is.EqualTo(1));
            Conserved(world);
        }
    }
}
