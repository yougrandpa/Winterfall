using System;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingSystem
    {
        internal static ItemKind Weapon(NpcState npc) => npc.Survival.Durability(ItemKind.Bow) > 0 ? ItemKind.Bow : ItemKind.Spear;
        internal static bool CanStrike(WorldState world, NpcState npc, AnimalState animal)
        {
            int range = Weapon(npc) == ItemKind.Bow ? 4 : 1;
            return npc.Position.ManhattanDistance(animal.Position) <= range && Visible(world, npc.Position, animal.Position);
        }
        internal static bool Visible(WorldState world, GridPos from, GridPos to)
        {
            int x = from.X, y = from.Y, dx = Math.Abs(to.X - x), dy = Math.Abs(to.Y - y);
            int sx = x < to.X ? 1 : -1, sy = y < to.Y ? 1 : -1, error = dx - dy;
            while (x != to.X || y != to.Y)
            {
                int twice = error * 2;
                if (twice > -dy) { error -= dy; x += sx; }
                if (twice < dx) { error += dx; y += sy; }
                var cell = new GridPos(x, y);
                if (!world.Map.IsWalkable(cell) || (cell != to && world.Map.GetTile(cell) == TileType.Forest)) { return false; }
            }
            return true;
        }
    }
}
