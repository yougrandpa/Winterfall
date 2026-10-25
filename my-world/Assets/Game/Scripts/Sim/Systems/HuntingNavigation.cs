using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingNavigation
    {
        private static readonly GridPathfinder Pathfinder = new GridPathfinder();

        internal static bool Move(WorldState world, NpcState npc, GridPos destination, int arrivalRange = 0)
        {
            string label = npc.Action.Label;
            long blockedUntil = npc.Action.Type == NpcActionType.Idle && npc.Action.Target == destination ?
                npc.Action.EndTick : world.Tick + HuntingRules.PathWaitTicks;
            if (npc.Position.ManhattanDistance(destination) <= arrivalRange)
            {
                npc.Action.SetTimed(NpcActionType.Work, label, destination, world.Tick + 1);
                return true;
            }

            var occupied = new HashSet<GridPos>();
            foreach (NpcState other in world.Entities.Npcs)
            {
                if (other.Id != npc.Id && other.Survival.Life != LifeState.Dead) { occupied.Add(other.Position); }
            }
            bool CanEnter(GridPos cell) => !occupied.Contains(cell) && !HuntingSafety.ChargeLane(world, npc, cell);

            // Keep a complete detour, rather than stepping aside and walking back to the blocked route.
            NpcActionState action = npc.Action;
            GridPos next = action.PeekNextStep();
            if (action.Target != destination || !next.IsValid || npc.Position.ManhattanDistance(next) != 1 ||
                !world.Map.IsWalkable(next) || !CanEnter(next) ||
                action.Path[action.Path.Count - 1].ManhattanDistance(destination) > arrivalRange ||
                !CanEnter(action.Path[action.Path.Count - 1]))
            {
                PathResult best = null;
                int bestIndex = int.MaxValue;
                var goals = new List<GridPos>();
                for (int y = -arrivalRange; y <= arrivalRange; y++)
                for (int x = -arrivalRange; x <= arrivalRange; x++)
                {
                    GridPos cell = destination.Offset(x, y);
                    if (Math.Abs(x) + Math.Abs(y) > arrivalRange || !world.Map.IsWalkable(cell)) { continue; }
                    goals.Add(cell);
                    if (!CanEnter(cell)) { continue; }
                    PathResult path = Pathfinder.FindPath(world.Map, npc.Position, cell, 512, CanEnter);
                    int index = cell.Y * world.Map.Width + cell.X;
                    if (path.Found && (best == null || path.Cost < best.Cost || path.Cost == best.Cost && index < bestIndex))
                    {
                        best = path;
                        bestIndex = index;
                    }
                }

                if (best == null)
                {
                    // Dynamic crowding is a wait; only an unreachable terrain route aborts the hunt.
                    npc.Action.SetTimed(NpcActionType.Idle, "Hunt: wait for path", destination, blockedUntil);
                    foreach (GridPos goal in goals)
                    {
                        if (Pathfinder.FindPath(world.Map, npc.Position, goal, 512).Found)
                        {
                            return world.Tick < blockedUntil;
                        }
                    }
                    return false;
                }

                action.SetMove(label, destination, best.Path);
                next = action.PeekNextStep();
            }

            npc.Position = next;
            action.AdvanceStep();
            npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - 4);
            return true;
        }
    }
}
