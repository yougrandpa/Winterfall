using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class SocialSystem
    {
        public static bool Comfortable(NpcState npc) => npc.Survival.Life == LifeState.Active && npc.Needs.Food >= 6000 && npc.Needs.Water >= 6000 &&
            npc.Needs.Energy >= 6000 && npc.Needs.Health >= 3000;

        public static NpcState ChoosePartner(WorldState world, NpcState actor, SocialKind kind)
        {
            if (!Comfortable(actor) || world.Tick < actor.Social.NextInteractionTick ||
                (kind == SocialKind.FoodGift && actor.Inventory.Food < 2)) { return null; }
            NpcState best = null; int bestScore = int.MinValue;
            foreach (NpcState other in world.Entities.Npcs)
            {
                int distance = actor.Position.ManhattanDistance(other.Position);
                if (other.Survival.Life != LifeState.Active || other.Id == actor.Id || other.Needs.Health <= 0 || distance > (kind == SocialKind.FoodGift ? 8 : 5)) { continue; }
                if (kind == SocialKind.FoodGift && (other.Needs.Food >= 6500 || other.Inventory.Food > 0 || other.Inventory.TotalCount >= other.Inventory.Capacity)) { continue; }
                int score = actor.Social.TrustIn(other.Id) * 100 - distance * 3 + (kind == SocialKind.FoodGift ? (10000 - other.Needs.Food) / 100 : 0);
                if (score <= bestScore) { continue; }
                // Candidates are ordered by stable entity id; score ties retain the earlier id.
                if (!new GridPathfinder().FindPath(world.Map, actor.Position, other.Position).Found) { continue; }
                best = other; bestScore = score;
            }
            return best;
        }

        internal static bool Interact(WorldState world, NpcState actor, NpcState target, SocialKind kind, List<SimEvent> events)
        {
            if (target == null || target.Survival.Life != LifeState.Active || actor.Id == target.Id || actor.Needs.Health <= 0 || target.Needs.Health <= 0 ||
                !Comfortable(actor) || world.Tick < actor.Social.NextInteractionTick || actor.Position.ManhattanDistance(target.Position) > 1) { return false; }
            if (kind == SocialKind.FoodGift && (actor.Inventory.Food < 2 || target.Inventory.TotalCount >= target.Inventory.Capacity || target.Needs.Food >= 6500 || target.Inventory.Food > 0)) { return false; }
            long cause = kind == SocialKind.FoodGift ? actor.Social.HelpReceivedFrom(target.Id, actor.Id) : 0;
            string reason = kind == SocialKind.Greeting ? "friendly_greeting" : cause > 0 ? "reciprocate_help" : "help_hungry_neighbor";
            if (kind == SocialKind.FoodGift)
            {
                // Validate both legs first; a gift is a transfer, never a source/sink.
                actor.Inventory.TryRemove(ItemKind.Food, 1); target.Inventory.TryAdd(ItemKind.Food, 1);
                world.Social.Gifts++;
            }
            else
            {
                actor.Social.SetTrust(target.Id, Math.Min(100, actor.Social.TrustIn(target.Id) + 1));
                world.Social.Greetings++;
            }
            int before = target.Social.TrustIn(actor.Id);
            int after = Math.Min(100, before + (kind == SocialKind.FoodGift ? 12 : 1));
            target.Social.SetTrust(actor.Id, after);
            var evt = new SocialEvent(world.Social.NextSequence++, world.Tick, actor.Id, target.Id, kind, before, after, reason, cause);
            actor.Social.Remember(evt); target.Social.Remember(evt); world.Social.Append(evt);
            actor.Social.NextInteractionTick = world.Tick + (kind == SocialKind.FoodGift ? 120 : 180);
            events.Add(new SimEvent(kind == SocialKind.FoodGift ? SimEventKind.FoodGift : SimEventKind.Greeting,
                world.Tick, actor.Id, actor.Name + " -> " + target.Name + ": " + reason + "; trust " + before + " -> " + after + "; event #" + evt.Sequence + "; cause #" + cause + "."));
            return true;
        }
    }
}
