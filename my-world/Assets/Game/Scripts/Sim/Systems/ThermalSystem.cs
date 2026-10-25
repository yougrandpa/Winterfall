using System;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class ThermalSystem
    {
        public static void UpdateWorld(WorldState world)
        {
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                int difference = world.Environment.Temperature - shelter.Temperature;
                shelter.Temperature += Math.Sign(difference) * Math.Min(Math.Abs(difference), Math.Max(1, Math.Abs(difference) / 480));
                if (!IsUsable(world, shelter))
                {
                    shelter.FuelTicks = 0;
                    shelter.HeatBudget = 0;
                    continue;
                }

                if (shelter.FuelTicks == 0 && HasResidents(world, shelter) && HeatingRequired(world, shelter) &&
                    shelter.Inventory.TryRemove(ItemKind.Wood, 1))
                {
                    world.Materials.Consume(ItemKind.Wood, 1);
                    shelter.FuelTicks = SurvivalRules.FuelTicksPerWood;
                    shelter.HeatBudget = SurvivalRules.HeatPerWood;
                }

                if (shelter.FuelTicks > 0)
                {
                    int heat = Math.Min(SurvivalRules.HeatPerTick, shelter.HeatBudget);
                    shelter.HeatBudget -= heat;
                    shelter.FuelTicks--;
                    shelter.Temperature += Math.Min(heat, Math.Max(0, SurvivalRules.ShelterTargetTemperature - shelter.Temperature));
                    if (shelter.FuelTicks == 0)
                    {
                        shelter.HeatBudget = 0;
                    }
                }

                shelter.Temperature = Math.Max(EnvironmentState.MinTemperature, Math.Min(EnvironmentState.MaxTemperature, shelter.Temperature));
            }
        }

        public static void UpdateNpc(WorldState world, NpcState npc)
        {
            SurvivalNpcState state = npc.Survival;
            if (state.Life == LifeState.Dead)
            {
                return;
            }

            bool sheltered = IsAtShelter(world, npc);
            int temperature = sheltered ? world.Survival.FindShelter(state.ShelterId).Temperature : world.Environment.Temperature;
            int wetChange;
            if (sheltered)
            {
                wetChange = temperature >= 1200 ? -80 : -15;
            }
            else if (world.Environment.Weather == WeatherKind.Snow || world.Environment.Weather == WeatherKind.Blizzard)
            {
                wetChange = world.Environment.Weather == WeatherKind.Blizzard ? 24 : 8;
                if (state.CoatDurability > 0)
                {
                    wetChange /= 2;
                }
            }
            else if (world.Environment.SnowDepth > 0 && temperature > 0 && npc.Action.Type == NpcActionType.Move)
            {
                wetChange = 4;
            }
            else
            {
                wetChange = -12 - world.Environment.Daylight / 1000;
            }

            state.Wetness = Math.Max(0, Math.Min(10000, state.Wetness + wetChange));
            int exposure = EffectiveTemperature(world, npc, sheltered);
            int comfort = state.Life == LifeState.Active && npc.Action.Type != NpcActionType.Sleep ? 500 : 1200;
            if (exposure < comfort)
            {
                int cooling = Math.Max(1, (comfort - exposure + 599) / 600);
                state.BodyTemperature = Math.Max(2000, state.BodyTemperature - cooling);
            }
            else
            {
                int warming = Math.Max(2, Math.Min(12, (exposure - comfort) / 150 + 2));
                state.BodyTemperature = Math.Min(3700, state.BodyTemperature + warming);
            }

            if (state.IsCold)
            {
                state.ColdTicks = Math.Min(SurvivalRules.MaxColdTicks, state.ColdTicks + 1);
                int damage = 6 + (3500 - state.BodyTemperature) / 50 + Math.Min(30, state.ColdTicks / 120);
                int previousHealth = npc.Needs.Health;
                npc.Needs.Health = Math.Max(0, previousHealth - damage);
                if (previousHealth > 0 && npc.Needs.Health == 0) { state.DeathCause = NpcDeathCause.Cold; }
            }
            else
            {
                state.ColdTicks = Math.Max(0, state.ColdTicks - 4);
            }

            if (!sheltered && state.CoatDurability > 0 && (temperature < 500 || state.Wetness > 0) &&
                (world.Tick + npc.Id.Value) % SurvivalRules.CoatWearInterval == 0)
            {
                state.CoatDurability--;
                if (state.CoatDurability == 0)
                {
                    world.Materials.Consume(ItemKind.WinterCoat, 1);
                }
            }
        }

        public static bool CanHuntTrip(WorldState world, NpcState npc, int outwardCost)
        {
            int exposure = EffectiveTemperature(world, npc, false);
            int cooling = exposure >= 500 ? 0 : Math.Max(1, (500 - exposure + 599) / 600);
            int duration = outwardCost * 3 + 45;
            return npc.Survival.BodyTemperature - cooling * duration >= 3600 && npc.Survival.Wetness < 5000 &&
                (!HuntingPreparation.CoatRequired(world) || npc.Survival.CoatDurability > 0);
        }

        public static bool IsSafe(WorldState world, NpcState npc)
        {
            return npc.Survival.Life != LifeState.Dead && npc.Survival.BodyTemperature >= 3500 &&
                npc.Survival.Wetness < 5000 && EffectiveTemperature(world, npc, IsAtShelter(world, npc)) >=
                (npc.Survival.Life == LifeState.Active && npc.Action.Type != NpcActionType.Sleep ? 500 : 1200);
        }

        public static bool ShouldSeekWarmth(WorldState world, NpcState npc)
        {
            if (npc.Survival.Life == LifeState.Dead)
            {
                return false;
            }

            if (npc.Survival.BodyTemperature < 3600 || npc.Survival.Wetness >= 5000)
            {
                return true;
            }

            if (IsAtShelter(world, npc) || EffectiveTemperature(world, npc, false) >= 500) { return false; }
            long returnTick = world.Tick + npc.Position.ManhattanDistance(npc.Home) * 2L + 30;
            bool nightOnReturn = EnvironmentSystem.SolarDaylight(GameCalendar.FromTick(returnTick)) < 2000;
            return !world.Environment.IsDaytime || nightOnReturn || world.Environment.Weather == WeatherKind.Blizzard ||
                world.Environment.Weather == WeatherKind.ColdSnap;
        }

        public static bool NeedsFuel(WorldState world)
        {
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                if (IsUsable(world, shelter) && HasResidents(world, shelter) && HeatingRequired(world, shelter) &&
                    shelter.Inventory.Count(ItemKind.Wood) * SurvivalRules.FuelTicksPerWood + shelter.FuelTicks < 480)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAtShelter(WorldState world, NpcState npc)
        {
            ShelterState shelter = world.Survival.FindShelter(npc.Survival.ShelterId);
            if (shelter == null || npc.Survival.Life == LifeState.Dead || !IsUsable(world, shelter) ||
                npc.Home != shelter.Entrance || npc.Position != shelter.Entrance)
            {
                return false;
            }

            int residents = 0;
            foreach (NpcState resident in world.Entities.Npcs)
            {
                if (resident.Survival.Life == LifeState.Dead || resident.Survival.ShelterId != shelter.Id)
                {
                    continue;
                }

                residents++;
                if (resident.Id == npc.Id)
                {
                    return residents <= shelter.Capacity;
                }
            }

            return false;
        }

        private static int EffectiveTemperature(WorldState world, NpcState npc, bool sheltered)
        {
            SurvivalNpcState state = npc.Survival;
            int temperature = sheltered ? world.Survival.FindShelter(state.ShelterId).Temperature :
                world.Environment.Temperature - world.Environment.Wind * 12;
            temperature -= state.Wetness / 5;
            if (state.CoatDurability > 0)
            {
                temperature += 1800;
            }

            return temperature;
        }

        private static bool IsUsable(WorldState world, ShelterState shelter)
        {
            return world.Map.IsInside(shelter.Position) && world.Map.GetTile(shelter.Position) == TileType.House &&
                world.Map.IsWalkable(shelter.Entrance) && shelter.Position.ManhattanDistance(shelter.Entrance) == 1;
        }

        private static bool HasResidents(WorldState world, ShelterState shelter)
        {
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Survival.Life != LifeState.Dead && npc.Survival.ShelterId == shelter.Id)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HeatingRequired(WorldState world, ShelterState shelter)
        {
            if (world.Environment.Temperature < 1200)
            {
                return true;
            }

            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Survival.Life != LifeState.Dead && npc.Survival.ShelterId == shelter.Id && npc.Survival.BodyTemperature < 3600)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
