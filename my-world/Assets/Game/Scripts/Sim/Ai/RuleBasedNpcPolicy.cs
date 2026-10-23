using System;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.Ai
{
    public sealed class RuleBasedNpcPolicy : INpcDecisionPolicy
    {
        private static readonly int[] DirectionX = { 0, 1, 0, -1 };
        private static readonly int[] DirectionY = { 1, 0, -1, 0 };

        public NpcDecision Decide(NpcDecisionContext context)
        {
            if (context.World != null)
            {
                return DecideSurvival(context);
            }

            NpcState npc = context.Npc;
            var rng = new DeterministicRng(npc.RngState);
            Personality personality = npc.Personality;

            int restScore = 32 + (100 - personality.Curiosity) / 3 + (100 - personality.Diligence) / 4;
            int wanderScore = 36 + personality.Curiosity / 2 + personality.Diligence / 5;
            if (!HasWalkableNeighbor(context.Map, npc.Position))
            {
                wanderScore = 0;
            }

            var trace = new NpcDecisionTrace();
            trace.Options.Add(new NpcDecisionOption("Rest", restScore));
            trace.Options.Add(new NpcDecisionOption("Wander", wanderScore));

            int total = restScore + wanderScore;
            int roll = total <= 0 ? 0 : rng.NextInt(0, total);
            bool chooseWander = wanderScore > 0 && roll >= restScore;

            if (chooseWander)
            {
                GridPos target;
                if (TryPickWanderTarget(context.Map, npc, ref rng, out target))
                {
                    trace.Goal = "Wander";
                    trace.ReasonCode = "curiosity_and_diligence";
                    trace.ChosenScore = wanderScore;
                    return new NpcDecision(NpcGoalType.Wander, target, 0, rng.State, trace);
                }

                wanderScore = 0;
                trace.Options[1] = new NpcDecisionOption("Wander", 0);
            }

            int duration = 3 + rng.NextInt(0, 10);
            trace.Goal = "Rest";
            trace.ReasonCode = restScore >= wanderScore ? "rest_bias" : "no_valid_wander_target";
            trace.ChosenScore = restScore;
            return new NpcDecision(NpcGoalType.Rest, npc.Position, duration, rng.State, trace);
        }

        private static NpcDecision DecideSurvival(NpcDecisionContext context)
        {
            NpcState npc = context.Npc;
            var trace = new NpcDecisionTrace();
            ShelterState home = context.World.Survival.FindShelter(npc.Survival.ShelterId);
            if (ThermalSystem.IsAtShelter(context.World, npc) && npc.Needs.Food >= 1500 && npc.Needs.Water >= 1800 &&
                (home.FuelTicks == 0 && home.Inventory.Count(ItemKind.Wood) == 0 && npc.Inventory.Count(ItemKind.Wood) > 0 ||
                 npc.Survival.CoatDurability == 0 && npc.Inventory.Count(ItemKind.WinterCoat) > 0))
            {
                trace.Goal = NpcGoalType.Produce.ToString();
                trace.ChosenScore = 20000;
                trace.ReasonCode = "restore_shelter_warmth";
                return new NpcDecision(NpcGoalType.Produce, npc.Home, 2, npc.RngState, trace);
            }
            if (npc.Needs.Food >= 1500 && npc.Needs.Water >= 1800 &&
                (ThermalSystem.ShouldSeekWarmth(context.World, npc) ||
                 !context.World.Environment.IsDaytime && npc.Position.ManhattanDistance(npc.Home) > 5 &&
                 context.World.Environment.Temperature < 0))
            {
                trace.Goal = NpcGoalType.WarmUp.ToString();
                trace.ChosenScore = 20000;
                trace.ReasonCode = npc.Survival.IsCold ? "seek_warmth" : "return_before_cold_night";
                trace.Options.Add(new NpcDecisionOption(trace.Goal, trace.ChosenScore));
                return new NpcDecision(NpcGoalType.WarmUp, npc.Home, 30, npc.RngState, trace);
            }
            NpcGoalType[] goals =
            {
                NpcGoalType.Drink, NpcGoalType.Eat, NpcGoalType.Sleep,
                NpcGoalType.Farm, NpcGoalType.GatherFood, NpcGoalType.GatherWater, NpcGoalType.Rest, NpcGoalType.Hunt, NpcGoalType.Trade
            };
            bool farmWork = false;
            for (int i = 0; i < context.World.Resources.FarmPlots.Count; i++)
            {
                FarmPlotState plot = context.World.Resources.FarmPlots[i];
                if (plot.ReservedBy.IsNone && AgricultureSystem.NeedsWork(context.World, plot))
                {
                    farmWork = true;
                    break;
                }
            }
            bool foodNode = false;
            bool waterNode = false;
            for (int i = 0; i < context.World.Resources.Nodes.Count; i++)
            {
                ResourceNodeState node = context.World.Resources.Nodes[i];
                if (!node.ReservedBy.IsNone || node.Amount <= 0)
                {
                    continue;
                }
                if (node.Item == ItemKind.Food)
                {
                    foodNode = true;
                }
                if (node.Item == ItemKind.RawWater)
                {
                    waterNode = true;
                }
            }
            int[] scores =
            {
                Math.Max(0, 7000 - npc.Needs.Thirst) * 3,
                Math.Max(0, 6500 - npc.Needs.Hunger) * 2,
                Math.Max(0, 6000 - npc.Needs.Fatigue) * 2 + Math.Max(0, 6500 - npc.Needs.Safety),
                farmWork && (npc.Role == "Farmer" || context.World.Resources.Stockpile.Food < 40)
                    ? (npc.Role == "Farmer" ? 2200 : 900) + npc.Personality.Diligence * 2 : 0,
                foodNode && context.World.Resources.Stockpile.Food < (npc.Role == "Gatherer" ? 80 : 40)
                    ? (npc.Role == "Gatherer" ? 1950 : 1100) : 0,
                waterNode && context.World.Resources.Stockpile.Count(ItemKind.RawWater) < Math.Max(12, context.World.LivingCount * 2)
                    ? (npc.Role == "Trader" ? 1900 : 1050) : 0,
                100,
                HasPrey(context) && npc.Needs.Energy > 4000 && npc.Needs.Health >= 3000 &&
                    context.World.Environment.IsDaytime && !npc.Survival.IsCold &&
                    (context.World.Resources.Stockpile.Food < AgricultureSystem.FoodTarget(context.World)) ? 2100 : 0,
                HasSurplus(context) ? (npc.Role == "Trader" ? 2200 : 1750) : 0
            };
            int best = 0;
            for (int i = 0; i < goals.Length; i++)
            {
                trace.Options.Add(new NpcDecisionOption(goals[i].ToString(), scores[i]));
                if (scores[i] > scores[best])
                {
                    best = i;
                }
            }
            if (npc.Survival.Life == LifeState.Recovering)
            {
                NpcGoalType goal = npc.Needs.Water < 3000 ? NpcGoalType.Drink : npc.Needs.Food < 3000 ? NpcGoalType.Eat : NpcGoalType.Sleep;
                trace.Goal = goal.ToString(); trace.ReasonCode = "recovery_needs";
                return new NpcDecision(goal, npc.Home, 60, npc.RngState, trace);
            }
            if (npc.Needs.Food > 3500 && npc.Needs.Water > 3500 && npc.Needs.Energy > 3500 && npc.Needs.Health > 4000)
            {
                foreach (var target in context.World.Entities.Npcs)
                {
                    if (HealthSystem.NeedsRescue(target) &&
                        (target.Survival.Life == LifeState.Recovering || npc.Inventory.Count(ItemKind.Medicine) > 0 || context.World.Resources.AvailableStock(ItemKind.Medicine) > 0))
                    {
                        trace.Goal = "Rescue"; trace.ReasonCode = "urgent_rescue";
                        return new NpcDecision(NpcGoalType.Rescue, target.Position, 180, npc.RngState, trace);
                    }
                }
            }
            if (npc.Survival.DiseaseTicks > 0 && npc.Needs.Food > 2000 && npc.Needs.Water > 2000 &&
                (npc.Inventory.Count(ItemKind.Medicine) > 0 || context.World.Resources.AvailableStock(ItemKind.Medicine) > 0))
            {
                trace.Goal = "Treat"; trace.ReasonCode = "treat_waterborne_illness";
                return new NpcDecision(NpcGoalType.Treat, npc.Position, 4, npc.RngState, trace);
            }
            int productionScore = ProductionSystem.Needed(context.World, npc) ?
                (ProductionSystem.NeedsArrows(npc) || ProductionSystem.CookingNeeded(context.World) || ThermalSystem.NeedsFuel(context.World) ? 2800 :
                npc.Role == "Builder" || npc.Role == "Trader" ? 2700 : context.World.Resources.Stockpile.Water < context.World.LivingCount ? 2400 : 1600) : 0;
            trace.Options.Add(new NpcDecisionOption("Produce", productionScore));
            // Only discretionary work/leisure yields to social activity; survival scores win.
            if (best >= 3 && SocialSystem.Comfortable(npc))
            {
                NpcState partner = SocialSystem.ChoosePartner(context.World, npc, SocialKind.FoodGift);
                NpcGoalType socialGoal = NpcGoalType.GiveFood;
                if (partner == null && goals[best] == NpcGoalType.Rest && productionScore == 0)
                {
                    partner = SocialSystem.ChoosePartner(context.World, npc, SocialKind.Greeting);
                    socialGoal = NpcGoalType.Greet;
                }
                if (partner != null)
                {
                    trace.Goal = socialGoal.ToString(); trace.ChosenScore = 2400;
                    trace.ReasonCode = socialGoal == NpcGoalType.Greet ? "friendly_greeting" :
                        npc.Social.HelpReceivedFrom(partner.Id, npc.Id) > 0 ? "reciprocate_help" : "help_hungry_neighbor";
                    trace.Options.Add(new NpcDecisionOption(trace.Goal, trace.ChosenScore));
                    return new NpcDecision(socialGoal, partner.Position, 45, npc.RngState, trace, partner.Id);
                }
            }
            if (productionScore > scores[best])
            {
                trace.Goal = "Produce"; trace.ReasonCode = "village_production_shortage";
                return new NpcDecision(NpcGoalType.Produce, npc.Position, 30, npc.RngState, trace);
            }
            trace.Goal = goals[best].ToString();
            trace.ChosenScore = scores[best];
            trace.ReasonCode = best < 3 ? "need_satisfaction_deficit" : "profession_supply_strategy";
            if (goals[best] == NpcGoalType.Rest)
            {
                return DecideLeisure(context, trace);
            }
            return new NpcDecision(goals[best], npc.Home, 80, npc.RngState, trace);
        }

        private static NpcDecision DecideLeisure(NpcDecisionContext context, NpcDecisionTrace trace)
        {
            NpcState npc = context.Npc;
            var rng = new DeterministicRng(npc.RngState);
            // Finish using the shared counter, then leave room for the next worker.
            // Otherwise alternate short breaks and walks using this NPC's own RNG.
            bool leaveCounter = npc.Position.ManhattanDistance(context.World.Resources.StockpilePosition) <= 2;
            bool walk = leaveCounter || rng.Chance(45 + npc.Personality.Curiosity / 3);
            int duration = 12 + rng.NextInt(0, 17);
            GridPos target;
            if (walk && TryPickLeisureTarget(context, ref rng, out target))
            {
                trace.Goal = NpcGoalType.Wander.ToString();
                trace.ChosenScore = 150;
                trace.Options.Add(new NpcDecisionOption(trace.Goal, trace.ChosenScore));
                trace.ReasonCode = leaveCounter ? "clear_shared_counter" : "leisure_walk";
                return new NpcDecision(NpcGoalType.Wander, target, duration, rng.State, trace);
            }

            trace.ReasonCode = "short_leisure_break";
            return new NpcDecision(NpcGoalType.Rest, npc.Position, duration, rng.State, trace);
        }

        private static bool TryPickLeisureTarget(NpcDecisionContext context, ref DeterministicRng rng, out GridPos target)
        {
            NpcState npc = context.Npc;
            GridPos anchor = rng.Chance(50) ? npc.Home : npc.Position;
            var pathfinder = new GridPathfinder();
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var candidate = anchor.Offset(rng.NextInt(-7, 8), rng.NextInt(-7, 8));
                if (!context.Map.IsWalkable(candidate) || candidate == npc.Position ||
                    candidate.ManhattanDistance(context.World.Resources.StockpilePosition) <= 2 ||
                    context.World.Entities.IsCellOccupied(candidate) || context.Map.GetTile(candidate) == TileType.Farmland)
                {
                    continue;
                }
                bool workCell = false;
                foreach (ResourceNodeState node in context.World.Resources.Nodes)
                {
                    if (node.Position == candidate) { workCell = true; break; }
                }
                if (workCell || !context.World.Resources.IsWorkCellAvailable(npc.Id, candidate) ||
                    !pathfinder.FindPath(context.Map, npc.Position, candidate).Found)
                {
                    continue;
                }
                target = candidate;
                return true;
            }
            target = GridPos.Invalid;
            return false;
        }

        private static bool HasPrey(NpcDecisionContext context)
        {
            foreach (CarcassState carcass in context.World.Ecology.Carcasses)
            {
                if (carcass.Region == -1 && carcass.ReservedBy.IsNone &&
                    carcass.MeatRemaining + carcass.HideRemaining + carcass.Inventory.TotalCount > 0 &&
                    context.Npc.Inventory.TotalCount < context.Npc.Inventory.Capacity &&
                    HuntingSystem.Perceived(context.World, context.Npc, carcass.Position)) { return true; }
            }
            if (!context.Npc.HasHuntingTool || context.Npc.Survival.Durability(HuntingSystem.Weapon(context.Npc)) <= 0)
            {
                return false;
            }
            for (int i = 0; i < context.World.Ecology.Animals.Count; i++)
            {
                AnimalState animal = context.World.Ecology.Animals[i];
                if (animal.ReservedBy.IsNone && HuntingSystem.Perceived(context.World, context.Npc, animal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasSurplus(NpcDecisionContext context)
        {
            for (int i = 0; i < SurvivalRules.MarketItemCount; i++)
            {
                ItemKind item = (ItemKind)i;
                if (context.Npc.Inventory.Count(item) > 1 && context.World.Market.Inventory.Count(item) < 60 &&
                    context.World.Market.Treasury >= 2L * context.World.Market.Price(item))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasWalkableNeighbor(World.WorldMap map, GridPos position)
        {
            for (int i = 0; i < 4; i++)
            {
                var next = position.Offset(DirectionX[i], DirectionY[i]);
                if (map.IsWalkable(next))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryPickWanderTarget(
            World.WorldMap map,
            NpcState npc,
            ref DeterministicRng rng,
            out GridPos target)
        {
            bool useHomeAsAnchor = rng.Chance(25);
            GridPos anchor = useHomeAsAnchor ? npc.Home : npc.Position;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                int radius = 2 + rng.NextInt(0, 7);
                int dx = rng.NextInt(-radius, radius + 1);
                int dy = rng.NextInt(-radius, radius + 1);
                var candidate = new GridPos(anchor.X + dx, anchor.Y + dy);
                if (!map.IsWalkable(candidate) || candidate == npc.Position)
                {
                    continue;
                }

                target = candidate;
                return true;
            }

            target = GridPos.Invalid;
            return false;
        }
    }
}
