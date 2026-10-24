using System;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingSystem
    {
        internal static ItemKind Weapon(NpcState npc) => npc.Survival.Durability(ItemKind.Bow) > 0 &&
            npc.Inventory.Count(ItemKind.Arrow) > 0 ? ItemKind.Bow : ItemKind.Spear;
        internal static ItemKind Weapon(WorldState world, NpcState npc) =>
            world.Hunting.ForNpc(npc.Id)?.Member(npc.Id)?.Role == HuntRole.Spear && npc.Survival.Durability(ItemKind.Spear) > 0 ?
                ItemKind.Spear : Weapon(npc);

        internal static bool ClearShot(WorldState world, EntityId shooter, GridPos from, GridPos to)
        {
            if (!Visible(world, from, to)) { return false; }
            long dx = to.X - from.X, dy = to.Y - from.Y, length = dx * dx + dy * dy;
            foreach (NpcState other in world.Entities.Npcs)
            {
                if (other.Id == shooter || other.Survival.Life == LifeState.Dead) { continue; }
                long x = other.Position.X - from.X, y = other.Position.Y - from.Y;
                long dot = x * dx + y * dy, cross = x * dy - y * dx;
                if (length == 0 ? other.Position == to : dot >= 0 && dot <= length && cross * cross * 25 <= length * 4) { return false; }
            }
            return true;
        }

        internal static bool Perceived(WorldState world, NpcState npc, AnimalState animal)
        {
            return world.Ecology.CanHunt(animal) && Perceived(world, npc, animal.Position);
        }

        internal static bool Perceived(WorldState world, NpcState npc, GridPos position)
        {
            if (!world.Map.IsInside(position)) { return false; }
            int range = (world.Environment.IsDaytime ? 8 : 4) + npc.HuntingSkill / 25;
            if (world.Environment.Weather == WeatherKind.Blizzard) { range -= 3; }
            if (world.Map.GetTile(position) == TileType.Forest) { range -= 2; }
            return npc.Position.ManhattanDistance(position) <= Math.Max(2, range) &&
                Visible(world, npc.Position, position);
        }

        internal static bool CanStrike(WorldState world, NpcState npc, AnimalState animal)
        {
            if (npc == null || animal == null || animal.Health <= 0) { return false; }
            ItemKind weapon = Weapon(world, npc);
            if (npc.Survival.Durability(weapon) <= 0 || (weapon == ItemKind.Bow && npc.Inventory.Count(ItemKind.Arrow) <= 0)) { return false; }
            int range = weapon == ItemKind.Bow ? 4 : 1;
            return npc.Position.ManhattanDistance(animal.Position) <= range && Visible(world, npc.Position, animal.Position) &&
                (weapon != ItemKind.Bow || ClearShot(world, npc.Id, npc.Position, animal.Position));
        }
        internal static bool Visible(WorldState world, GridPos from, GridPos to)
        {
            if (!world.Map.IsWalkable(from) || !world.Map.IsWalkable(to)) { return false; }
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
