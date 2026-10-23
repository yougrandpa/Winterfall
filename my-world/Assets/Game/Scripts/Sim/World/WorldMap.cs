using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum TileType : byte
    {
        Grass = 0,
        Dirt = 1,
        Path = 2,
        Forest = 3,
        Water = 4,
        Rock = 5,
        House = 6,
        Farmland = 7
    }

    public sealed class WorldMap
    {
        private static readonly int[] NeighborX = { 0, 1, 0, -1 };
        private static readonly int[] NeighborY = { 1, 0, -1, 0 };

        private readonly TileType[] _tiles;

        public WorldMap(int width, int height, TileType fill = TileType.Grass)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
            _tiles = new TileType[width * height];
            if (fill != TileType.Grass)
            {
                for (int i = 0; i < _tiles.Length; i++)
                {
                    _tiles[i] = fill;
                }
            }
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount => _tiles.Length;

        public bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool IsInside(GridPos pos) => IsInside(pos.X, pos.Y);

        public TileType GetTile(int x, int y)
        {
            if (!IsInside(x, y))
            {
                return TileType.Water;
            }

            return _tiles[y * Width + x];
        }

        public TileType GetTile(GridPos pos) => GetTile(pos.X, pos.Y);

        public void SetTile(GridPos pos, TileType type)
        {
            if (!IsInside(pos))
            {
                throw new ArgumentOutOfRangeException(nameof(pos), "Tile position is outside the map.");
            }

            _tiles[pos.Y * Width + pos.X] = type;
        }

        public bool IsWalkable(GridPos pos) => GetMoveCost(pos) > 0;

        public int GetMoveCost(GridPos pos)
        {
            switch (GetTile(pos))
            {
                case TileType.Grass:
                case TileType.Dirt:
                case TileType.Path:
                case TileType.Farmland:
                    return 1;
                case TileType.Forest:
                    return 2;
                default:
                    return 0;
            }
        }

        public TileType[] CopyTiles()
        {
            var copy = new TileType[_tiles.Length];
            Array.Copy(_tiles, copy, _tiles.Length);
            return copy;
        }

        public bool IsReachable(GridPos start, GridPos goal)
        {
            if (!IsInside(start) || !IsInside(goal) || !IsWalkable(start) || !IsWalkable(goal))
            {
                return false;
            }

            if (start == goal)
            {
                return true;
            }

            var visited = new bool[_tiles.Length];
            var queue = new Queue<GridPos>();
            queue.Enqueue(start);
            visited[start.Y * Width + start.X] = true;

            while (queue.Count > 0)
            {
                GridPos current = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    var next = new GridPos(current.X + NeighborX[i], current.Y + NeighborY[i]);
                    if (!IsInside(next) || !IsWalkable(next))
                    {
                        continue;
                    }

                    int index = next.Y * Width + next.X;
                    if (visited[index])
                    {
                        continue;
                    }

                    if (next == goal)
                    {
                        return true;
                    }

                    visited[index] = true;
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        public List<GridPos> CollectReachable(GridPos start)
        {
            var results = new List<GridPos>();
            if (!IsInside(start) || !IsWalkable(start))
            {
                return results;
            }

            var visited = new bool[_tiles.Length];
            var queue = new Queue<GridPos>();
            queue.Enqueue(start);
            visited[start.Y * Width + start.X] = true;

            while (queue.Count > 0)
            {
                GridPos current = queue.Dequeue();
                results.Add(current);

                for (int i = 0; i < 4; i++)
                {
                    var next = new GridPos(current.X + NeighborX[i], current.Y + NeighborY[i]);
                    if (!IsInside(next) || !IsWalkable(next))
                    {
                        continue;
                    }

                    int index = next.Y * Width + next.X;
                    if (visited[index])
                    {
                        continue;
                    }

                    visited[index] = true;
                    queue.Enqueue(next);
                }
            }

            return results;
        }
    }
}
