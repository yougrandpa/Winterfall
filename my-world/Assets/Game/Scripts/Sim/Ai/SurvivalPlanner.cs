using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Ai
{
    // A bounded domain planner: choose one reachable source, compose prerequisite steps,
    // and reserve its stock and work cell atomically before publishing the plan.
    internal sealed partial class SurvivalPlanner
    {
        private readonly GridPathfinder _pathfinder;
        public SurvivalPlanner(GridPathfinder pathfinder)
        {
            _pathfinder = pathfinder;
        }

        public bool TryBuild(WorldState world, NpcState npc, NpcDecision decision, out string failure)
        {
            var steps = new List<PlanStep>();
            world.Resources.Release(npc.Id);
            world.Ecology.Release(npc.Id);
            ProductionSystem.Release(world, npc);
            bool found;
            // Interrupted work may leave useful supplies in a bounded bag. Deposit
            // excess before planning a new source/harvest instead of retrying forever.
            bool needsSpace = (decision.Goal == NpcGoalType.Eat && npc.Inventory.Food == 0 && npc.Inventory.TotalCount == npc.Inventory.Capacity) ||
                (decision.Goal == NpcGoalType.Drink && npc.Inventory.Water == 0 && npc.Inventory.TotalCount == npc.Inventory.Capacity) ||
                ((decision.Goal == NpcGoalType.Farm || decision.Goal == NpcGoalType.Hunt || decision.Goal == NpcGoalType.Produce || decision.Goal == NpcGoalType.Rescue) && npc.Inventory.TotalCount > 6) ||
                ((decision.Goal == NpcGoalType.GatherFood || decision.Goal == NpcGoalType.GatherWater) && npc.Inventory.TotalCount > 8);
            if (needsSpace && CanReach(world, npc.Position, world.Resources.StockpilePosition))
            {
                int stockSpace = world.Resources.Stockpile.Capacity - world.Resources.Stockpile.TotalCount;
                int deposited = 0;
                for (int i = 0; i < (int)ItemKind.Count; i++)
                {
                    ItemKind carried = (ItemKind)i;
                    int retained = carried == ItemKind.Arrow && npc.Role == "Hunter" ? Math.Min(4, npc.Inventory.Count(carried)) : 0;
                    int amount = Math.Min(npc.Inventory.Count(carried) - retained, stockSpace - deposited);
                    if (amount <= 0)
                    {
                        continue;
                    }
                    if (steps.Count == 0)
                    {
                        Move(steps, world.Resources.StockpilePosition);
                    }
                    steps.Add(new PlanStep(PlanStepType.Deposit, world.Resources.StockpilePosition, item: carried, quantity: amount));
                    deposited += amount;
                }
                if (deposited > 0)
                {
                    npc.Plan.Start(decision.Goal, steps);
                    failure = "none";
                    return true;
                }
            }
            switch (decision.Goal)
            {
                case NpcGoalType.WarmUp:
                    ShelterState home = world.Survival.FindShelter(npc.Survival.ShelterId);
                    if (home != null && home.FuelTicks == 0 && home.Inventory.Count(ItemKind.Wood) == 0 &&
                        SupplyShelter(world, npc, steps))
                    {
                        found = true;
                        break;
                    }
                    if (npc.Survival.CoatDurability == 0 && npc.Inventory.Count(ItemKind.WinterCoat) > 0)
                    {
                        steps.Add(new PlanStep(PlanStepType.Equip, npc.Position, item: ItemKind.WinterCoat));
                        found = true;
                        break;
                    }
                    found = home != null && CanReach(world, npc.Position, npc.Home);
                    if (found)
                    {
                        Move(steps, npc.Home);
                        steps.Add(new PlanStep(PlanStepType.WarmUp, npc.Home, npc.Survival.ShelterId, durationTicks: 30));
                    }
                    break;
                case NpcGoalType.Treat:
                    found = EnsureItem(world, npc, ItemKind.Medicine, steps);
                    if (found) { steps.Add(new PlanStep(PlanStepType.Treat, steps.Count == 0 ? npc.Position : steps[steps.Count - 1].Target, item: ItemKind.Medicine, durationTicks: 4)); }
                    break;
                case NpcGoalType.Produce:
                    found = BuildProduction(world, npc, steps);
                    break;
                case NpcGoalType.Rescue:
                    found = BuildRescue(world, npc, steps);
                    break;
                case NpcGoalType.Eat:
                case NpcGoalType.Drink:
                    ItemKind item = decision.Goal == NpcGoalType.Eat ? ItemKind.Food : ItemKind.Water;
                    found = EnsureItem(world, npc, item, steps);
                    if (!found)
                    {
                        world.Resources.Release(npc.Id); steps.Clear();
                        found = Recipe(world, npc, ProductionRecipe.For(item), steps);
                        if (found) { break; }
                        world.Resources.Release(npc.Id); steps.Clear();
                        if (item == ItemKind.Water && MeltSnow(world, npc, steps))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found && item == ItemKind.Water && npc.Needs.Thirst < 3000)
                    {
                        world.Resources.Release(npc.Id); steps.Clear();
                        found = EnsureItem(world, npc, ItemKind.RawWater, steps);
                        if (found) { steps.Add(new PlanStep(PlanStepType.DrinkRaw, steps.Count > 0 ? steps[steps.Count - 1].Target : npc.Position, durationTicks: 4)); break; }
                    }
                    if (found)
                    {
                        steps.Add(new PlanStep(item == ItemKind.Food ? PlanStepType.Eat : PlanStepType.Drink,
                            steps.Count > 0 ? steps[steps.Count - 1].Target : npc.Position, durationTicks: 4));
                    }
                    break;
                case NpcGoalType.Sleep:
                    found = CanReach(world, npc.Position, npc.Home);
                    if (found)
                    {
                        Move(steps, npc.Home);
                        steps.Add(new PlanStep(PlanStepType.Sleep, npc.Home, durationTicks: 100));
                    }
                    break;
                case NpcGoalType.GatherFood:
                case NpcGoalType.GatherWater:
                    item = decision.Goal == NpcGoalType.GatherFood ? ItemKind.Food : ItemKind.RawWater;
                    found = Gather(world, npc, item, steps, item == ItemKind.RawWater ? WaterSystem.CollectionAmount(world, npc) : 3);
                    if (!found && item == ItemKind.RawWater)
                    {
                        world.Resources.Release(npc.Id);
                        steps.Clear();
                        found = MeltSnow(world, npc, steps);
                    }
                    if (found)
                    {
                        AddDeposit(world, steps, item, steps[steps.Count - 1].Quantity);
                    }
                    break;
                case NpcGoalType.Greet:
                case NpcGoalType.GiveFood:
                    found = world.Entities.TryGetNpc(decision.TargetEntity, out NpcState partner) &&
                        partner.Id != npc.Id && CanReach(world, npc.Position, partner.Position);
                    if (found)
                    {
                        steps.Add(new PlanStep(decision.Goal == NpcGoalType.Greet ? PlanStepType.Greet : PlanStepType.GiveFood,
                            partner.Position, resourceId: partner.Id.Value, durationTicks: 45));
                    }
                    break;
                case NpcGoalType.Hunt:
                    found = Hunt(world, npc, steps);
                    break;
                case NpcGoalType.Trade:
                    found = SellSurplus(world, npc, steps);
                    break;
                case NpcGoalType.Farm:
                    found = Farm(world, npc, steps);
                    break;
                case NpcGoalType.Wander:
                    found = CanReach(world, npc.Position, decision.Target);
                    if (found)
                    {
                        Move(steps, decision.Target);
                        steps.Add(new PlanStep(PlanStepType.Wait, decision.Target,
                            durationTicks: Math.Max(1, decision.DurationTicks)));
                    }
                    break;
                default:
                    found = true;
                    steps.Add(new PlanStep(PlanStepType.Wait, npc.Position, durationTicks: Math.Max(1, decision.DurationTicks)));
                    break;
            }
            if (!found || steps.Count == 0)
            {
                world.Resources.Release(npc.Id);
                failure = decision.Goal == NpcGoalType.Farm ? "no_available_farm_work_or_supplies" : "no_reachable_unreserved_resource";
                return false;
            }
            npc.Plan.Start(decision.Goal, steps);
            failure = "none";
            return true;
        }

        private bool EnsureItem(WorldState world, NpcState npc, ItemKind item, List<PlanStep> steps)
        {
            if (npc.Inventory.Count(item) > 0)
            {
                return true;
            }
            int plannedAdds = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Type == PlanStepType.TakeFromStock || steps[i].Type == PlanStepType.Gather || steps[i].Type == PlanStepType.Buy)
                {
                    plannedAdds += steps[i].Quantity;
                }
            }
            if (npc.Inventory.TotalCount + plannedAdds >= npc.Inventory.Capacity)
            {
                return false;
            }
            GridPos start = steps.Count == 0 ? npc.Position : steps[steps.Count - 1].Target;
            GridPos stock = world.Resources.StockpilePosition;
            int price = world.Market.Price(item);
            if (world.Market.Inventory.Count(item) > 0 && npc.Coins >= price && price > 0 && CanReach(world, start, stock))
            {
                Move(steps, stock);
                steps.Add(new PlanStep(PlanStepType.Buy, stock, item: item, unitPrice: price));
                return true;
            }
            if (world.Resources.AvailableStock(item) > 0 && CanReach(world, start, stock) &&
                world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.Stockpile, 0, item, 1, stock)))
            {
                Move(steps, stock);
                steps.Add(new PlanStep(PlanStepType.TakeFromStock, stock, item: item));
                return true;
            }
            foreach (FacilityState facility in world.Survival.Facilities)
            {
                if (facility.Inventory.Count(item) <= 0 || !facility.ReservedBy.IsNone ||
                    !CanReach(world, start, facility.WorkCell) || !ClaimFacility(world, npc, facility)) { continue; }
                Move(steps, facility.WorkCell);
                steps.Add(new PlanStep(PlanStepType.TakeFacility, facility.WorkCell, facility.Id, item));
                return true;
            }
            return !SurvivalRules.IsSeed(item) && Gather(world, npc, item, steps, 1);
        }

        private bool Gather(WorldState world, NpcState npc, ItemKind item, List<PlanStep> steps, int desired)
        {
            int space = npc.Inventory.Capacity - npc.Inventory.TotalCount;
            foreach (var planned in steps) { if (planned.Type == PlanStepType.Gather || planned.Type == PlanStepType.TakeFromStock || planned.Type == PlanStepType.Buy) { space -= planned.Quantity; } }
            if (space <= 0)
            {
                return false;
            }
            var candidates = new List<ResourceNodeState>();
            for (int i = 0; i < world.Resources.Nodes.Count; i++)
            {
                ResourceNodeState node = world.Resources.Nodes[i];
                if (node.Item == item && node.Amount > 0 && node.ReservedBy.IsNone && WaterSystem.SourceAvailable(world, node))
                {
                    candidates.Add(node);
                }
            }
            GridPos start = steps.Count == 0 ? npc.Position : steps[steps.Count - 1].Target;
            candidates.Sort((a, b) => CompareTargets(start, a.Position, a.Id, b.Position, b.Id));
            for (int i = 0; i < candidates.Count; i++)
            {
                ResourceNodeState node = candidates[i];
                int amount = Math.Min(Math.Min(desired, space), node.Amount);
                if (!CanReach(world, start, node.Position) || !world.Resources.TryReserve(
                    new ResourceReservation(npc.Id, ReservationKind.Node, node.Id, item, amount, node.Position)))
                {
                    continue;
                }
                Move(steps, node.Position);
                steps.Add(new PlanStep(PlanStepType.Gather, node.Position, node.Id, item, amount, 8));
                return true;
            }
            return false;
        }

        private bool Farm(WorldState world, NpcState npc, List<PlanStep> steps)
        {
            var candidates = new List<FarmPlotState>();
            foreach (var plot in world.Resources.FarmPlots)
            { if (plot.ReservedBy.IsNone && AgricultureSystem.NeedsWork(world, plot)) { candidates.Add(plot); } }
            candidates.Sort((a, b) =>
            {
                int pa = a.Crop != CropType.None && a.Stage != CropStage.Mature && a.Moisture < 180 ? 0 : a.Stage == CropStage.Mature ? 1 : 2;
                int pb = b.Crop != CropType.None && b.Stage != CropStage.Mature && b.Moisture < 180 ? 0 : b.Stage == CropStage.Mature ? 1 : 2;
                return pa != pb ? pa.CompareTo(pb) : CompareTargets(npc.Position, a.Position, a.Id, b.Position, b.Id);
            });
            foreach (var first in candidates)
            {
                if (!CanReach(world, npc.Position, first.Position)) { continue; }
                bool harvest = first.Stage == CropStage.Mature;
                bool planting = first.Crop == CropType.None;
                var crop = AgricultureSystem.ChooseCrop(world, first);
                var selected = new List<FarmPlotState>();
                int outputSpace = 0;
                foreach (var plot in candidates)
                {
                    if (selected.Count >= 4 || plot.Position.ManhattanDistance(first.Position) > 4 ||
                        (plot.Stage == CropStage.Mature) != harvest || (plot.Crop == CropType.None) != planting ||
                        (planting && AgricultureSystem.ChooseCrop(world, plot).Type != crop.Type) || !CanReach(world, first.Position, plot.Position)) { continue; }
                    int required = harvest ? plot.HarvestYield + CropDefinition.For(plot.Crop).SeedYield : planting ? 2 : 1;
                    if (outputSpace + required > npc.Inventory.Capacity - npc.Inventory.TotalCount) { continue; }
                    if (!world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.FarmPlot, plot.Id, ItemKind.Food, 0, plot.Position))) { continue; }
                    selected.Add(plot); outputSpace += required;
                }
                if (selected.Count == 0) { continue; }
                ItemKind water = npc.Inventory.Count(ItemKind.RawWater) > 0 || world.Resources.AvailableStock(ItemKind.RawWater) > 0 ? ItemKind.RawWater : ItemKind.Water;
                // Natural raw-water sources are available even when the village store is empty.
                if (water == ItemKind.Water && world.Resources.AvailableStock(ItemKind.Water) < Math.Max(world.LivingCount, selected.Count) && npc.Inventory.Water < selected.Count) { water = ItemKind.RawWater; }
                bool supplied = harvest || ((!planting || Acquire(world, npc, crop.Seed, selected.Count, steps)) && Acquire(world, npc, water, selected.Count, steps));
                if (!supplied) { world.Resources.Release(npc.Id); steps.Clear(); continue; }
                var outputs = new int[(int)ItemKind.Count];
                foreach (var plot in selected)
                {
                    Move(steps, plot.Position);
                    if (harvest)
                    {
                        steps.Add(new PlanStep(PlanStepType.Harvest, plot.Position, plot.Id, durationTicks: npc.Survival.Durability(ItemKind.Hoe) > 0 ? 5 : 8));
                        outputs[(int)ItemKind.Food] += plot.HarvestYield;
                        outputs[(int)CropDefinition.For(plot.Crop).Seed] += CropDefinition.For(plot.Crop).SeedYield;
                    }
                    else
                    {
                        if (planting) { steps.Add(new PlanStep(PlanStepType.Plant, plot.Position, plot.Id, crop.Seed, durationTicks: npc.Survival.Durability(ItemKind.Hoe) > 0 ? 3 : 5)); }
                        steps.Add(new PlanStep(PlanStepType.Water, plot.Position, plot.Id, water, durationTicks: 4));
                    }
                }
                if (harvest)
                { for (int i = 0; i < outputs.Length; i++) { if (outputs[i] > 0) { AddDeposit(world, steps, (ItemKind)i, outputs[i]); } } }
                return true;
            }
            return false;
        }

        private bool Hunt(WorldState world, NpcState npc, List<PlanStep> steps)
        {
            foreach (CarcassState carcass in world.Ecology.Carcasses)
            {
                if (carcass.Region != -1 || !carcass.ReservedBy.IsNone || world.Hunting.ForTarget(carcass.Id) != null ||
                    carcass.MeatRemaining + carcass.HideRemaining + carcass.Inventory.TotalCount == 0 ||
                    npc.Inventory.TotalCount == npc.Inventory.Capacity || !HuntingSystem.Perceived(world, npc, carcass.Position) ||
                    !CanReach(world, npc.Position, carcass.Position)) { continue; }
                carcass.ReservedBy = npc.Id;
                steps.Add(new PlanStep(PlanStepType.Hunt, carcass.Position, carcass.Id.Value, durationTicks: 120));
                return true;
            }
            if (!HuntingCoordinator.Eligible(world, npc) ||
                npc.Needs.Energy < 3000 || npc.Needs.Health < 3000 || npc.Inventory.TotalCount == npc.Inventory.Capacity)
            {
                return false;
            }
            var candidates = new List<AnimalState>();
            for (int i = 0; i < world.Ecology.Animals.Count; i++)
            {
                AnimalState animal = world.Ecology.Animals[i];
                if (HuntingSystem.Perceived(world, npc, animal) && animal.ReservedBy.IsNone)
                {
                    candidates.Add(animal);
                }
            }
            var costs = new Dictionary<int, int>();
            foreach (var animal in candidates)
            {
                var path = _pathfinder.FindPath(world.Map, npc.Position, animal.Position);
                costs[animal.Id.Value] = !path.Found ? int.MaxValue : path.Cost * 10 +
                    animal.Definition.InjuryRisk * (100 + npc.Personality.Caution - npc.HuntingSkill) / 10 - animal.Definition.FoodYield * 8;
            }
            candidates.Sort((a, b) => { int order = costs[a.Id.Value].CompareTo(costs[b.Id.Value]); return order != 0 ? order : a.Id.Value.CompareTo(b.Id.Value); });
            for (int i = 0; i < candidates.Count; i++)
            {
                AnimalState animal = candidates[i];
                if (!CanReach(world, npc.Position, animal.Position))
                {
                    continue;
                }
                if (HuntingCoordinator.TryRequest(world, animal.Id, null, false, new List<SimEvent>(), out string reason, npc.Id))
                {
                    steps.Add(new PlanStep(PlanStepType.Hunt, animal.Position, animal.Id.Value, durationTicks: HuntingRules.PursuitTicks));
                    return true;
                }
            }
            if (candidates.Count > 0) { world.Hunting.Cooldowns[npc.Id.Value] = world.Tick + HuntingRules.RetryTicks; }
            return false;
        }

        private bool SellSurplus(WorldState world, NpcState npc, List<PlanStep> steps)
        {
            if (!CanReach(world, npc.Position, world.Resources.StockpilePosition))
            {
                return false;
            }
            for (int i = 0; i < SurvivalRules.MarketItemCount; i++)
            {
                ItemKind item = (ItemKind)i;
                int quantity = Math.Min(2, npc.Inventory.Count(item) - 1);
                if (quantity > 0 && world.Market.Inventory.Count(item) < 60 &&
                    world.Market.Treasury >= (long)quantity * world.Market.Price(item))
                {
                    Move(steps, world.Resources.StockpilePosition);
                    steps.Add(new PlanStep(PlanStepType.Sell, world.Resources.StockpilePosition,
                        item: item, quantity: quantity, unitPrice: world.Market.Price(item)));
                    return true;
                }
            }
            return false;
        }

        private static int CompareTargets(GridPos start, GridPos a, int idA, GridPos b, int idB)
        {
            int order = start.ManhattanDistance(a).CompareTo(start.ManhattanDistance(b));
            return order != 0 ? order : idA.CompareTo(idB);
        }

        private bool CanReach(WorldState world, GridPos start, GridPos target)
        {
            return start == target || _pathfinder.FindPath(world.Map, start, target).Found;
        }

        private static void Move(List<PlanStep> steps, GridPos target)
        {
            steps.Add(new PlanStep(PlanStepType.Move, target));
        }

        private static void AddDeposit(WorldState world, List<PlanStep> steps, ItemKind item, int quantity)
        {
            Move(steps, world.Resources.StockpilePosition);
            int sell = Math.Min(2, quantity);
            if (world.Market.Price(item) > 0 && world.Market.Inventory.Count(item) < 60 && world.Market.Treasury >= (long)sell * world.Market.Price(item))
            {
                steps.Add(new PlanStep(PlanStepType.Sell, world.Resources.StockpilePosition,
                    item: item, quantity: sell, unitPrice: world.Market.Price(item)));
                quantity -= sell;
            }
            if (quantity > 0)
            {
                steps.Add(new PlanStep(PlanStepType.Deposit, world.Resources.StockpilePosition, item: item, quantity: quantity));
            }
        }
    }
}
