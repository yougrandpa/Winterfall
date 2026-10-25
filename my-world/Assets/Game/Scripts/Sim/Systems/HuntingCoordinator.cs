using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    // Task authority lives in Sim. Views and commands never move actors directly.
    public static class HuntingCoordinator
    {
        private static readonly GridPathfinder Pathfinder = new GridPathfinder();
        public static bool CanPrepareWeapon(WorldState world, NpcState npc, ItemKind weapon) =>
            (weapon == ItemKind.Bow || weapon == ItemKind.Spear) && HuntingEquipment.CanPrepare(world, npc, weapon);
        public static bool Eligible(WorldState world, NpcState npc)
        {
            return npc.Survival.Life == LifeState.Active && npc.Needs.Health >= HuntingRules.JoinHealth && npc.Needs.Energy >= HuntingRules.JoinEnergy && npc.Needs.Food >= 3000 && npc.Needs.Water >= 3000 && npc.Survival.BodyTemperature >= 3600 && npc.Survival.Wetness < 5000 && (HuntingEquipment.CanPrepare(world, npc, ItemKind.Bow) || HuntingEquipment.CanPrepare(world, npc, ItemKind.Spear)) && npc.Inventory.TotalCount < npc.Inventory.Capacity && world.Hunting.ForNpc(npc.Id) == null && world.Hunting.ForReturn(npc.Id) == null && world.Tick >= world.Hunting.RetryAt(npc.Id) && (!npc.Plan.IsActive || npc.Plan.Goal != NpcGoalType.Rescue && npc.Plan.Goal != NpcGoalType.WarmUp && npc.Plan.Goal != NpcGoalType.Eat && npc.Plan.Goal != NpcGoalType.Drink && npc.Plan.Goal != NpcGoalType.Sleep);
        }

        public static bool TryRequest(WorldState world, EntityId targetId, IReadOnlyList<EntityId> requested, bool player, List<SimEvent> events, out string reason, EntityId preferredLeader = default(EntityId))
        {
            reason = "猎物或参与者不符合条件";
            AnimalState target = world.Ecology.Find(targetId.Value);
            if (!world.Ecology.CanHunt(target))
            {
                reason = "猎物已死亡或不在本地";
                return false;
            }

            if (world.Hunting.ForTarget(targetId) != null || !target.ReservedBy.IsNone)
            {
                reason = "猎物已有狩猎任务";
                return false;
            }

            var candidates = new List<NpcState>();
            var costs = new Dictionary<int, int>();
            if (requested != null && requested.Count > 0)
            {
                if (requested.Count > HuntingRules.MaxMembers)
                {
                    reason = "最多选择三名队员";
                    return false;
                }

                var ids = new HashSet<int>();
                foreach (EntityId id in requested)
                {
                    if (!ids.Add(id.Value) || !world.Entities.TryGetNpc(id, out NpcState npc) || !Eligible(world, npc))
                    {
                        reason = "队员重复、正在救援或状态不适合狩猎";
                        return false;
                    }

                    candidates.Add(npc);
                }
            }
            else
            {
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    if (Eligible(world, npc) && (player || !preferredLeader.IsNone || HuntingSystem.Perceived(world, npc, target)))
                    {
                        candidates.Add(npc);
                    }
                }
            }

            foreach (NpcState npc in candidates)
            {
                PathResult path = Pathfinder.FindPath(world.Map, npc.Position, target.Position, 512);
                if (!path.Found && requested != null && requested.Count > 0)
                {
                    reason = "指定队员无法抵达猎物";
                    return false;
                }

                costs[npc.Id.Value] = path.Found ? path.Cost : int.MaxValue;
            }

            candidates.RemoveAll(n => costs[n.Id.Value] == int.MaxValue);
            candidates.Sort((a, b) => Compare(world, a, b, costs, preferredLeader, target.Species));
            if (candidates.Count == 0)
            {
                reason = "没有可用且能够抵达的猎人";
                return false;
            }

            NpcState leader = candidates[0];
            var nearby = new List<NpcState>();
            foreach (NpcState npc in candidates)
            {
                PathResult path = Pathfinder.FindPath(world.Map, leader.Position, npc.Position, 512);
                if (!path.Found || path.Cost > HuntingRules.RecruitDistance)
                {
                    if (requested != null && requested.Count > 0)
                    {
                        reason = "指定队员距离队长太远";
                        return false;
                    }

                    continue;
                }

                nearby.Add(npc);
            }

            int required = HuntingRules.RequiredMembers(target.Species);
            if (nearby.Count < required)
            {
                reason = "可用人数不足，暂缓危险狩猎";
                return false;
            }

            bool automatic = requested == null || requested.Count == 0;
            var selected = automatic ? new List<NpcState> { leader } : nearby;
            Dictionary<int, HuntRole> roles = automatic ?
                SelectTeam(world, nearby, selected, 1, required - 1, target.Species) :
                HuntingEquipment.Assign(world, selected, target.Species);
            if (roles == null)
            {
                reason = "实际武器或箭矢不足，等待补充生产";
                return false;
            }
            if (HuntingSafety.VisibleWolves(world, selected, target.Position) > selected.Count)
            {
                reason = "附近狼群过多，放弃冒险";
                return false;
            }

            if (!HuntingPreparation.Budget(world, selected, roles, out List<ResourceReservation> supplies, out reason)) { return false; }
            if (world.Hunting.NextTaskId == int.MaxValue)
            {
                reason = "狩猎任务编号已耗尽";
                return false;
            }

            var task = new HuntTaskState(world.Hunting.NextTaskId++, target.Id, leader.Id, world.Tick)
            {PlayerRequested = player, RallyCell = world.Resources.StockpilePosition, LastSeenPosition = target.Position, PreviousSeenPosition = target.Position,
                LastSeenTick = selected.Exists(n => HuntingSystem.Perceived(world, n, target)) ? world.Tick : -1};
            selected.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            foreach (NpcState npc in selected)
            {
                world.Resources.Release(npc.Id);
                world.Ecology.Release(npc.Id);
                ProductionSystem.Release(world, npc);
                CombatSystem.CancelUnreleased(world, npc.Id);
                HuntRole role = roles[npc.Id.Value];
                task.MutableMembers.Add(new HuntMemberState(npc.Id, role));
                task.Returnees.Add(new HuntReturnState(npc.Id));
                npc.Plan.Start(NpcGoalType.Hunt, new List<PlanStep>{new PlanStep(PlanStepType.Hunt, target.Position, target.Id.Value, durationTicks: HuntingRules.PursuitTicks)});
                npc.Action.SetTimed(NpcActionType.Work, "Hunt: gather", task.RallyCell, task.DeadlineTick);
                npc.Plan.StepStarted = true;
                npc.LastDecision = new NpcDecisionTrace
                {
                    Goal = NpcGoalType.Hunt.ToString(),
                    ReasonCode = player ? "player_requested_hunt" : "cooperative_hunt"
                };
            }

            if (task.Members.Count > 1)
            {
                HuntMemberState flanker = task.MutableMembers.FindLast(m => m.Role == HuntRole.Archer);
                if (flanker != null && (target.Species == AnimalSpecies.Deer || task.MutableMembers.Exists(m => m != flanker && m.Role == HuntRole.Archer)))
                {
                    flanker.Role = HuntRole.Flanker;
                }
            }

            foreach (ResourceReservation supply in supplies)
            {
                if (!world.Resources.TryReserve(supply)) { throw new InvalidOperationException("Validated hunting supply changed inside a tick."); }
            }
            target.ReservedBy = leader.Id;
            world.Hunting.MutableTasks.Add(task);
            events.Add(new SimEvent(SimEventKind.PlanStarted, world.Tick, leader.Id, "狩猎队 #" + task.Id + " 集结，猎物 #" + target.Id.Value));
            reason = "狩猎队已集结";
            return true;
        }

        private static Dictionary<int, HuntRole> SelectTeam(WorldState world, List<NpcState> candidates,
            List<NpcState> selected, int start, int remaining, AnimalSpecies species)
        {
            if (remaining == 0) { return HuntingEquipment.Assign(world, selected, species); }
            // Keep the ranked leader and choose the first complete team that real supply can equip.
            for (int i = start; i <= candidates.Count - remaining; i++)
            {
                selected.Add(candidates[i]);
                Dictionary<int, HuntRole> roles = SelectTeam(world, candidates, selected, i + 1, remaining - 1, species);
                if (roles != null) { return roles; }
                selected.RemoveAt(selected.Count - 1);
            }
            return null;
        }

        private static int Compare(WorldState world, NpcState a, NpcState b, Dictionary<int, int> costs, EntityId preferred, AnimalSpecies species)
        {
            if (a.Id == preferred || b.Id == preferred)
            {
                return a.Id == b.Id ? 0 : a.Id == preferred ? -1 : 1;
            }

            int order = (a.Role == "Hunter" ? 0 : 1).CompareTo(b.Role == "Hunter" ? 0 : 1);
            if (order == 0) { order = HuntingEquipment.Rank(world, a, species).CompareTo(HuntingEquipment.Rank(world, b, species)); }
            if (order == 0)
            {
                order = costs[a.Id.Value].CompareTo(costs[b.Id.Value]);
            }

            if (order == 0)
            {
                order = b.HuntingSkill.CompareTo(a.HuntingSkill);
            }

            return order != 0 ? order : a.Id.Value.CompareTo(b.Id.Value);
        }

        public static bool Authorized(WorldState world, NpcState npc, AnimalState animal)
        {
            HuntTaskState task = world.Hunting.ForTarget(animal.Id);
            if (task == null) { return animal.ReservedBy == npc.Id; }
            HuntMemberState member = task.Member(npc.Id);
            if (member == null) { return false; }
            return task.Stage == HuntStage.Engaging || task.Stage == HuntStage.Approaching ||
                task.Stage == HuntStage.Retreating && member.Role == HuntRole.Spear && world.Tick < task.DeadlineTick &&
                HuntingSafety.CanCover(world, npc);
        }

        public static void Update(WorldState world, List<SimEvent> events)
        {
            foreach (HuntTaskState task in world.Hunting.MutableTasks.ToArray())
            {
                foreach (HuntReturnState traveler in task.Returnees)
                {
                    if (!world.Entities.TryGetNpc(traveler.NpcId, out NpcState person)) { continue; }
                    traveler.Dead = person.Survival.Life == LifeState.Dead;
                    if (task.Member(person.Id) == null && AtVillage(world, person) && !traveler.Returned) { traveler.Returned = true; traveler.ReturnedTick = world.Tick; traveler.ReturnedCell = person.Position; }
                }
                if (!task.PlayerRequested && task.StartedTick + 1 == world.Tick)
                {
                    events.Add(new SimEvent(SimEventKind.PlanStarted, world.Tick, task.LeaderId, "狩猎队 #" + task.Id + " 自主集结"));
                }

                foreach (HuntMemberState member in task.MutableMembers.ToArray())
                {
                    if (!world.Entities.TryGetNpc(member.NpcId, out NpcState npc) || npc.Survival.Life != LifeState.Active || !npc.Plan.IsActive || npc.Plan.Goal != NpcGoalType.Hunt)
                    {
                        Retreat(world, task, "队员倒地或计划中断", events);
                        Detach(world, member.NpcId);
                    }
                }

                if (task.Members.Count == 0)
                {
                    if (task.Returnees.TrueForAll(r => r.Returned || r.Dead)) { Finish(world, task); }
                    continue;
                }

                if (task.Stage == HuntStage.Returning || task.Stage == HuntStage.Retreating)
                {
                    continue;
                }

                if (task.Stage == HuntStage.Gathering && world.Tick >= task.DeadlineTick)
                {
                    Retreat(world, task, "集结超时", events);
                    continue;
                }
                string unsafeReason = HuntingSafety.Check(world, task);
                if (unsafeReason != null)
                {
                    Retreat(world, task, unsafeReason, events);
                    continue;
                }

                CarcassState carcass = world.Ecology.FindCarcass(task.TargetId.Value);
                if (carcass != null)
                {
                    task.Stage = HuntStage.Retrieving;
                    continue;
                }

                AnimalState target = world.Ecology.Find(task.TargetId.Value);
                if (target == null)
                {
                    Retreat(world, task, "猎物已离开", events);
                    continue;
                }

                if (target.Health <= 0)
                {
                    continue;
                } // ResolveDeaths publishes the unique carcass later this tick.

                if (target.Region != -1 || task.Members.Count < HuntingRules.RequiredMembers(target.Species))
                {
                    Retreat(world, task, "猎物迁出或队伍人数不足", events);
                    continue;
                }

                bool seen = false;
                foreach (HuntMemberState member in task.Members)
                {
                    world.Entities.TryGetNpc(member.NpcId, out NpcState npc);
                    if (HuntingSystem.Perceived(world, npc, target))
                    {
                        seen = true;
                        break;
                    }
                }

                if (seen)
                {
                    task.PreviousSeenPosition = task.LastSeenPosition;
                    task.LastSeenPosition = target.Position;
                    task.LastSeenTick = world.Tick;
                }

                if (task.Stage != HuntStage.Gathering && world.Tick >= task.DeadlineTick)
                {
                    Retreat(world, task, "追猎超时", events);
                }
                else if (task.Stage != HuntStage.Gathering && task.LastSeenTick >= 0 &&
                    world.Tick - task.LastSeenTick >= HuntingRules.LostTicks)
                {
                    Retreat(world, task, "失去目击超过时限", events);
                }

                if (task.Stage != HuntStage.Retreating) { target.ReservedBy = task.LeaderId; }
            }
        }

        public static bool UpdateMember(WorldState world, NpcState npc, List<SimEvent> events)
        {
            HuntTaskState task = world.Hunting.ForNpc(npc.Id);
            if (task == null)
            {
                HuntTaskState returning = world.Hunting.ForReturn(npc.Id);
                if (returning == null || HuntingSafety.NeedsRetreat(world, npc) || npc.Plan.IsActive &&
                    (npc.Plan.Goal == NpcGoalType.Rescue || npc.Plan.Goal == NpcGoalType.WarmUp || npc.Plan.Goal == NpcGoalType.Eat || npc.Plan.Goal == NpcGoalType.Drink || npc.Plan.Goal == NpcGoalType.Sleep)) { return false; }
                world.Resources.Release(npc.Id);
                ProductionSystem.Release(world, npc);
                CombatSystem.CancelUnreleased(world, npc.Id);
                npc.Plan.Complete();
                npc.Survival.HuntMotion = HuntMotion.Walk;
                npc.Action.Label = "Hunt: return after survival";
                HuntingNavigation.Move(world, npc, npc.Home);
                return true;
            }

            HuntMemberState member = task.Member(npc.Id);
            HuntingLocomotion.Refresh(world, npc);
            if (task.Stage != HuntStage.Gathering && !HuntingSafety.VisibleDanger(world, npc)) { HuntingPreparation.EatDrink(world, npc); }
            if (task.Stage != HuntStage.Gathering && HuntingSafety.NeedsRetreat(world, npc) || npc.Needs.Health < HuntingRules.RetreatHealth || npc.Needs.Energy < HuntingRules.RetreatEnergy || npc.Survival.IsCold ||
                task.Stage == HuntStage.Gathering && npc.Position.ManhattanDistance(task.RallyCell) > 3 && (npc.Needs.Food < 1500 || npc.Needs.Water < 1800 || npc.Survival.BodyTemperature < 3600))
            {
                Retreat(world, task, HuntingSafety.Reason(world, npc), events);
            }

            AnimalState prey = world.Ecology.Find(task.TargetId.Value);
            if (HuntingSafety.Dodge(world, npc)) { return true; }
            if (task.Stage == HuntStage.Retreating || task.Stage == HuntStage.Returning)
            {
                if (npc.Needs.Food < 1500 || npc.Needs.Water < 1800 || npc.Needs.Energy < HuntingRules.RetreatEnergy ||
                    HuntingSafety.ThermalRisk(world, npc))
                {
                    // Urgent survival must not wait for arrival at a home blocked by other actors.
                    Detach(world, npc.Id);
                    npc.Plan.Fail(task.Reason);
                    npc.Action.SetIdle("Hunt: retreat", world.Tick);
                    return false;
                }
                bool canCover = task.Stage == HuntStage.Retreating && member.Role == HuntRole.Spear &&
                    world.Tick < task.DeadlineTick && HuntingSafety.CanCover(world, npc);
                if (!canCover) { CombatSystem.CancelUnreleased(world, npc.Id); }
                // Let the existing rescue planner acquire medicine and transport, rather than duplicating medical authority.
                if (task.Stage == HuntStage.Retreating)
                {
                    foreach (NpcState wounded in world.Entities.Npcs)
                    {
                        if (wounded.Survival.Life == LifeState.Downed && wounded.Survival.Rescuer.IsNone && npc.Needs.Health >= 6000 && npc.Position.ManhattanDistance(wounded.Position) <= 12 && (npc.Inventory.Count(ItemKind.Medicine) > 0 || world.Resources.AvailableStock(ItemKind.Medicine) > 0))
                        {
                            Detach(world, npc.Id);
                            npc.Plan.Fail("hunt_rescue");
                            npc.Action.SetIdle("Rescue: prepare", world.Tick);
                            return false;
                        }
                    }
                }

                if (canCover && prey != null && prey.Health > 0 && npc.Position.ManhattanDistance(prey.Position) <= 1 &&
                    (world.Combat.GetAttack(npc.Id)?.Phase == CombatAttackPhase.Preparing ||
                        world.Combat.GetAttack(npc.Id) == null && CombatSystem.TryStartHunt(world, npc, prey, events)))
                {
                    npc.Action.Label = "Hunt: cover";
                    return true;
                }

                GridPos destination = task.Stage == HuntStage.Returning ? world.Resources.StockpilePosition : npc.Home;
                if (!npc.Survival.RecoveringStamina) { npc.Survival.HuntMotion = HuntMotion.Walk; }
                npc.Action.Label = task.Stage == HuntStage.Returning ? "Hunt: return" : "Hunt: retreat";
                if (npc.Position == destination || task.Stage == HuntStage.Returning && npc.Position.ManhattanDistance(destination) <= 1)
                {
                    if (task.Stage == HuntStage.Returning)
                    {
                        foreach (ItemKind item in new[]{ItemKind.RawMeat, ItemKind.Hide, ItemKind.Fur, ItemKind.Feather})
                        {
                            int amount = Math.Min(npc.Inventory.Count(item), world.Resources.Stockpile.Capacity - world.Resources.Stockpile.TotalCount);
                            if (amount > 0)
                            {
                                npc.Inventory.TryRemove(item, amount);
                                world.Resources.Stockpile.TryAdd(item, amount);
                                task.Delivered[(int)item] += amount;
                            }
                        }
                    }

                    bool returned = task.Stage == HuntStage.Returning;
                    if (returned && npc.Inventory.Count(ItemKind.RawMeat) + npc.Inventory.Count(ItemKind.Hide) + npc.Inventory.Count(ItemKind.Fur) + npc.Inventory.Count(ItemKind.Feather) > 0)
                    { task.Reason = "仓库已满，未入库猎获保留在队员背包"; }
                    Detach(world, npc.Id);
                    if (returned)
                    {
                        npc.Plan.Complete();
                        events.Add(new SimEvent(SimEventKind.NpcActionCompleted, world.Tick, npc.Id, npc.Name + " 已运回猎获，狩猎完成。"));
                    }
                    else { npc.Plan.Fail(task.Reason); }
                    npc.Action.SetIdle(returned ? "Hunt: complete" : "Hunt: retreat", world.Tick);
                    return true; // Normal planner deposits actual carried goods next tick.
                }

                if (!HuntingNavigation.Move(world, npc, destination, task.Stage == HuntStage.Returning ? 1 : 0))
                {
                    Detach(world, npc.Id);
                    npc.Plan.Fail("hunt_return_unreachable");
                    npc.Action.SetIdle("Hunt: shelter", world.Tick);
                }

                return true;
            }

            if (task.Stage == HuntStage.Gathering)
            {
                npc.Action.Label = "Hunt: gather";
                if (!HuntingPreparation.Prepare(world, npc, member, out bool unavailable))
                {
                    if (unavailable)
                    {
                        Retreat(world, task, "装备补给不足或不可达，交回生产流程", events);
                    }
                    return true;
                }

                if (npc.Position.ManhattanDistance(task.RallyCell) > 2)
                {
                    if (!HuntingNavigation.Move(world, npc, task.RallyCell, 2)) { Retreat(world, task, "集结路线不可达", events); }
                    return true;
                }

                bool ready = true;
                foreach (HuntMemberState other in task.Members)
                {
                    world.Entities.TryGetNpc(other.NpcId, out NpcState partner);
                    ready &= partner.Position.ManhattanDistance(task.RallyCell) <= 2 && HuntingPreparation.Ready(world, partner, other);
                }

                if (ready && prey != null)
                {
                    foreach (HuntMemberState other in task.Members)
                    {
                        world.Entities.TryGetNpc(other.NpcId, out NpcState traveler);
                        PathResult route = Pathfinder.FindPath(world.Map, traveler.Position, task.LastSeenPosition);
                        if (!route.Found || !ThermalSystem.CanHuntTrip(world, traveler, route.Cost))
                        { Retreat(world, task, "天气或保暖不足以保障往返", events); return true; }
                    }
                    foreach (HuntMemberState prepared in task.Members) { world.Resources.Release(prepared.NpcId); }
                    foreach (HuntReturnState traveler in task.Returnees) { traveler.Returned = false; traveler.ReturnedTick = -1; traveler.ReturnedCell = GridPos.Invalid; }
                    task.Stage = HuntStage.Approaching;
                    task.DeadlineTick = world.Tick + HuntingRules.PursuitTicks;
                    bool seenNow = false;
                    foreach (HuntMemberState other in task.Members)
                    {
                        world.Entities.TryGetNpc(other.NpcId, out NpcState observer);
                        seenNow |= prey != null && HuntingSystem.Perceived(world, observer, prey);
                    }
                    // Preparation does not spend the time budget for losing sight during pursuit.
                    task.LastSeenTick = seenNow ? world.Tick : -1;
                }
                else if (world.Tick >= task.DeadlineTick)
                {
                    Retreat(world, task, "集结超时", events);
                }

                return true;
            }

            if (task.Stage == HuntStage.Retrieving)
            {
                CarcassState carcass = world.Ecology.FindCarcass(task.TargetId.Value);
                if (carcass == null || carcass.Region != -1)
                {
                    Retreat(world, task, "猎获不可回收", events);
                    return true;
                }

                if (carcass.HarvestCount + carcass.Inventory.TotalCount == 0 || !task.MutableMembers.Exists(m => world.Entities.TryGetNpc(m.NpcId, out NpcState p) && p.Inventory.TotalCount < p.Inventory.Capacity))
                {
                    task.Stage = HuntStage.Returning;
                    carcass.ReservedBy = EntityId.None;
                    return true;
                }

                if (!carcass.ReservedBy.IsNone && task.Member(carcass.ReservedBy) == null)
                {
                    return true;
                }

                if (carcass.ReservedBy.IsNone)
                {
                    foreach (HuntMemberState other in task.Members)
                    {
                        world.Entities.TryGetNpc(other.NpcId, out NpcState collector);
                        if (collector.Inventory.TotalCount < collector.Inventory.Capacity)
                        {
                            carcass.ReservedBy = collector.Id;
                            break;
                        }
                    }
                }

                if (carcass.ReservedBy != npc.Id)
                {
                    npc.Action.Label = "Hunt: cover";
                    return true;
                }

                npc.Action.Label = "Hunt: retrieve";
                if (npc.Position.ManhattanDistance(carcass.Position) > 1)
                {
                    if (!HuntingNavigation.Move(world, npc, carcass.Position, 1))
                    {
                        Retreat(world, task, "猎获不可达", events);
                    }
                }
                else
                {
                    npc.Survival.HuntDropId = carcass.Id.Value;
                } // Commit only after combat and HealthSystem.

                return true;
            }

            if (prey == null || !world.Ecology.CanHunt(prey))
            {
                return true;
            }

            if (world.Combat.GetAttack(npc.Id) != null)
            {
                npc.Action.Label = "Hunt: attack";
                return true;
            }

            bool perceived = HuntingSystem.Perceived(world, npc, prey);
            if (!npc.Survival.RecoveringStamina)
            {
                npc.Survival.HuntMotion = perceived && (prey.Fleeing || prey.Encounter.Phase == EncounterPhase.Defending) ? HuntMotion.Pursuit :
                    npc.Position.ManhattanDistance(task.LastSeenPosition) <= 8 ? HuntMotion.Sneak : HuntMotion.Walk;
            }
            if (!perceived && task.LastSeenTick != world.Tick)
            {
                // Visit the last observation itself: stopping at bow range can leave
                // an unseen animal behind forest forever. Search only known terrain.
                GridPos destination = task.LastSeenPosition;
                if (npc.Position == destination || member.WorkCell.IsValid && member.WorkCell.ManhattanDistance(destination) <= 2)
                {
                    if (!member.WorkCell.IsValid || npc.Position == member.WorkCell || member.WorkCell.ManhattanDistance(destination) > 2)
                    {
                        int offset = (int)((world.Tick - task.StartedTick) / 3 + npc.Id.Value) % 4;
                        for (int i = 0; i < 4; i++)
                        {
                            int direction = (offset + i) % 4;
                            GridPos candidate = destination.Offset(direction == 1 ? 2 : direction == 3 ? -2 : 0, direction == 0 ? 2 : direction == 2 ? -2 : 0);
                            if (world.Map.IsWalkable(candidate) && Pathfinder.FindPath(world.Map, npc.Position, candidate, 256).Found)
                            { member.WorkCell = candidate; break; }
                        }
                    }
                    if (member.WorkCell.IsValid) { destination = member.WorkCell; }
                }
                else { member.WorkCell = destination; }
                npc.Action.Label = npc.Survival.RecoveringStamina ? "Hunt: recover stamina" : "Hunt: track";
                if (!HuntingNavigation.Move(world, npc, destination)) { Retreat(world, task, "最后目击位置不可达", events); }
                return true;
            }
            bool intercepting = member.Role == HuntRole.Flanker && npc.Position.ManhattanDistance(prey.Position) > 1 &&
                (!member.WorkCell.IsValid || npc.Position != member.WorkCell);
            if (!npc.Survival.RecoveringStamina && !intercepting && perceived && HuntingSystem.CanStrike(world, npc, prey) && (member.Role != HuntRole.Spear || npc.Position.ManhattanDistance(prey.Position) <= 1))
            {
                task.Stage = HuntStage.Engaging;
                npc.Action.Label = "Hunt: attack";
                CombatSystem.TryStartHunt(world, npc, prey, events);
                return true;
            }

            member.WorkCell = ChooseSlot(world, npc, member, task);
            npc.Action.Label = npc.Survival.RecoveringStamina ? "Hunt: recover stamina" : npc.Survival.HuntMotion == HuntMotion.Sneak ? "Hunt: sneak" : member.Role == HuntRole.Flanker ? "Hunt: flank" : "Hunt: track";
            if (!member.WorkCell.IsValid)
            {
                if (!Pathfinder.FindPath(world.Map, npc.Position, task.LastSeenPosition).Found)
                {
                    Retreat(world, task, "接近目标不可达", events);
                }
                else { npc.Action.Label = "Hunt: wait for lane"; }
                return true;
            }
            if (!HuntingNavigation.Move(world, npc, member.WorkCell))
            {
                Retreat(world, task, "接近或退路不可达", events);
            }

            return true;
        }

        private static GridPos ChooseSlot(WorldState world, NpcState npc, HuntMemberState member, HuntTaskState task)
        {
            GridPos center = task.LastSeenPosition;
            int dx = Math.Sign(center.X - task.PreviousSeenPosition.X), dy = Math.Sign(center.Y - task.PreviousSeenPosition.Y);
            if (member.Role == HuntRole.Flanker && dx == 0 && dy == 0)
            {
                dx = Math.Sign(center.X - task.RallyCell.X);
                dy = Math.Sign(center.Y - task.RallyCell.Y);
            }
            GridPos predicted = member.Role == HuntRole.Flanker ? center.Offset(dx * 2, dy * 2) : center;
            var candidates = new List<GridPos>();
            int range = member.Role == HuntRole.Spear ? 1 : 3;
            for (int y = -range; y <= range; y++)
                for (int x = -range; x <= range; x++)
                {
                    int distance = Math.Abs(x) + Math.Abs(y);
                    if (distance < 1 || distance > range)
                    {
                        continue;
                    }

                    GridPos cell = predicted.Offset(x, y);
                    if (!world.Map.IsWalkable(cell) || HuntingSafety.Occupied(world, npc, cell) ||
                        task.MutableMembers.Exists(m => m != member && m.WorkCell == cell))
                    {
                        continue;
                    }

                    if (cell == center || member.Role != HuntRole.Spear && cell.ManhattanDistance(center) > 4) { continue; }

                    if (member.Role != HuntRole.Spear && !HuntingSystem.ClearShot(world, npc.Id, cell, center))
                    {
                        continue;
                    }

                    candidates.Add(cell);
                }

            bool ahead = member.Role != HuntRole.Flanker ||
                (member.WorkCell.X - center.X) * dx + (member.WorkCell.Y - center.Y) * dy > 0 ||
                !candidates.Exists(c => (c.X - center.X) * dx + (c.Y - center.Y) * dy > 0);
            if (ahead && candidates.Contains(member.WorkCell) &&
                Pathfinder.FindPath(world.Map, npc.Position, member.WorkCell, 256).Found)
            {
                return member.WorkCell;
            }

            candidates.Sort((a, b) =>
            {
                int ca = npc.Position.ManhattanDistance(a) * 4 + (member.Role == HuntRole.Flanker ? a.ManhattanDistance(predicted) : Math.Abs(a.ManhattanDistance(center) - range));
                int cb = npc.Position.ManhattanDistance(b) * 4 + (member.Role == HuntRole.Flanker ? b.ManhattanDistance(predicted) : Math.Abs(b.ManhattanDistance(center) - range));
                if (member.Role == HuntRole.Flanker)
                {
                    // Prefer a reachable intercept ahead of the last observed heading over following the driver.
                    if ((a.X - center.X) * dx + (a.Y - center.Y) * dy <= 0) { ca += 10000; }
                    if ((b.X - center.X) * dx + (b.Y - center.Y) * dy <= 0) { cb += 10000; }
                }
                int order = ca.CompareTo(cb);
                return order != 0 ? order : (a.Y * world.Map.Width + a.X).CompareTo(b.Y * world.Map.Width + b.X);
            }

            );
            foreach (GridPos cell in candidates)
            {
                if (Pathfinder.FindPath(world.Map, npc.Position, cell, 256).Found)
                {
                    return cell;
                }
            }

            return GridPos.Invalid;
        }

        public static void CommitRetrieval(WorldState world, List<SimEvent> events)
        {
            foreach (HuntTaskState task in world.Hunting.MutableTasks.ToArray())
                foreach (HuntMemberState member in task.MutableMembers.ToArray())
                {
                    if (!world.Entities.TryGetNpc(member.NpcId, out NpcState npc) || npc.Survival.HuntDropId == 0)
                    {
                        continue;
                    }

                    CarcassState carcass = world.Ecology.FindCarcass(npc.Survival.HuntDropId);
                    if (npc.Survival.Life == LifeState.Active && task.Stage == HuntStage.Retrieving && carcass != null && carcass.ReservedBy == npc.Id)
                    {
                        world.Ecology.HarvestCarcass(world, npc, carcass, events);
                        CombatSystem.RecoverArrows(world, npc, carcass.Position);
                    }

                    npc.Survival.HuntDropId = 0;
                    if (carcass != null && carcass.ReservedBy == npc.Id)
                    {
                        carcass.ReservedBy = EntityId.None;
                    }
                }
        }

        public static void Retreat(WorldState world, HuntTaskState task, string reason, List<SimEvent> events)
        {
            if (task.Stage == HuntStage.Retreating)
            {
                return;
            }

            task.Stage = HuntStage.Retreating;
            task.Reason = reason;
            task.DeadlineTick = world.Tick + HuntingRules.CoverTicks;
            foreach (HuntMemberState member in task.Members)
            {
                world.Resources.Release(member.NpcId);
                CombatSystem.CancelUnreleased(world, member.NpcId);
            }

            AnimalState target = world.Ecology.Find(task.TargetId.Value);
            if (target != null && target.ReservedBy == task.LeaderId)
            {
                target.ReservedBy = EntityId.None;
            }

            CarcassState carcass = world.Ecology.FindCarcass(task.TargetId.Value);
            if (carcass != null && task.Member(carcass.ReservedBy) != null)
            {
                carcass.ReservedBy = EntityId.None;
            }

            events.Add(new SimEvent(SimEventKind.PlanInterrupted, world.Tick, task.LeaderId, "狩猎队 #" + task.Id + " 撤退：" + reason));
        }

        public static void Detach(WorldState world, EntityId id)
        {
            HuntTaskState task = world.Hunting.ForNpc(id);
            if (task == null)
            {
                return;
            }

            world.Resources.Release(id);
            CombatSystem.CancelUnreleased(world, id);
            task.MutableMembers.RemoveAll(m => m.NpcId == id);
            if (world.Entities.TryGetNpc(id, out NpcState traveler))
            {
                HuntReturnState arrival = task.Returnees.Find(r => r.NpcId == id);
                if (arrival != null) { arrival.Returned = AtVillage(world, traveler); if (arrival.Returned) { arrival.ReturnedTick = world.Tick; arrival.ReturnedCell = traveler.Position; } arrival.Dead = traveler.Survival.Life == LifeState.Dead; }
                traveler.Survival.HuntMotion = HuntMotion.Walk;
            }
            world.Hunting.Cooldowns[id.Value] = world.Tick + HuntingRules.RetryTicks;
            if (world.Entities.TryGetNpc(id, out NpcState npc))
            {
                npc.Survival.HuntDropId = 0;
            }

            world.Ecology.Release(id);
            if (task.Members.Count == 0)
            {
                if (task.Stage != HuntStage.Returning) { task.Stage = HuntStage.Retreating; }
                if (task.Returnees.TrueForAll(r => r.Returned || r.Dead)) { Finish(world, task); }
                return;
            }

            if (task.LeaderId == id)
            {
                task.LeaderId = task.Members[0].NpcId;
                AnimalState animal = world.Ecology.Find(task.TargetId.Value);
                if (animal != null && animal.Region == -1 && animal.Health > 0 && task.Stage != HuntStage.Retreating && task.Stage != HuntStage.Returning)
                {
                    animal.ReservedBy = task.LeaderId;
                }
            }
        }

        private static bool AtVillage(WorldState world, NpcState npc) => npc.Survival.Life != LifeState.Dead &&
            (npc.Position == npc.Home || npc.Position.ManhattanDistance(world.Resources.StockpilePosition) <= 2);

        private static void Finish(WorldState world, HuntTaskState task)
        {
            // Detach may finish the last member while Update is still visiting its task copy.
            if (!world.Hunting.MutableTasks.Remove(task)) { return; }
            foreach (HuntMemberState member in task.Members) { world.Resources.Release(member.NpcId); }
            world.Ecology.Release(task.LeaderId);
            int delivered = 0;
            foreach (int amount in task.Delivered) { delivered += amount; }
            bool success = task.Stage == HuntStage.Returning && delivered > 0 && !task.Returnees.Exists(r => r.Dead);
            string result = success ? (string.IsNullOrEmpty(task.Reason) ? "猎获已入库，全队返村" : "部分猎获已入库，全队返村 · " + task.Reason) : string.IsNullOrEmpty(task.Reason) ? "未带回猎获，队伍已返村" : task.Reason + " · 返村结束";
            var participants = new List<EntityId>();
            foreach (HuntReturnState person in task.Returnees) { participants.Add(person.NpcId); }
            var returnTicks = new List<long>(); var returnCells = new List<GridPos>();
            foreach (HuntReturnState person in task.Returnees) { returnTicks.Add(person.ReturnedTick); returnCells.Add(person.ReturnedCell); }
            world.Hunting.MutableResults.Add(new HuntResultState(task.Id, task.TargetId, world.Tick, success, result, task.Delivered, participants, returnTicks, returnCells));
            if (world.Hunting.MutableResults.Count > 16) { world.Hunting.MutableResults.RemoveAt(0); }
        }
    }
}
