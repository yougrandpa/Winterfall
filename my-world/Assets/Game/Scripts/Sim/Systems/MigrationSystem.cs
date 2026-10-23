using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class MigrationSystem
    {
        public const int RegionUpdateTicks = 60;
        private static readonly int[] Dx = { 0, 1, 0, -1 };
        private static readonly int[] Dy = { 1, 0, -1, 0 };

        public static void Initialize(WorldState world)
        {
            if (world.Ecology.Regions.Regions.Count != 0)
            {
                return;
            }
            int localPatches = ((world.Map.Width + EcologyState.PatchSize - 1) / EcologyState.PatchSize) *
                ((world.Map.Height + EcologyState.PatchSize - 1) / EcologyState.PatchSize);
            for (int side = 0; side < 2; side++)
            {
                GridPos exit = OpenExit(world, side == 0);
                if (!exit.IsValid)
                {
                    throw new InvalidOperationException("No safe migration corridor avoiding houses/facilities.");
                }
                world.Ecology.Regions.MutableRegions.Add(new EcologyRegionState(side, exit, localPatches + side,
                    side == 0 ? 700 : -300) { LastUpdateTick = world.Tick });
            }
            world.Ecology.Regions.LastUpdateTick = world.Tick;
        }

        private static GridPos OpenExit(WorldState world, bool west)
        {
            WorldMap map = world.Map;
            // BFS finds an existing-land corridor first in generated maps. If it
            // must cross a blocked cell, only that minimal corridor is opened.
            var previous = new int[map.CellCount];
            for (int i = 0; i < previous.Length; i++)
            {
                previous[i] = -2;
            }
            var queue = new Queue<GridPos>();
            foreach (GridPos cell in map.CollectReachable(world.Center))
            {
                int index = cell.Y * map.Width + cell.X;
                previous[index] = -1;
                queue.Enqueue(cell);
            }
            GridPos goal = GridPos.Invalid;
            while (queue.Count > 0)
            {
                GridPos current = queue.Dequeue();
                if (current.X == (west ? 0 : map.Width - 1) && current.Y > 0 && current.Y < map.Height - 1)
                {
                    goal = current;
                    break;
                }
                for (int d = 0; d < 4; d++)
                {
                    GridPos next = current.Offset(Dx[d], Dy[d]);
                    if (!map.IsInside(next) || map.GetTile(next) == TileType.House)
                    {
                        continue;
                    }
                    bool facilityCell = false;
                    foreach (FacilityState facility in world.Survival.Facilities)
                    {
                        if (facility.Position == next || facility.WorkCell == next)
                        {
                            facilityCell = true;
                            break;
                        }
                    }
                    if (facilityCell)
                    {
                        continue;
                    }
                    int index = next.Y * map.Width + next.X;
                    if (previous[index] != -2)
                    {
                        continue;
                    }
                    previous[index] = current.Y * map.Width + current.X;
                    queue.Enqueue(next);
                }
            }
            if (!goal.IsValid)
            {
                return goal;
            }
            int cursor = goal.Y * map.Width + goal.X;
            while (cursor >= 0)
            {
                var cell = new GridPos(cursor % map.Width, cursor / map.Width);
                if (!map.IsWalkable(cell))
                {
                    map.SetTile(cell, TileType.Dirt);
                }
                cursor = previous[cursor];
            }
            return goal;
        }

        public static void Update(WorldState world, List<SimEvent> events)
        {
            EcologyState ecology = world.Ecology;
            if (!ecology.Regions.Enabled)
            {
                foreach (AnimalState animal in ecology.Animals)
                {
                    animal.MigrationDestination = -1;
                    animal.MigrationPath.Clear();
                    animal.MigrationPathIndex = 0;
                }
                return;
            }
            if (world.Tick - ecology.Regions.LastUpdateTick >= RegionUpdateTicks)
            {
                ecology.Regions.LastUpdateTick = world.Tick;
                foreach (AnimalState animal in ecology.Animals)
                {
                    if (animal.Health <= 0 || animal.LifeStage == AnimalLifeStage.Infant ||
                        animal.LastMigrationTick >= 0 && world.Tick - animal.LastMigrationTick < 3 * GameCalendar.TicksPerDay)
                    {
                        continue;
                    }
                    if (animal.Region >= 0)
                    {
                        TryReturn(world, animal);
                    }
                    else if (animal.MigrationDestination < 0)
                    {
                        PlanDeparture(world, animal);
                    }
                }
            }
            foreach (AnimalState animal in ecology.Animals)
            {
                if (animal.Health <= 0 || animal.Region >= 0 || animal.MigrationDestination < 0)
                {
                    continue;
                }
                EcologyRegionState destination = ecology.Regions.MutableRegions[animal.MigrationDestination];
                if (animal.Position == destination.Exit)
                {
                    Depart(world, animal, destination);
                }
            }
        }

        private static void PlanDeparture(WorldState world, AnimalState animal)
        {
            EcologyState ecology = world.Ecology;
            HabitatPatchState local = ecology.FindHabitat(animal.HabitatId);
            bool stress = animal.Energy < animal.Definition.EnergyCapacity / 4 || animal.Hydration < 500 ||
                local.AvailableWater(world.Environment.Temperature) < animal.Definition.WaterPerHour * 12 ||
                local.AvailablePlantFood(animal.Species, world.Date.Season) < local.LandCells * 12 || local.SnowDepth > 5000 &&
                (animal.Species == AnimalSpecies.Deer || animal.Species == AnimalSpecies.Raven);
            if (!stress || animal.PregnancyLitter > 0 || animal.Behavior == AnimalBehavior.Nurse)
            {
                return;
            }
            foreach (AnimalState child in ecology.Animals)
            {
                if (child.MotherId == animal.Id && child.Health > 0 && child.LifeStage == AnimalLifeStage.Infant)
                {
                    return;
                }
            }
            EcologyRegionState best = null;
            long bestScore = Score(world, local, animal, world.Environment.Temperature);
            foreach (EcologyRegionState region in ecology.Regions.Regions)
            {
                HabitatPatchState patch = ecology.FindHabitat(region.HabitatId);
                long score = Score(world, patch, animal, world.Environment.Temperature + region.TemperatureOffset) -
                    animal.Position.ManhattanDistance(region.Exit) * 4;
                if (score > bestScore + 100)
                {
                    bestScore = score;
                    best = region;
                }
            }
            if (best == null)
            {
                return;
            }
            PathResult path = new GridPathfinder().FindPath(world.Map, animal.Position, best.Exit);
            if (!path.Found)
            {
                return;
            }
            animal.MigrationDestination = best.Id;
            animal.MigrationPath.Clear();
            animal.MigrationPath.AddRange(path.Path);
            animal.MigrationPathIndex = 0;
            animal.Behavior = AnimalBehavior.Migrate;
            animal.TargetPosition = best.Exit;
        }

        private static long Score(WorldState world, HabitatPatchState patch, AnimalState animal, int temperature)
        {
            long food = patch.AvailablePlantFood(animal.Species, world.Date.Season);
            long demand = 0;
            foreach (AnimalState resident in world.Ecology.Animals)
            {
                if (resident.Health <= 0 || resident.HabitatId != patch.Id)
                {
                    continue;
                }
                if (animal.Definition.IsPredator && animal.Definition.CanPreyOn(resident.Species))
                {
                    food += resident.BodyMass;
                }
                if (resident.Definition.IsPredator == animal.Definition.IsPredator)
                {
                    demand += resident.Definition.MetabolismPerHour * 72;
                }
            }
            if (animal.Definition.EatsCarrion)
            {
                foreach (CarcassState carcass in world.Ecology.Carcasses)
                {
                    if (carcass.HabitatId == patch.Id)
                    {
                        food += carcass.MeatRemaining * 100;
                    }
                }
            }
            food = Math.Max(0, food - demand) / Math.Max(1, patch.LandCells);
            return food * 4 + patch.AvailableWater(temperature) / Math.Max(1, patch.LandCells) * 3 + patch.Shelter / 10 -
                patch.SnowDepth / 8 - Math.Max(0, animal.Definition.ColdTolerance - temperature) / 3;
        }

        private static void Depart(WorldState world, AnimalState animal, EcologyRegionState region)
        {
            animal.Region = region.Id;
            animal.Position = GridPos.Invalid;
            animal.HabitatId = region.HabitatId;
            animal.TerritoryHabitatId = region.HabitatId;
            animal.LastMigrationTick = world.Tick;
            animal.LastNeedsTick = world.Tick;
            animal.TargetPosition = GridPos.Invalid;
            animal.TargetAnimalId = EntityId.None;
            animal.ReservedBy = EntityId.None;
            animal.MigrationDestination = -1;
            animal.MigrationPath.Clear();
            animal.MigrationPathIndex = 0;
            animal.Behavior = AnimalBehavior.Rest;
            InvalidateTargets(world, animal);
            world.Ecology.Regions.Departures++;
            region.Immigration++;
        }

        private static void TryReturn(WorldState world, AnimalState animal)
        {
            EcologyRegionState region = world.Ecology.Regions.MutableRegions[animal.Region];
            HabitatPatchState outside = world.Ecology.FindHabitat(region.HabitatId);
            HabitatPatchState local = world.Ecology.HabitatAt(region.Exit);
            bool springReturn = world.Date.Season == Season.Spring && local.SnowDepth < 2000 &&
                local.Water > animal.Definition.WaterPerHour * 24 && local.PlantBiomass > local.LandCells * 20;
            bool resourceReturn = Score(world, local, animal, world.Environment.Temperature) >
                Score(world, outside, animal, world.Environment.Temperature + region.TemperatureOffset) + 200;
            if ((!springReturn && !resourceReturn) || !world.Map.IsWalkable(region.Exit))
            {
                return;
            }
            // Crossing commits the same object, ID and random stream. No spawn.
            animal.Region = -1;
            animal.Position = region.Exit;
            animal.HabitatId = local.Id;
            animal.TerritoryHabitatId = local.Id;
            animal.LastMigrationTick = world.Tick;
            animal.LastNeedsTick = world.Tick;
            animal.NextDecisionTick = world.Tick;
            animal.TargetAnimalId = EntityId.None;
            animal.TargetPosition = GridPos.Invalid;
            InvalidateTargets(world, animal);
            world.Ecology.Regions.Returns++;
            region.Emigration++;
        }

        private static void InvalidateTargets(WorldState world, AnimalState migrated)
        {
            foreach (AnimalState observer in world.Ecology.Animals)
            {
                if (observer.TargetAnimalId == migrated.Id && observer.Region != migrated.Region)
                {
                    observer.LastTargetId = migrated.Id;
                    observer.TargetAnimalId = EntityId.None;
                    observer.TargetInvalidatedTick = world.Tick;
                }
            }
        }
    }
}
