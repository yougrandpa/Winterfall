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
        public static bool Eligible(WorldState world, NpcState npc)
        {
            return npc.Survival.Life == LifeState.Active && npc.Needs.Health >= HuntingRules.JoinHealth && npc.Needs.Energy >= HuntingRules.JoinEnergy && npc.Needs.Food >= 3000 && npc.Needs.Water >= 3000 && !ThermalSystem.ShouldSeekWarmth(world, npc) && !npc.Survival.IsCold && npc.HasHuntingTool && npc.Inventory.TotalCount < npc.Inventory.Capacity && world.Hunting.ForNpc(npc.Id) == null && world.Tick >= world.Hunting.RetryAt(npc.Id) && (!npc.Plan.IsActive || npc.Plan.Goal != NpcGoalType.Rescue && npc.Plan.Goal != NpcGoalType.WarmUp && npc.Plan.Goal != NpcGoalType.Eat && npc.Plan.Goal != NpcGoalType.Drink && npc.Plan.Goal != NpcGoalType.Sleep);
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
            candidates.Sort((a, b) => Compare(a, b, costs, preferredLeader));
            if (candidates.Count == 0)
            {
                reason = "没有可用且能够抵达的猎人";
                return false;
            }

            NpcState leader = candidates[0];
            var selected = new List<NpcState>();
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

                selected.Add(npc);
                if ((requested == null || requested.Count == 0) && selected.Count == HuntingRules.RequiredMembers(target.Species))
                {
                    break;
                }
            }

            if ((requested == null || requested.Count == 0) && HuntingRules.NeedsSpear(target.Species) && !selected.Exists(n => n.Survival.Durability(ItemKind.Spear) > 0))
            {
                foreach (NpcState candidate in candidates)
                {
                    if (selected.Contains(candidate) || candidate.Survival.Durability(ItemKind.Spear) == 0)
                    {
                        continue;
                    }

                    PathResult route = Pathfinder.FindPath(world.Map, leader.Position, candidate.Position, 512);
                    if (!route.Found || route.Cost > HuntingRules.RecruitDistance)
                    {
                        continue;
                    }

                    if (selected.Count >= HuntingRules.RequiredMembers(target.Species))
                    {
                        selected.RemoveAt(selected.Count - 1);
                    }

                    selected.Add(candidate);
                    break;
                }
            }

            if (selected.Count < HuntingRules.RequiredMembers(target.Species))
            {
                reason = "可用人数不足，暂缓危险狩猎";
                return false;
            }

            // A spear carried in a real bag/stockpile may be equipped during gathering.
            bool spear = selected.Exists(n => n.Survival.Durability(ItemKind.Spear) > 0 || n.Inventory.Count(ItemKind.Spear) > 0);
            if (HuntingRules.NeedsSpear(target.Species) && !spear && world.Resources.AvailableStock(ItemKind.Spear) == 0)
            {
                reason = "需要一名持矛队员接应";
                return false;
            }

            int wolves = 0;
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Species == AnimalSpecies.Wolf && animal.Region == -1 && animal.Health > 0 && animal.Position.ManhattanDistance(target.Position) <= 6 && HuntingSystem.Visible(world, target.Position, animal.Position))
                {
                    wolves++;
                }
            }

            if (wolves > selected.Count)
            {
                reason = "附近狼群过多，放弃冒险";
                return false;
            }

            if (world.Hunting.NextTaskId == int.MaxValue)
            {
                reason = "狩猎任务编号已耗尽";
                return false;
            }

            var task = new HuntTaskState(world.Hunting.NextTaskId++, target.Id, leader.Id, world.Tick)
            {PlayerRequested = player, RallyCell = leader.Position, LastSeenPosition = target.Position, PreviousSeenPosition = target.Position, LastSeenTick = world.Tick};
            selected.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            bool hasSpearRole = false;
            foreach (NpcState npc in selected)
            {
                world.Resources.Release(npc.Id);
                world.Ecology.Release(npc.Id);
                ProductionSystem.Release(world, npc);
                CombatSystem.CancelUnreleased(world, npc.Id);
                HuntRole role = npc.Survival.Durability(ItemKind.Spear) > 0 ? HuntRole.Spear : HuntRole.Archer;
                if (HuntingRules.NeedsSpear(target.Species) && !hasSpearRole && (npc.Survival.Durability(ItemKind.Spear) > 0 || npc.Inventory.Count(ItemKind.Spear) > 0 || !spear))
                {
                    role = HuntRole.Spear;
                    hasSpearRole = true;
                }

                task.MutableMembers.Add(new HuntMemberState(npc.Id, role));
                npc.Plan.Start(NpcGoalType.Hunt, new List<PlanStep>{new PlanStep(PlanStepType.Hunt, target.Position, target.Id.Value, durationTicks: HuntingRules.PursuitTicks)});
                npc.Action.SetTimed(NpcActionType.Work, "Hunt: gather", task.RallyCell, task.DeadlineTick);
                npc.Plan.StepStarted = true;
            }

            if (task.Members.Count > 1)
            {
                HuntMemberState flanker = task.MutableMembers.FindLast(m => m.Role == HuntRole.Archer);
                if (flanker != null && task.MutableMembers.Exists(m => m != flanker && m.Role == HuntRole.Archer))
                {
                    flanker.Role = HuntRole.Flanker;
                }
            }

            target.ReservedBy = leader.Id;
            world.Hunting.MutableTasks.Add(task);
            events.Add(new SimEvent(SimEventKind.PlanStarted, world.Tick, leader.Id, "狩猎队 #" + task.Id + " 集结，猎物 #" + target.Id.Value));
            reason = "狩猎队已集结";
            return true;
        }

        private static int Compare(NpcState a, NpcState b, Dictionary<int, int> costs, EntityId preferred)
        {
            if (a.Id == preferred || b.Id == preferred)
            {
                return a.Id == b.Id ? 0 : a.Id == preferred ? -1 : 1;
            }

            int order = (a.Role == "Hunter" ? 0 : 1).CompareTo(b.Role == "Hunter" ? 0 : 1);
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
            return task == null ? animal.ReservedBy == npc.Id : task.Member(npc.Id) != null && (task.Stage == HuntStage.Engaging || task.Stage == HuntStage.Approaching || task.Stage == HuntStage.Retreating);
        }

        public static void Update(WorldState world, List<SimEvent> events)
        {
            foreach (HuntTaskState task in world.Hunting.MutableTasks.ToArray())
            {
                if (!task.PlayerRequested && task.StartedTick + 1 == world.Tick)
                {
                    events.Add(new SimEvent(SimEventKind.PlanStarted, world.Tick, task.LeaderId, "狩猎队 #" + task.Id + " 自主集结"));
                }

                foreach (HuntMemberState member in task.MutableMembers.ToArray())
                {
                    if (!world.Entities.TryGetNpc(member.NpcId, out NpcState npc) || npc.Survival.Life != LifeState.Active || !npc.Plan.IsActive || npc.Plan.Goal != NpcGoalType.Hunt)
                    {
                        Detach(world, member.NpcId);
                    }
                }

                if (task.Members.Count == 0)
                {
                    Finish(world, task);
                    continue;
                }

                if (task.Stage == HuntStage.Returning || task.Stage == HuntStage.Retreating)
                {
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

                if (task.Stage != HuntStage.Gathering && (world.Tick >= task.DeadlineTick || world.Tick - task.LastSeenTick >= HuntingRules.LostTicks))
                {
                    Retreat(world, task, "追猎超时或失去目击", events);
                }

                target.ReservedBy = task.LeaderId;
            }
        }

        public static bool UpdateMember(WorldState world, NpcState npc, List<SimEvent> events)
        {
            HuntTaskState task = world.Hunting.ForNpc(npc.Id);
            if (task == null)
            {
                return false;
            }

            HuntMemberState member = task.Member(npc.Id);
            if (npc.Needs.Health < HuntingRules.RetreatHealth || npc.Needs.Energy < HuntingRules.RetreatEnergy || npc.Needs.Food < 1500 || npc.Needs.Water < 1800 || ThermalSystem.ShouldSeekWarmth(world, npc))
            {
                Retreat(world, task, "队员受伤、疲惫或需求告急", events);
            }

            AnimalState prey = world.Ecology.Find(task.TargetId.Value);
            if (task.Stage == HuntStage.Retreating || task.Stage == HuntStage.Returning)
            {
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

                if (task.Stage == HuntStage.Retreating && world.Tick < task.DeadlineTick && member.Role == HuntRole.Spear && npc.Needs.Health >= HuntingRules.JoinHealth && prey != null && prey.Health > 0 && npc.Position.ManhattanDistance(prey.Position) <= 1 && world.Combat.GetAttack(npc.Id) == null && CombatSystem.TryStartHunt(world, npc, prey, events))
                {
                    npc.Action.Label = "Hunt: cover";
                    return true;
                }

                GridPos destination = task.Stage == HuntStage.Returning ? world.Resources.StockpilePosition : npc.Home;
                npc.Action.Label = task.Stage == HuntStage.Returning ? "Hunt: return" : "Hunt: retreat";
                if (npc.Position == destination)
                {
                    if (task.Stage == HuntStage.Returning)
                    {
                        foreach (ItemKind item in new[]{ItemKind.RawMeat, ItemKind.Hide})
                        {
                            int amount = Math.Min(npc.Inventory.Count(item), world.Resources.Stockpile.Capacity - world.Resources.Stockpile.TotalCount);
                            if (amount > 0)
                            {
                                npc.Inventory.TryRemove(item, amount);
                                world.Resources.Stockpile.TryAdd(item, amount);
                            }
                        }
                    }

                    Detach(world, npc.Id);
                    npc.Plan.Fail("hunt_returned");
                    npc.Action.SetIdle("Hunt: complete", world.Tick);
                    return true; // Normal planner deposits actual carried goods next tick.
                }

                if (!Move(world, npc, destination, task))
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
                if (!PrepareEquipment(world, npc, member, task))
                {
                    if (world.Tick >= task.DeadlineTick)
                    {
                        Retreat(world, task, "装备或集结超时", events);
                    }

                    return true;
                }

                if (npc.Position.ManhattanDistance(task.RallyCell) > 2)
                {
                    Move(world, npc, task.RallyCell, task);
                    return true;
                }

                bool ready = true;
                foreach (HuntMemberState other in task.Members)
                {
                    world.Entities.TryGetNpc(other.NpcId, out NpcState partner);
                    ready &= partner.Position.ManhattanDistance(task.RallyCell) <= 2 && Equipped(partner, other);
                }

                if (ready)
                {
                    task.Stage = HuntStage.Approaching;
                    task.DeadlineTick = world.Tick + HuntingRules.PursuitTicks;
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

                if (carcass.MeatRemaining + carcass.HideRemaining + carcass.Inventory.TotalCount == 0 || !task.MutableMembers.Exists(m => world.Entities.TryGetNpc(m.NpcId, out NpcState p) && p.Inventory.TotalCount < p.Inventory.Capacity))
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
                    if (!Move(world, npc, carcass.Position, task))
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
            if (perceived && HuntingSystem.CanStrike(world, npc, prey) && (member.Role != HuntRole.Spear || npc.Position.ManhattanDistance(prey.Position) <= 1))
            {
                task.Stage = HuntStage.Engaging;
                npc.Action.Label = "Hunt: attack";
                CombatSystem.TryStartHunt(world, npc, prey, events);
                return true;
            }

            member.WorkCell = ChooseSlot(world, npc, member, task);
            npc.Action.Label = member.Role == HuntRole.Flanker ? "Hunt: flank" : "Hunt: track";
            if (!member.WorkCell.IsValid || !Move(world, npc, member.WorkCell, task))
            {
                Retreat(world, task, "接近或退路不可达", events);
            }

            return true;
        }

        private static bool Equipped(NpcState npc, HuntMemberState member) => member.Role == HuntRole.Spear ? npc.Survival.Durability(ItemKind.Spear) > 0 : npc.Survival.Durability(ItemKind.Bow) > 0 && npc.Inventory.Count(ItemKind.Arrow) > 0;
        private static bool PrepareEquipment(WorldState world, NpcState npc, HuntMemberState member, HuntTaskState task)
        {
            if (Equipped(npc, member))
            {
                return true;
            }

            ItemKind item = member.Role == HuntRole.Spear ? ItemKind.Spear : npc.Survival.Durability(ItemKind.Bow) == 0 ? ItemKind.Bow : ItemKind.Arrow;
            if (npc.Inventory.Count(item) > 0 && item != ItemKind.Arrow)
            {
                npc.Inventory.TryRemove(item, 1);
                npc.Survival.SetTool(item, SurvivalRules.ToolDurability);
                return Equipped(npc, member);
            }

            GridPos stock = world.Resources.StockpilePosition;
            if (npc.Position != stock)
            {
                Move(world, npc, stock, task);
                return false;
            }

            int amount = item == ItemKind.Arrow ? Math.Min(4, npc.Inventory.Capacity - npc.Inventory.TotalCount) : 1;
            amount = Math.Min(amount, world.Resources.AvailableStock(item));
            if (amount > 0 && npc.Inventory.TotalCount + amount <= npc.Inventory.Capacity)
            {
                world.Resources.Stockpile.TryRemove(item, amount);
                npc.Inventory.TryAdd(item, amount);
            }

            return false;
        }

        private static GridPos ChooseSlot(WorldState world, NpcState npc, HuntMemberState member, HuntTaskState task)
        {
            GridPos center = task.LastSeenPosition;
            int dx = Math.Sign(center.X - task.PreviousSeenPosition.X), dy = Math.Sign(center.Y - task.PreviousSeenPosition.Y);
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
                    if (!world.Map.IsWalkable(cell) || task.MutableMembers.Exists(m => m != member && m.WorkCell == cell))
                    {
                        continue;
                    }

                    if (member.Role != HuntRole.Spear && !HuntingSystem.ClearShot(world, npc.Id, cell, center))
                    {
                        continue;
                    }

                    candidates.Add(cell);
                }

            candidates.Sort((a, b) =>
            {
                int ca = npc.Position.ManhattanDistance(a) * 4 + (member.Role == HuntRole.Flanker ? a.ManhattanDistance(predicted) : Math.Abs(a.ManhattanDistance(center) - range));
                int cb = npc.Position.ManhattanDistance(b) * 4 + (member.Role == HuntRole.Flanker ? b.ManhattanDistance(predicted) : Math.Abs(b.ManhattanDistance(center) - range));
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

        private static bool Move(WorldState world, NpcState npc, GridPos destination, HuntTaskState task)
        {
            npc.Action.Target = destination;
            if (npc.Position == destination)
            {
                return true;
            }

            PathResult path = Pathfinder.FindPath(world.Map, npc.Position, destination, 512);
            if (!path.Found || path.Path.Count == 0)
            {
                return false;
            }

            GridPos next = path.Path[0];
            foreach (HuntMemberState member in task.Members)
            {
                if (member.NpcId != npc.Id && world.Entities.TryGetNpc(member.NpcId, out NpcState other) && other.Position == next)
                {
                    return true;
                }
            }

            // Keep a visible boar wind-up lane free; a flank step is safer than walking into it.
            foreach (CombatAttackState attack in world.Combat.Attacks)
            {
                if (attack.Kind != CombatAttackKind.Charge || attack.Phase != CombatAttackPhase.Preparing || !CombatSystem.TryPosition(world, attack.Attacker, true, out GridPos origin))
                {
                    continue;
                }

                if (AnimalEncounterSystem.OnChargeLane(origin, next, attack.FacingX, attack.FacingY))
                {
                    GridPos side = npc.Position.Offset(attack.FacingY == 0 ? 0 : 1, attack.FacingX == 0 ? 0 : 1);
                    if (world.Map.IsWalkable(side))
                    {
                        next = side;
                    }
                }
            }

            npc.Position = next;
            npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - 4);
            return true;
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
            task.DeadlineTick = world.Tick + 3;
            foreach (HuntMemberState member in task.Members)
            {
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

            task.MutableMembers.RemoveAll(m => m.NpcId == id);
            world.Hunting.Cooldowns[id.Value] = world.Tick + HuntingRules.RetryTicks;
            if (world.Entities.TryGetNpc(id, out NpcState npc))
            {
                npc.Survival.HuntDropId = 0;
            }

            world.Ecology.Release(id);
            if (task.Members.Count == 0)
            {
                Finish(world, task);
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

        private static void Finish(WorldState world, HuntTaskState task)
        {
            world.Ecology.Release(task.LeaderId);
            world.Hunting.MutableTasks.Remove(task);
        }
    }
}
