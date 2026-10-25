using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingEquipment
    {
        internal static bool CanPrepare(WorldState world, NpcState npc, ItemKind weapon)
        {
            bool available = npc.Survival.Durability(weapon) > 0 || npc.Inventory.Count(weapon) > 0 ||
                world.Resources.AvailableStock(weapon) > 0;
            return available && (weapon != ItemKind.Bow || npc.Inventory.Count(ItemKind.Arrow) > 0 ||
                world.Resources.AvailableStock(ItemKind.Arrow) > 0);
        }

        internal static int Rank(WorldState world, NpcState npc, AnimalSpecies species)
        {
            if (HuntingRules.NeedsSpear(species) && npc.Survival.Durability(ItemKind.Spear) > 0) { return 0; }
            if (npc.Survival.Durability(ItemKind.Bow) > 0 && npc.Inventory.Count(ItemKind.Arrow) > 0) { return 1; }
            if (npc.Survival.Durability(ItemKind.Spear) > 0) { return 2; }
            if (npc.HasHuntingTool) { return 3; }
            return 4;
        }

        // At most three actors and two loadouts: evaluate real shared supply before mutating any plan.
        internal static Dictionary<int, HuntRole> Assign(WorldState world, List<NpcState> members, AnimalSpecies species)
        {
            Dictionary<int, HuntRole> best = null;
            int bestScore = int.MaxValue;
            for (int mask = 0; mask < (1 << members.Count); mask++)
            {
                int bows = 0, spears = 0, arrows = 0, score = 0;
                bool spearRole = false;
                var roles = new Dictionary<int, HuntRole>();
                for (int i = 0; i < members.Count; i++)
                {
                    NpcState npc = members[i];
                    ItemKind weapon = (mask & (1 << i)) != 0 ? ItemKind.Spear : ItemKind.Bow;
                    roles[npc.Id.Value] = weapon == ItemKind.Spear ? HuntRole.Spear : HuntRole.Archer;
                    spearRole |= weapon == ItemKind.Spear;
                    if (npc.Survival.Durability(weapon) == 0)
                    {
                        score += npc.Inventory.Count(weapon) > 0 ? 4 : 8;
                        if (npc.Inventory.Count(weapon) == 0)
                        {
                            if (weapon == ItemKind.Bow) { bows++; } else { spears++; }
                        }
                    }
                    if (weapon == ItemKind.Bow && npc.Inventory.Count(ItemKind.Arrow) == 0) { arrows++; score += 2; }
                    if (weapon == ItemKind.Spear) { score++; }
                }
                if (HuntingRules.NeedsSpear(species) && !spearRole || bows > world.Resources.AvailableStock(ItemKind.Bow) ||
                    spears > world.Resources.AvailableStock(ItemKind.Spear) || arrows > world.Resources.AvailableStock(ItemKind.Arrow))
                {
                    continue;
                }
                if (score < bestScore) { best = roles; bestScore = score; }
            }
            return best;
        }

        internal static bool Ready(NpcState npc, HuntMemberState member)
        {
            return member.Role == HuntRole.Spear ? npc.Survival.Durability(ItemKind.Spear) > 0 :
                npc.Survival.Durability(ItemKind.Bow) > 0 && npc.Inventory.Count(ItemKind.Arrow) > 0;
        }

        internal static bool Prepare(WorldState world, NpcState npc, HuntMemberState member, HuntTaskState task,
            out bool unavailable)
        {
            unavailable = false;
            if (Ready(npc, member)) { return true; }
            ItemKind weapon = member.Role == HuntRole.Spear ? ItemKind.Spear : ItemKind.Bow;
            ItemKind item = npc.Survival.Durability(weapon) == 0 ? weapon : ItemKind.Arrow;
            if (item != ItemKind.Arrow && npc.Inventory.TryRemove(item, 1))
            {
                npc.Survival.SetTool(item, SurvivalRules.ToolDurability);
                return Ready(npc, member);
            }
            if (world.Resources.AvailableStock(item) == 0) { unavailable = true; return false; }
            if (npc.Position.ManhattanDistance(world.Resources.StockpilePosition) > 1) { return false; }
            if (item != ItemKind.Arrow)
            {
                world.Resources.Stockpile.TryRemove(item, 1);
                npc.Survival.SetTool(item, SurvivalRules.ToolDurability);
            }
            else
            {
                int neededByOthers = 0;
                foreach (HuntMemberState other in task.Members)
                {
                    if (other.NpcId != npc.Id && other.Role != HuntRole.Spear &&
                        world.Entities.TryGetNpc(other.NpcId, out NpcState partner) && partner.Inventory.Count(ItemKind.Arrow) == 0)
                    {
                        neededByOthers++;
                    }
                }
                int amount = Math.Min(4, Math.Min(npc.Inventory.Capacity - npc.Inventory.TotalCount,
                    world.Resources.AvailableStock(ItemKind.Arrow) - neededByOthers));
                if (amount <= 0) { unavailable = true; return false; }
                world.Resources.Stockpile.TryRemove(ItemKind.Arrow, amount);
                npc.Inventory.TryAdd(ItemKind.Arrow, amount);
            }
            return Ready(npc, member);
        }
    }
}
