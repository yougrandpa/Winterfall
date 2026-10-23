using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Navigation
{
    public sealed class PathResult
    {
        public PathResult(bool found, List<GridPos> path, int expandedNodes, int cost)
        {
            Found = found;
            Path = path ?? new List<GridPos>();
            ExpandedNodes = expandedNodes;
            Cost = cost;
        }

        public bool Found { get; }
        public List<GridPos> Path { get; }
        public int ExpandedNodes { get; }
        public int Cost { get; }

        public static PathResult NotFound(int expandedNodes)
        {
            return new PathResult(false, new List<GridPos>(), expandedNodes, 0);
        }
    }

    public sealed class GridPathfinder
    {
        private static readonly int[] DirectionX = { 0, 1, 0, -1 };
        private static readonly int[] DirectionY = { 1, 0, -1, 0 };

        public PathResult FindPath(WorldMap map, GridPos start, GridPos goal, int maxExpandedNodes = 0)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (!map.IsWalkable(start) || !map.IsWalkable(goal))
            {
                return PathResult.NotFound(0);
            }

            if (start == goal)
            {
                return new PathResult(true, new List<GridPos>(), 0, 0);
            }

            int width = map.Width;
            int height = map.Height;
            int cellCount = width * height;
            var gScore = new int[cellCount];
            var cameFrom = new int[cellCount];
            var closed = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                gScore[i] = int.MaxValue;
                cameFrom[i] = -1;
            }

            int startIndex = start.Y * width + start.X;
            int goalIndex = goal.Y * width + goal.X;
            gScore[startIndex] = 0;

            var open = new MinHeap();
            open.Push(startIndex, Heuristic(start, goal));

            int expanded = 0;
            while (open.Count > 0)
            {
                HeapNode current = open.Pop();
                if (closed[current.Index])
                {
                    continue;
                }

                closed[current.Index] = true;
                expanded++;
                if (maxExpandedNodes > 0 && expanded > maxExpandedNodes)
                {
                    return PathResult.NotFound(expanded);
                }

                if (current.Index == goalIndex)
                {
                    return BuildResult(cameFrom, width, startIndex, goalIndex, expanded, gScore[goalIndex]);
                }

                int cx = current.Index % width;
                int cy = current.Index / width;

                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = cx + DirectionX[dir];
                    int ny = cy + DirectionY[dir];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    var next = new GridPos(nx, ny);
                    int moveCost = map.GetMoveCost(next);
                    if (moveCost <= 0)
                    {
                        continue;
                    }

                    int neighborIndex = ny * width + nx;
                    if (closed[neighborIndex])
                    {
                        continue;
                    }

                    int tentative = gScore[current.Index] + moveCost;
                    if (tentative >= gScore[neighborIndex])
                    {
                        continue;
                    }

                    gScore[neighborIndex] = tentative;
                    cameFrom[neighborIndex] = current.Index;
                    int f = tentative + Heuristic(next, goal);
                    open.Push(neighborIndex, f);
                }
            }

            return PathResult.NotFound(expanded);
        }

        private static int Heuristic(GridPos a, GridPos b)
        {
            return a.ManhattanDistance(b);
        }

        private static PathResult BuildResult(
            int[] cameFrom,
            int width,
            int startIndex,
            int goalIndex,
            int expanded,
            int cost)
        {
            var path = new List<GridPos>();
            int current = goalIndex;
            while (current != startIndex && current >= 0)
            {
                path.Add(new GridPos(current % width, current / width));
                current = cameFrom[current];
            }

            path.Reverse();
            return new PathResult(true, path, expanded, cost);
        }

        private struct HeapNode
        {
            public HeapNode(int index, int fScore)
            {
                Index = index;
                FScore = fScore;
            }

            public int Index;
            public int FScore;
        }

        private sealed class MinHeap
        {
            private readonly List<HeapNode> _items = new List<HeapNode>(64);

            public int Count => _items.Count;

            public void Push(int index, int fScore)
            {
                _items.Add(new HeapNode(index, fScore));
                int child = _items.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (!Less(_items[child], _items[parent]))
                    {
                        break;
                    }

                    HeapNode tmp = _items[parent];
                    _items[parent] = _items[child];
                    _items[child] = tmp;
                    child = parent;
                }
            }

            public HeapNode Pop()
            {
                HeapNode root = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);

                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    int right = left + 1;
                    int smallest = parent;

                    if (left < _items.Count && Less(_items[left], _items[smallest]))
                    {
                        smallest = left;
                    }

                    if (right < _items.Count && Less(_items[right], _items[smallest]))
                    {
                        smallest = right;
                    }

                    if (smallest == parent)
                    {
                        break;
                    }

                    HeapNode tmp = _items[parent];
                    _items[parent] = _items[smallest];
                    _items[smallest] = tmp;
                    parent = smallest;
                }

                return root;
            }

            private static bool Less(HeapNode a, HeapNode b)
            {
                if (a.FScore != b.FScore)
                {
                    return a.FScore < b.FScore;
                }

                return a.Index < b.Index;
            }
        }
    }
}
