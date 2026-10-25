using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingPreparation
    {
        internal static bool CoatRequired(WorldState world) => world.Environment.Temperature - world.Environment.Wind * 12 < 500;

        internal static bool Budget(WorldState world, List<NpcState> members, Dictionary<int, HuntRole> roles,
            out List<ResourceReservation> reservations, out string reason)
        {
            reservations = new List<ResourceReservation>();
            var totals = new int[(int)ItemKind.Count];
            foreach (NpcState npc in members)
            {
                ItemKind weapon = roles[npc.Id.Value] == HuntRole.Spear ? ItemKind.Spear : ItemKind.Bow;
                Add(world, npc, weapon, npc.Survival.Durability(weapon) > 0 ? 0 : 1, reservations, totals);
                if (weapon == ItemKind.Bow) { Add(world, npc, ItemKind.Arrow, HuntingRules.ArrowsPerArcher, reservations, totals); }
                Add(world, npc, ItemKind.Food, 1 + Math.Max(0, (7000 - npc.Needs.Food + 4499) / 4500), reservations, totals);
                Add(world, npc, ItemKind.Water, 1 + Math.Max(0, (7000 - npc.Needs.Water + 5499) / 5500), reservations, totals);
                if (CoatRequired(world) && npc.Survival.CoatDurability == 0) { Add(world, npc, ItemKind.WinterCoat, 1, reservations, totals); }
            }
            for (int i = 0; i < totals.Length; i++)
            {
                if (totals[i] <= world.Resources.AvailableStock((ItemKind)i)) { continue; }
                string item = i == (int)ItemKind.Food ? "食物" : i == (int)ItemKind.Water ? "饮水" : i == (int)ItemKind.WinterCoat ? "冬衣" : "武器或箭矢";
                reason = "出发补给不足：" + item + "缺 " + (totals[i] - world.Resources.AvailableStock((ItemKind)i)) + "，等待村庄生产";
                return false;
            }
            reason = "";
            return true;
        }

        private static void Add(WorldState world, NpcState npc, ItemKind item, int desired,
            List<ResourceReservation> reservations, int[] totals)
        {
            int missing = Math.Max(0, desired - npc.Inventory.Count(item));
            if (missing == 0) { return; }
            reservations.Add(new ResourceReservation(npc.Id, ReservationKind.HuntSupply, 0, item, missing, world.Resources.StockpilePosition));
            totals[(int)item] += missing;
        }

        internal static bool Ready(WorldState world, NpcState npc, HuntMemberState member) =>
            HuntingEquipment.Ready(npc, member) && (member.Role == HuntRole.Spear || npc.Inventory.Count(ItemKind.Arrow) >= HuntingRules.ArrowsPerArcher) &&
            npc.Needs.Food >= HuntingRules.DepartureNeed && npc.Needs.Water >= HuntingRules.DepartureNeed && npc.Needs.Energy >= HuntingRules.DepartureNeed &&
            npc.Inventory.Food > 0 && npc.Inventory.Water > 0 && (!CoatRequired(world) || npc.Survival.CoatDurability > 0) &&
            npc.Survival.BodyTemperature >= 3600 && npc.Survival.Wetness < 5000;

        internal static bool Prepare(WorldState world, NpcState npc, HuntMemberState member, out bool unavailable)
        {
            unavailable = false;
            npc.Survival.HuntMotion = HuntMotion.Walk;
            if (Ready(world, npc, member) && (npc.Needs.Energy >= 7000 || npc.Position.ManhattanDistance(world.Resources.StockpilePosition) <= 1)) { return true; }
            ItemKind requiredWeapon = member.Role == HuntRole.Spear ? ItemKind.Spear : ItemKind.Bow;
            int Reserved(ItemKind item)
            {
                int total = npc.Inventory.Count(item);
                foreach (ResourceReservation r in world.Resources.Reservations) { if (r.Owner == npc.Id && r.Kind == ReservationKind.HuntSupply && r.Item == item) { total += r.Quantity; } }
                return total;
            }
            if (npc.Survival.Durability(requiredWeapon) == 0 && Reserved(requiredWeapon) == 0 || requiredWeapon == ItemKind.Bow && Reserved(ItemKind.Arrow) < HuntingRules.ArrowsPerArcher)
            { unavailable = true; return false; }
            bool supplied = HuntingEquipment.Ready(npc, member) && npc.Inventory.Food > 0 && npc.Inventory.Water > 0 &&
                (!CoatRequired(world) || npc.Survival.CoatDurability > 0);
            if (supplied && (npc.Needs.Energy < 7000 || npc.Survival.BodyTemperature < 3600 || npc.Survival.Wetness >= 5000))
            {
                npc.Action.Label = "Hunt: rest before departure";
                if (npc.Position != npc.Home) { unavailable = !HuntingNavigation.Move(world, npc, npc.Home); }
                else { npc.Action.SetTimed(NpcActionType.Sleep, "Hunt: rest before departure", npc.Home, world.Tick + 1); }
                return false;
            }
            if (npc.Position.ManhattanDistance(world.Resources.StockpilePosition) > 1)
            {
                npc.Action.Label = "Hunt: prepare supplies";
                unavailable = !HuntingNavigation.Move(world, npc, world.Resources.StockpilePosition, 1);
                return false;
            }
            ItemKind weapon = member.Role == HuntRole.Spear ? ItemKind.Spear : ItemKind.Bow;
            // Keep mission items; unload unrelated work cargo before claiming reserved supply.
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                ItemKind item = (ItemKind)i;
                int keep = item == ItemKind.Food ? 1 + Math.Max(0, (7000 - npc.Needs.Food + 4499) / 4500) :
                    item == ItemKind.Water ? 1 + Math.Max(0, (7000 - npc.Needs.Water + 5499) / 5500) : item == ItemKind.Arrow && weapon == ItemKind.Bow ? 4 :
                    item == weapon && npc.Survival.Durability(weapon) == 0 || item == ItemKind.WinterCoat && npc.Survival.CoatDurability == 0 ? 1 : 0;
                int amount = Math.Min(Math.Max(0, npc.Inventory.Count(item) - keep), world.Resources.Stockpile.Capacity - world.Resources.Stockpile.TotalCount);
                if (amount > 0) { npc.Inventory.TryRemove(item, amount); world.Resources.Stockpile.TryAdd(item, amount); }
            }
            if (npc.Survival.Durability(weapon) == 0)
            {
                if (!Equip(world, npc, weapon)) { unavailable = true; return false; }
                npc.Survival.SetTool(weapon, SurvivalRules.ToolDurability);
            }
            if (CoatRequired(world) && npc.Survival.CoatDurability == 0)
            {
                npc.Action.Label = "Hunt: put on coat";
                if (!Equip(world, npc, ItemKind.WinterCoat)) { unavailable = true; return false; }
                npc.Survival.CoatDurability = SurvivalRules.ToolDurability;
            }
            if (weapon == ItemKind.Bow && !Fill(world, npc, ItemKind.Arrow, HuntingRules.ArrowsPerArcher)) { unavailable = true; return false; }
            int food = 1 + Math.Max(0, (7000 - npc.Needs.Food + 4499) / 4500);
            int water = 1 + Math.Max(0, (7000 - npc.Needs.Water + 5499) / 5500);
            if (!Fill(world, npc, ItemKind.Food, food) || !Fill(world, npc, ItemKind.Water, water)) { unavailable = true; return false; }
            EatDrink(world, npc, HuntingRules.DepartureNeed);
            if (npc.Needs.Energy < HuntingRules.DepartureNeed || npc.Survival.BodyTemperature < 3600 || npc.Survival.Wetness >= 5000)
            {
                npc.Action.Label = "Hunt: rest before departure";
                if (npc.Position != npc.Home) { unavailable = !HuntingNavigation.Move(world, npc, npc.Home); }
                else { npc.Action.SetTimed(NpcActionType.Sleep, "Hunt: rest before departure", npc.Home, world.Tick + 1); }
                return false;
            }
            npc.Action.SetTimed(NpcActionType.Work, "Hunt: supplies ready", world.Resources.StockpilePosition, world.Tick + 1);
            return Ready(world, npc, member);
        }

        private static bool Equip(WorldState world, NpcState npc, ItemKind item) =>
            npc.Inventory.TryRemove(item, 1) || world.Resources.TakeHuntSupply(npc.Id, item, 1) == 1 ||
            world.Resources.AvailableStock(item) > 0 && world.Resources.Stockpile.TryRemove(item, 1);

        private static bool Fill(WorldState world, NpcState npc, ItemKind item, int desired)
        {
            int missing = Math.Max(0, desired - npc.Inventory.Count(item));
            if (missing == 0) { return true; }
            if (npc.Inventory.Capacity - npc.Inventory.TotalCount < missing) { return false; }
            int taken = world.Resources.TakeHuntSupply(npc.Id, item, missing);
            int extra = Math.Min(missing - taken, world.Resources.AvailableStock(item));
            if (extra > 0 && world.Resources.Stockpile.TryRemove(item, extra)) { taken += extra; }
            if (taken > 0) { npc.Inventory.TryAdd(item, taken); }
            return taken == missing;
        }

        internal static void EatDrink(WorldState world, NpcState npc, int threshold = 4000)
        {
            if (npc.Needs.Food < threshold && npc.Inventory.TryRemove(ItemKind.Food, 1))
            { world.Materials.Consume(ItemKind.Food, 1); npc.Needs.Hunger = Math.Min(10000, npc.Needs.Hunger + 4500); npc.Action.Label = "Hunt: eat rations"; }
            if (npc.Needs.Water < threshold && npc.Inventory.TryRemove(ItemKind.Water, 1))
            { world.Materials.Consume(ItemKind.Water, 1); npc.Needs.Thirst = Math.Min(10000, npc.Needs.Thirst + 5500); npc.Action.Label = "Hunt: drink supplies"; }
        }
    }
}
