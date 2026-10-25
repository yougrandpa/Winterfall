using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
    public static class WorldFactory
    {
        private static readonly string[] Names =
        {
            "Mira", "Tomas", "Elsa", "Rowan", "Nina", "Bram", "Iris", "Oren",
            "Sana", "Piet", "Lena", "Cato", "Ada", "Jory", "Mae", "Rook",
            "Vale", "Tess", "Ansel", "Faye", "Dorn", "Kira", "Silas", "Wren"
        };

        private static readonly string[] Roles =
        {
            "Farmer", "Gatherer", "Hunter", "Trader", "Builder"
        };

        public static WorldState Create(WorldConfigData config)
        {
            List<SimEvent> ignored;
            return Create(config, out ignored);
        }

        public static WorldState Create(WorldConfigData config, out List<SimEvent> initialEvents)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            initialEvents = new List<SimEvent>();
            WorldMap map = GenerateMap(config);
            ulong rootSeed = DeterministicRng.Mix((ulong)(uint)config.Seed ^ 0xD1B54A32D192ED03UL);
            var world = new WorldState(config, map, new DeterministicRng(rootSeed));
            EnvironmentSystem.Update(world);
            InitializeShelters(world);

            GridPos center = world.Center;
            List<GridPos> reachable = map.CollectReachable(center);
            var candidates = new List<GridPos>();
            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i].ManhattanDistance(center) <= 12)
                {
                    candidates.Add(reachable[i]);
                }
            }

            if (candidates.Count < config.NpcCount)
            {
                candidates.Clear();
                candidates.AddRange(reachable);
            }

            var shuffleRng = new DeterministicRng(DeterministicRng.Mix(rootSeed ^ 0xA24BAED4963EE407UL));
            Shuffle(candidates, ref shuffleRng);

            int toSpawn = Math.Min(config.NpcCount, candidates.Count);
            for (int i = 0; i < toSpawn; i++)
            {
                SpawnNpc(world, candidates[i], true, initialEvents);
            }

            PopulateResources(world, reachable, rootSeed);
            world.Market.Inventory.TryAdd(ItemKind.Food, 30);
            world.Market.Inventory.TryAdd(ItemKind.Water, 30);
            world.Market.Inventory.TryAdd(ItemKind.SpinachSeed, 30);
            world.Ecology.Initialize(world, reachable);
            GrowingBedSystem.Initialize(world);
            world.Campaign.Initialize(world);
            world.Materials.Initialize(world);
            return world;
        }

        public static NpcState SpawnNpc(
            WorldState world,
            GridPos? requestedCell,
            bool allowOverlap,
            List<SimEvent> events)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            GridPos cell;
            if (requestedCell.HasValue)
            {
                cell = requestedCell.Value;
                if (!world.Map.IsWalkable(cell) ||
                    (!allowOverlap && world.Entities.IsCellOccupied(cell)))
                {
                    return null;
                }
            }
            else if (!TryChooseFreeCell(world, out cell))
            {
                return null;
            }

            ShelterState shelter = ChooseShelter(world, cell);
            if (shelter == null)
            {
                return null;
            }

            EntityId id = world.AllocateEntityId();
            int nameIndex = (id.Value - 1) % Names.Length;
            string name = Names[nameIndex];
            if (id.Value > Names.Length)
            {
                name = name + " " + id.Value;
            }

            string role = Roles[(id.Value - 1) % Roles.Length];
            int age = 18 + world.Rng.NextInt(0, 43);
            var personality = new Personality
            {
                Diligence = 15 + world.Rng.NextInt(0, 71),
                Caution = 15 + world.Rng.NextInt(0, 71),
                Sociability = 15 + world.Rng.NextInt(0, 71),
                Curiosity = 15 + world.Rng.NextInt(0, 71)
            };

            uint rngA = world.Rng.NextUInt();
            uint rngB = world.Rng.NextUInt();
            ulong childSeed = ((ulong)rngA << 32) | rngB;
            ulong npcSeed = DeterministicRng.Mix(childSeed ^ (ulong)id.Value);

            var npc = new NpcState(
                id,
                new NpcIdentity(name, role, age),
                personality,
                shelter.Entrance,
                npcSeed)
            {
                Position = cell
            };
            npc.Survival.ShelterId = shelter.Id;
            npc.HasHuntingTool = role == "Hunter";
            if (role == "Hunter")
            {
                npc.Inventory.TryAdd(ItemKind.Arrow, 4);
                world.Materials.Produce(ItemKind.Arrow, 4);
            }
            if (world.Date.Season == Season.Winter && (role == "Hunter" || role == "Gatherer"))
            {
                npc.Survival.CoatDurability = SurvivalRules.ToolDurability;
                world.Materials.Produce(ItemKind.WinterCoat, 1);
            }
            if (role == "Farmer") { npc.Survival.SetTool(ItemKind.Hoe, SurvivalRules.ToolDurability); }
            if (role == "Trader") { npc.Survival.SetTool(ItemKind.Bucket, SurvivalRules.ToolDurability); }
            for (int i = (int)ItemKind.Bow; i <= (int)ItemKind.Bucket; i++) { if (npc.Survival.EquipmentCount((ItemKind)i) > 0) { world.Materials.Produce((ItemKind)i, 1); } }
            world.Entities.AddNpc(npc);
            world.Market.IssuedCurrency += npc.Coins;

            if (events != null)
            {
                events.Add(new SimEvent(
                    SimEventKind.NpcSpawned,
                    world.Tick,
                    id,
                    name + " the " + role + " arrived at " + cell + "."));
            }

            return npc;
        }

        private static void InitializeShelters(WorldState world)
        {
            var reachable = new HashSet<GridPos>(world.Map.CollectReachable(world.Center));
            for (int y = 0; y < world.Map.Height; y++)
            {
                for (int x = 0; x < world.Map.Width; x++)
                {
                    var position = new GridPos(x, y);
                    if (world.Map.GetTile(position) == TileType.House && TryShelterEntrance(world, position, reachable, out GridPos entrance))
                    {
                        AddShelter(world, position, entrance);
                    }
                }
            }

            int capacity = world.Survival.Shelters.Count * SurvivalRules.ShelterCapacity;
            if (capacity >= world.Config.NpcCount)
            {
                return;
            }

            var candidates = new List<GridPos>(reachable);
            candidates.Sort((a, b) =>
            {
                int order = a.ManhattanDistance(world.Center).CompareTo(b.ManhattanDistance(world.Center));
                return order != 0 ? order : (a.Y * world.Map.Width + a.X).CompareTo(b.Y * world.Map.Width + b.X);
            });
            foreach (GridPos position in candidates)
            {
                TileType previous = world.Map.GetTile(position);
                if (position.ManhattanDistance(world.Center) < 4 ||
                    (previous != TileType.Grass && previous != TileType.Dirt && previous != TileType.Forest))
                {
                    continue;
                }

                bool crowded = false;
                foreach (ShelterState shelter in world.Survival.Shelters)
                {
                    if (shelter.Position.ManhattanDistance(position) < 3)
                    {
                        crowded = true;
                        break;
                    }
                }
                if (crowded)
                {
                    continue;
                }

                world.Map.SetTile(position, TileType.House);
                var remaining = new HashSet<GridPos>(world.Map.CollectReachable(world.Center));
                if (remaining.Count != reachable.Count - 1 || !TryShelterEntrance(world, position, remaining, out GridPos entrance))
                {
                    world.Map.SetTile(position, previous);
                    continue;
                }

                AddShelter(world, position, entrance);
                reachable = remaining;
                capacity += SurvivalRules.ShelterCapacity;
                if (capacity >= world.Config.NpcCount)
                {
                    break;
                }
            }
        }

        private static void AddShelter(WorldState world, GridPos position, GridPos entrance)
        {
            var shelter = new ShelterState(world.Survival.NextId++, position, entrance, SurvivalRules.ShelterCapacity);
            shelter.Inventory.TryAdd(ItemKind.Wood, SurvivalRules.FirstNightWood);
            world.Survival.MutableShelters.Add(shelter);
        }

        private static bool TryShelterEntrance(WorldState world, GridPos position, HashSet<GridPos> reachable, out GridPos entrance)
        {
            int[] dx = { 0, -1, 0, 1 };
            int[] dy = { -1, 0, 1, 0 };
            for (int direction = 0; direction < 4; direction++)
            {
                GridPos candidate = position.Offset(dx[direction], dy[direction]);
                if (!reachable.Contains(candidate))
                {
                    continue;
                }

                bool shared = false;
                foreach (ShelterState shelter in world.Survival.Shelters)
                {
                    if (shelter.Entrance == candidate)
                    {
                        shared = true;
                        break;
                    }
                }
                if (!shared)
                {
                    entrance = candidate;
                    return true;
                }
            }

            entrance = GridPos.Invalid;
            return false;
        }

        private static ShelterState ChooseShelter(WorldState world, GridPos cell)
        {
            ShelterState best = null;
            int bestDistance = int.MaxValue;
            foreach (ShelterState shelter in world.Survival.Shelters)
            {
                int residents = 0;
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    if (npc.Survival.Life != LifeState.Dead && npc.Survival.ShelterId == shelter.Id)
                    {
                        residents++;
                    }
                }

                if (residents >= shelter.Capacity || world.Map.GetTile(shelter.Position) != TileType.House ||
                    !world.Map.IsReachable(cell, shelter.Entrance))
                {
                    continue;
                }

                int distance = cell.ManhattanDistance(shelter.Entrance);
                if (distance < bestDistance || distance == bestDistance && (best == null || shelter.Id < best.Id))
                {
                    best = shelter;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static void PopulateResources(WorldState world, List<GridPos> reachable, ulong rootSeed)
        {
            var rng = new DeterministicRng(DeterministicRng.Mix(rootSeed ^ 0xB5432D714921UL));
            var candidates = new List<GridPos>();
            for (int i = 0; i < reachable.Count; i++)
            {
                GridPos cell = reachable[i];
                if (world.Map.GetTile(cell) == TileType.Farmland)
                {
                    world.Resources.AddPlot(new FarmPlotState(world.Resources.FarmPlots.Count + 1, cell));
                }
                else if (cell != world.Center && cell.ManhattanDistance(world.Center) <= 18 &&
                    cell.ManhattanDistance(world.Center) >= 3)
                {
                    candidates.Add(cell);
                }
            }
            Shuffle(candidates, ref rng);
            int nodeCount = Math.Min(candidates.Count, Math.Max(16, world.Config.NpcCount));
            for (int i = 0; i < nodeCount; i++)
            {
                // Renewable berry patches and village wells are all on reachable work cells.
                ItemKind item = i % 4 == 0 ? ItemKind.RawWater : ItemKind.Food;
                world.Resources.AddNode(new ResourceNodeState(i + 1, candidates[i], item,
                    item == ItemKind.RawWater ? 30 : 8, item == ItemKind.RawWater ? 12 : 180));
            }
            ItemKind[] raw = { ItemKind.Wood, ItemKind.Stone, ItemKind.Fiber, ItemKind.Herb };
            int rawEnd = Math.Min(candidates.Count, nodeCount + 16);
            int rawCursor = nodeCount;
            for (int i = nodeCount; i < rawEnd; i++)
            {
                ItemKind item = raw[(i - nodeCount) % raw.Length];
                // Wood nodes must sit on forested ground: the patch's finite tree
                // biomass is what regenerates them, so a clear-cut node would die
                // permanently while the rest of the map still held timber.
                if (item == ItemKind.Wood)
                {
                    for (int j = rawCursor; j < candidates.Count; j++)
                    {
                        if (world.Map.GetTile(candidates[j]) != TileType.Forest) { continue; }
                        GridPos swap = candidates[rawCursor];
                        candidates[rawCursor] = candidates[j];
                        candidates[j] = swap;
                        break;
                    }
                }
                world.Resources.AddNode(new ResourceNodeState(i + 1, candidates[rawCursor], item, 30, 60));
                rawCursor++;
            }
            world.Resources.Stockpile.TryAdd(ItemKind.KaleSeed, 40);
            world.Resources.Stockpile.TryAdd(ItemKind.LambsLettuceSeed, 40);
            world.Resources.Stockpile.TryAdd(ItemKind.Medicine, 4);
            world.Resources.Stockpile.TryAdd(ItemKind.Food, 20);
            world.Resources.Stockpile.TryAdd(ItemKind.Water, 20);
            world.Resources.Stockpile.TryAdd(ItemKind.SpinachSeed, 40);
            for (int i = 0; i < world.Entities.Npcs.Count; i++)
            {
                world.Entities.Npcs[i].Inventory.TryAdd(ItemKind.Food, 1);
                world.Entities.Npcs[i].Inventory.TryAdd(ItemKind.Water, 1);
            }
        }

        private static bool TryChooseFreeCell(WorldState world, out GridPos cell)
        {
            List<GridPos> reachable = world.Map.CollectReachable(world.Center);
            var free = new List<GridPos>();
            for (int i = 0; i < reachable.Count; i++)
            {
                if (!world.Entities.IsCellOccupied(reachable[i]))
                {
                    free.Add(reachable[i]);
                }
            }

            if (free.Count == 0)
            {
                cell = GridPos.Invalid;
                return false;
            }

            cell = free[world.Rng.NextInt(0, free.Count)];
            return true;
        }

        private static WorldMap GenerateMap(WorldConfigData config)
        {
            var map = new WorldMap(config.Width, config.Height);
            var rng = new DeterministicRng(DeterministicRng.Mix((ulong)(uint)config.Seed));
            int centerX = config.Width / 2;
            int centerY = config.Height / 2;

            for (int x = 0; x < config.Width; x++)
            {
                map.SetTile(new GridPos(x, 0), TileType.Water);
                map.SetTile(new GridPos(x, config.Height - 1), TileType.Water);
            }

            for (int y = 0; y < config.Height; y++)
            {
                map.SetTile(new GridPos(0, y), TileType.Water);
                map.SetTile(new GridPos(config.Width - 1, y), TileType.Water);
            }

            // A darker canopy frames the playable meadow without closing the map.
            for (int x = 2; x < config.Width - 2; x++)
            {
                if (rng.Chance(42))
                {
                    map.SetTile(new GridPos(x, 1), TileType.Forest);
                }

                if (rng.Chance(42))
                {
                    map.SetTile(new GridPos(x, config.Height - 2), TileType.Forest);
                }
            }

            for (int y = 2; y < config.Height - 2; y++)
            {
                if (rng.Chance(42))
                {
                    map.SetTile(new GridPos(1, y), TileType.Forest);
                }

                if (rng.Chance(42))
                {
                    map.SetTile(new GridPos(config.Width - 2, y), TileType.Forest);
                }
            }

            DrawDisc(map, new GridPos(centerX, centerY), 8, TileType.Dirt, true);

            int pondX = centerX + 11;
            int pondY = centerY - 8;
            DrawEllipse(map, new GridPos(pondX, pondY), 6, 4, TileType.Water);
            DrawEllipse(map, new GridPos(pondX, pondY), 7, 5, TileType.Dirt, false);

            for (int cluster = 0; cluster < 24; cluster++)
            {
                int cx = 3 + rng.NextInt(0, config.Width - 6);
                int cy = 3 + rng.NextInt(0, config.Height - 6);
                var clusterCenter = new GridPos(cx, cy);
                if (clusterCenter.ManhattanDistance(new GridPos(centerX, centerY)) < 10)
                {
                    continue;
                }

                int radius = 2 + rng.NextInt(0, 4);
                DrawForest(map, clusterCenter, radius);
            }

            for (int rock = 0; rock < 64; rock++)
            {
                var pos = new GridPos(
                    2 + rng.NextInt(0, config.Width - 4),
                    2 + rng.NextInt(0, config.Height - 4));
                if (pos.ManhattanDistance(new GridPos(centerX, centerY)) < 4)
                {
                    continue;
                }

                if (map.GetTile(pos) == TileType.Grass || map.GetTile(pos) == TileType.Dirt)
                {
                    map.SetTile(pos, TileType.Rock);
                }
            }

            for (int x = 3; x < config.Width - 2; x++)
            {
                for (int offset = -1; offset <= 1; offset++)
                {
                    var pos = new GridPos(x, centerY + offset);
                    if (map.IsInside(pos))
                    {
                        map.SetTile(pos, TileType.Path);
                    }
                }
            }

            for (int y = 3; y < config.Height - 2; y++)
            {
                for (int offset = -1; offset <= 1; offset++)
                {
                    var pos = new GridPos(centerX + offset, y);
                    if (map.IsInside(pos))
                    {
                        map.SetTile(pos, TileType.Path);
                    }
                }
            }

            DrawDisc(map, new GridPos(centerX, centerY), 2, TileType.Path, true);

            // Path branch toward the pond and the southern farm plots.
            for (int x = centerX + 3; x <= pondX - 3; x++)
            {
                map.SetTile(new GridPos(x, centerY - 4), TileType.Path);
            }

            for (int y = pondY + 3; y <= centerY - 4; y++)
            {
                map.SetTile(new GridPos(pondX - 3, y), TileType.Path);
            }

            for (int y = centerY + 4; y <= centerY + 12; y++)
            {
                map.SetTile(new GridPos(centerX - 4, y), TileType.Path);
            }

            for (int x = centerX - 12; x <= centerX - 4; x++)
            {
                map.SetTile(new GridPos(x, centerY + 12), TileType.Path);
            }

            PlaceHouse(map, new GridPos(centerX - 6, centerY - 5));
            PlaceHouse(map, new GridPos(centerX + 5, centerY - 6));
            PlaceHouse(map, new GridPos(centerX - 7, centerY + 4));
            PlaceHouse(map, new GridPos(centerX + 6, centerY + 5));
            PlaceHouse(map, new GridPos(centerX - 3, centerY - 9));

            DrawFarmPlot(map, new GridPos(centerX - 13, centerY - 7), 5, 3);
            DrawFarmPlot(map, new GridPos(centerX + 8, centerY + 7), 5, 3);
            DrawFarmPlot(map, new GridPos(centerX - 13, centerY + 8), 5, 3);
            return map;
        }


        private static void PlaceHouse(WorldMap map, GridPos pos)
        {
            if (!map.IsInside(pos))
            {
                return;
            }

            TileType tile = map.GetTile(pos);
            if (tile == TileType.Grass || tile == TileType.Dirt || tile == TileType.Forest)
            {
                map.SetTile(pos, TileType.House);
            }
        }

        private static void DrawFarmPlot(WorldMap map, GridPos origin, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pos = new GridPos(origin.X + x, origin.Y + y);
                    if (!map.IsInside(pos))
                    {
                        continue;
                    }

                    TileType tile = map.GetTile(pos);
                    if (tile == TileType.Grass || tile == TileType.Dirt || tile == TileType.Forest)
                    {
                        map.SetTile(pos, TileType.Farmland);
                    }
                }
            }
        }

        private static void DrawForest(WorldMap map, GridPos center, int radius)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius)
                    {
                        continue;
                    }

                    var pos = new GridPos(center.X + x, center.Y + y);
                    if (!map.IsInside(pos) || pos.X < 2 || pos.Y < 2 ||
                        pos.X >= map.Width - 2 || pos.Y >= map.Height - 2)
                    {
                        continue;
                    }

                    if (map.GetTile(pos) == TileType.Grass || map.GetTile(pos) == TileType.Dirt)
                    {
                        map.SetTile(pos, TileType.Forest);
                    }
                }
            }
        }

        private static void DrawDisc(WorldMap map, GridPos center, int radius, TileType tile, bool overwrite)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius)
                    {
                        continue;
                    }

                    var pos = new GridPos(center.X + x, center.Y + y);
                    if (!map.IsInside(pos))
                    {
                        continue;
                    }

                    if (overwrite || map.GetTile(pos) == TileType.Grass)
                    {
                        map.SetTile(pos, tile);
                    }
                }
            }
        }

        private static void DrawEllipse(
            WorldMap map,
            GridPos center,
            int radiusX,
            int radiusY,
            TileType tile,
            bool overwrite = true)
        {
            for (int y = -radiusY; y <= radiusY; y++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    float normalized = (x * x) / (float)(radiusX * radiusX) +
                        (y * y) / (float)(radiusY * radiusY);
                    if (normalized > 1f)
                    {
                        continue;
                    }

                    var pos = new GridPos(center.X + x, center.Y + y);
                    if (!map.IsInside(pos))
                    {
                        continue;
                    }

                    if (overwrite || map.GetTile(pos) == TileType.Grass)
                    {
                        map.SetTile(pos, tile);
                    }
                }
            }
        }

        private static void Shuffle(List<GridPos> items, ref DeterministicRng rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                GridPos tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
