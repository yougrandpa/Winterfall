using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class AnimalEncounterSystem
    {
        private static readonly int[] Dx = {0, 1, 0, -1};
        private static readonly int[] Dy = {1, 0, -1, 0};
        internal static bool Update(WorldState world, AnimalState animal, List<SimEvent> events)
        {
            AnimalEncounterState state = animal.Encounter;
            EntityId threat = EntityId.None;
            bool isAnimal = false;
            GridPos position = GridPos.Invalid;
            int best = int.MaxValue;
            int sight = animal.Definition.Sight;
            if (world.Environment.Daylight < 2000 && !animal.Definition.IsPredator)
            {
                sight = Math.Max(2, sight / 2);
            }

            if (world.Environment.Weather == WeatherKind.Blizzard)
            {
                sight = Math.Max(1, sight / 3);
            }

            foreach (NpcState npc in world.Entities.Npcs)
            {
                int distance = npc.Position.ManhattanDistance(animal.Position);
                if (npc.Survival.Life != LifeState.Active || distance > sight || distance > (npc.Survival.HuntMotion == HuntMotion.Sneak ? 2 : animal.Species == AnimalSpecies.Deer || animal.Species == AnimalSpecies.Wolf ? 4 : 3) || !npc.HasHuntingTool && distance > 2 || !HuntingSystem.Visible(world, animal.Position, npc.Position))
                {
                    continue;
                }

                int score = distance * 10 - (animal.LastDamageSourceId == npc.Id ? 30 : 0);
                if (score < best || score == best && npc.Id.Value < threat.Value)
                {
                    best = score;
                    threat = npc.Id;
                    position = npc.Position;
                    isAnimal = false;
                }
            }

            foreach (AnimalState other in world.Ecology.Animals)
            {
                if (other == animal || other.Region != -1 || other.Health <= 0 || !other.Definition.CanPreyOn(animal.Species))
                {
                    continue;
                }

                int distance = other.Position.ManhattanDistance(animal.Position);
                if (distance > sight || !HuntingSystem.Visible(world, animal.Position, other.Position))
                {
                    continue;
                }

                int score = distance * 10 - (animal.LastDamageSourceId == other.Id ? 30 : 0);
                if (score < best || score == best && other.Id.Value < threat.Value)
                {
                    best = score;
                    threat = other.Id;
                    position = other.Position;
                    isAnimal = true;
                }
            }

            CombatAttackState attack = world.Combat.GetAttack(animal.Id);
            if (attack != null && attack.Kind == CombatAttackKind.Charge && attack.Phase != CombatAttackPhase.Cooldown)
            {
                state.Phase = EncounterPhase.Defending;
                animal.Behavior = AnimalBehavior.Defend;
                animal.Fleeing = false;
                return true;
            }

            if (!threat.IsNone)
            {
                if (state.Phase == EncounterPhase.Calm || state.Phase == EncounterPhase.Recovering)
                {
                    state.Phase = EncounterPhase.Alert;
                    state.AlertTick = world.Tick;
                }

                state.ThreatId = threat;
                state.ThreatIsAnimal = isAnimal;
                state.LastThreatPosition = position;
                state.ThreatUntilTick = world.Tick + HuntingRules.LostTicks;
                animal.TargetPosition = position;
                animal.TargetAnimalId = isAnimal ? threat : EntityId.None;
                int distance = animal.Position.ManhattanDistance(position);
                int nearbyHunters = 0;
                foreach (NpcState other in world.Entities.Npcs)
                {
                    if (other.Survival.Life == LifeState.Active && other.Position.ManhattanDistance(animal.Position) <= 2)
                    {
                        nearbyHunters++;
                    }
                }

                bool cornered = Exits(world, animal.Position) <= 1 || nearbyHunters >= 2;
                bool engaged = animal.LastDamageSourceId == threat && world.Tick < state.ThreatUntilTick || distance <= 1;
                if (!engaged && animal.Species == AnimalSpecies.Wolf)
                {
                    foreach (AnimalState pack in world.Ecology.Animals)
                    {
                        if (pack != animal && pack.GroupId == animal.GroupId && pack.Region == -1 && pack.Health > 0 && pack.Position.ManhattanDistance(animal.Position) <= 6 && (pack.Encounter.ThreatId == threat && world.Tick < pack.Encounter.ThreatUntilTick && (pack.Encounter.Phase == EncounterPhase.Defending || pack.LastDamageSourceId == threat)))
                        {
                            engaged = true;
                            break;
                        }
                    }
                }

                bool defend = animal.Species == AnimalSpecies.Wolf && engaged || animal.Species == AnimalSpecies.Boar && engaged && animal.Health > animal.Definition.Health / 5 || animal.Species == AnimalSpecies.Deer && cornered && distance <= 1;
                if (defend && distance <= 1)
                {
                    state.Phase = EncounterPhase.Defending;
                    animal.Behavior = AnimalBehavior.Defend;
                    animal.Fleeing = false;
                    if (isAnimal)
                    {
                        CombatSystem.RequestAnimalAttack(world, animal, world.Ecology.Find(threat.Value), events);
                    }
                    else if (world.Entities.TryGetNpc(threat, out NpcState npc))
                    {
                        CombatSystem.RequestNpcAttack(world, animal, npc, events);
                    }

                    return true;
                }

                if (animal.Species == AnimalSpecies.Boar && !isAnimal && distance >= 2 && distance <= 4 && defend && world.Entities.TryGetNpc(threat, out NpcState chargeTarget) && CombatSystem.RequestCharge(world, animal, chargeTarget, events))
                {
                    state.Phase = EncounterPhase.Defending;
                    animal.Behavior = AnimalBehavior.Defend;
                    animal.Fleeing = false;
                    return true;
                }

                if (animal.Species == AnimalSpecies.Wolf && engaged && !isAnimal)
                {
                    state.Phase = EncounterPhase.Defending;
                    animal.Behavior = AnimalBehavior.Defend;
                    animal.Fleeing = false;
                    if (world.Tick >= state.NextStepTick)
                    {
                        StepToward(world, animal, position);
                        state.NextStepTick = world.Tick + 1;
                    }

                    return true;
                }

                if (world.Tick == state.AlertTick && animal.Health == animal.Definition.Health)
                {
                    animal.Fleeing = false;
                    animal.Behavior = AnimalBehavior.Rest;
                    return true;
                }

                state.Phase = EncounterPhase.Fleeing;
            }
            else if (world.Tick >= state.ThreatUntilTick)
            {
                state.Phase = state.Stamina < HuntingRules.MaxStamina ? EncounterPhase.Recovering : EncounterPhase.Calm;
                state.Stamina = Math.Min(HuntingRules.MaxStamina, state.Stamina + HuntingRules.StaminaRecovery);
                if (!state.ThreatId.IsNone)
                {
                    animal.TargetPosition = animal.Position;
                    animal.TargetAnimalId = EntityId.None;
                    animal.NextDecisionTick = world.Tick;
                }

                state.ThreatId = EntityId.None;
                state.LastThreatPosition = GridPos.Invalid;
                animal.Fleeing = false;
                return false;
            }

            if (state.Phase == EncounterPhase.Fleeing || !state.ThreatId.IsNone)
            {
                state.Phase = EncounterPhase.Fleeing;
                animal.Fleeing = true;
                animal.Behavior = AnimalBehavior.Flee;
                if (world.Tick >= state.NextStepTick)
                {
                    Flee(world, animal, state.LastThreatPosition);
                    bool sprint = state.Stamina >= HuntingRules.SprintCost && animal.Health * 2 >= animal.Definition.Health;
                    if (sprint)
                    {
                        state.Stamina -= HuntingRules.SprintCost;
                    }
                    else
                    {
                        state.Stamina = Math.Min(HuntingRules.MaxStamina, state.Stamina + 200);
                    }

                    state.NextStepTick = world.Tick + (sprint ? 1 : 2);
                }

                return true;
            }

            return false;
        }

        internal static void NearMiss(WorldState world, GridPos from, GridPos to, EntityId source)
        {
            long dx = to.X - from.X, dy = to.Y - from.Y, length = dx * dx + dy * dy;
            if (length == 0) { return; }
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Region != -1 || animal.Health <= 0) { continue; }
                long x = animal.Position.X - from.X, y = animal.Position.Y - from.Y;
                long dot = x * dx + y * dy, cross = x * dy - y * dx;
                // An arrow still approaching is not a near miss. Do not give every
                // animal a free warning one tick before the projectile can arrive.
                if (dot < 0 || dot > length || cross * cross > length) { continue; }
                GridPos heard = new GridPos(Math.Max(0, Math.Min(world.Map.Width - 1, from.X + (int)(dx * dot / length))), Math.Max(0, Math.Min(world.Map.Height - 1, from.Y + (int)(dy * dot / length))));
                AnimalEncounterState state = animal.Encounter;
                state.ThreatId = source; state.ThreatIsAnimal = false; state.LastThreatPosition = heard;
                state.ThreatUntilTick = world.Tick + HuntingRules.LostTicks; state.AlertTick = Math.Max(0, world.Tick - 1);
                state.Phase = EncounterPhase.Fleeing; animal.Fleeing = true;
            }
        }

        internal static void Disturb(WorldState world, GridPos origin, EntityId source, int radius)
        {
            if (!world.Map.IsInside(origin)) { return; }
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Region != -1 || animal.Health <= 0 || animal.Position.ManhattanDistance(origin) > radius) { continue; }
                AnimalEncounterState state = animal.Encounter;
                state.ThreatId = source;
                state.ThreatIsAnimal = false;
                state.LastThreatPosition = origin;
                state.ThreatUntilTick = world.Tick + HuntingRules.LostTicks;
                state.AlertTick = Math.Max(0, world.Tick - 1);
                state.Phase = EncounterPhase.Fleeing;
                animal.Fleeing = true;
            }
        }

        internal static void Hit(WorldState world, AnimalState animal, EntityId source, bool isAnimal, long projectileId = 0)
        {
            if (animal.Region != -1 || animal.Health <= 0)
            {
                return;
            }

            GridPos cell = animal.Encounter.ThreatId == source && animal.Encounter.LastThreatPosition.IsValid ?
                animal.Encounter.LastThreatPosition : animal.Position;
            int sight = animal.Definition.Sight;
            if (world.Environment.Daylight < 2000 && !animal.Definition.IsPredator) { sight = Math.Max(2, sight / 2); }
            if (world.Environment.Weather == WeatherKind.Blizzard) { sight = Math.Max(1, sight / 3); }
            if (CombatSystem.TryPosition(world, source, isAnimal, out GridPos perceived) &&
                animal.Position.ManhattanDistance(perceived) <= sight && HuntingSystem.Visible(world, animal.Position, perceived))
            {
                cell = perceived;
            }
            else if (cell == animal.Position && projectileId > 0)
            {
                // An unseen shooter only reveals the direction of the incoming arrow, never their live position.
                foreach (ProjectileState projectile in world.Combat.Projectiles)
                {
                    if (projectile.Id != projectileId) { continue; }
                    GridPos incoming = animal.Position.Offset(-Math.Sign(projectile.VelocityX), -Math.Sign(projectile.VelocityY));
                    if (world.Map.IsInside(incoming)) { cell = incoming; }
                    break;
                }
            }
            animal.Encounter.ThreatId = source;
            animal.Encounter.ThreatIsAnimal = isAnimal;
            animal.Encounter.LastThreatPosition = cell;
            animal.Encounter.ThreatUntilTick = world.Tick + HuntingRules.LostTicks;
            animal.Encounter.AlertTick = Math.Max(0, world.Tick - 1);
            animal.Encounter.Phase = EncounterPhase.Fleeing;
            animal.Encounter.NextStepTick = Math.Min(animal.Encounter.NextStepTick, world.Tick + 1);
        }

        private static int Exits(WorldState world, GridPos cell)
        {
            int result = 0;
            for (int i = 0; i < 4; i++)
            {
                if (world.Map.IsWalkable(cell.Offset(Dx[i], Dy[i])))
                {
                    result++;
                }
            }

            return result;
        }

        private static void Flee(WorldState world, AnimalState animal, GridPos threat)
        {
            if (!threat.IsValid)
            {
                return;
            }

            // Bounded breadth-first lookahead can temporarily approach a threat to leave a dead end.
            var queue = new Queue<GridPos>();
            var first = new Dictionary<GridPos, GridPos>();
            var depth = new Dictionary<GridPos, int>();
            queue.Enqueue(animal.Position);
            first[animal.Position] = animal.Position;
            depth[animal.Position] = 0;
            GridPos best = animal.Position;
            int bestScore = int.MinValue;
            while (queue.Count > 0)
            {
                GridPos cell = queue.Dequeue();
                int d = depth[cell];
                int score = cell.ManhattanDistance(threat) * 30 + Exits(world, cell) * 5 - d * 2 + (world.Map.GetTile(cell) == TileType.Forest ? 20 : 0) + (!HuntingSystem.Visible(world, threat, cell) ? 35 : 0);
                if (d > 0 && score > bestScore)
                {
                    best = cell;
                    bestScore = score;
                }

                if (d == 4)
                {
                    continue;
                }

                for (int n = 0; n < 4; n++)
                {
                    int dir = (n + animal.Id.Value) % 4;
                    GridPos next = cell.Offset(Dx[dir], Dy[dir]);
                    if (first.ContainsKey(next) || !world.Map.IsWalkable(next) || next == threat)
                    {
                        continue;
                    }

                    bool occupied = false;
                    foreach (NpcState npc in world.Entities.Npcs)
                    {
                        if (npc.Survival.Life == LifeState.Active && npc.Position == next)
                        {
                            occupied = true;
                            break;
                        }
                    }

                    if (occupied)
                    {
                        continue;
                    }

                    first[next] = d == 0 ? next : first[cell];
                    depth[next] = d + 1;
                    queue.Enqueue(next);
                }
            }

            if (best != animal.Position)
            {
                animal.Position = first[best];
                animal.HabitatId = world.Ecology.HabitatAt(animal.Position).Id;
            }
        }

        private static void StepToward(WorldState world, AnimalState animal, GridPos position)
        {
            var path = new MyAiWorld.Sim.Navigation.GridPathfinder().FindPath(world.Map, animal.Position, position, 128);
            if (path.Found && path.Path.Count > 0)
            {
                animal.Position = path.Path[0];
                animal.HabitatId = world.Ecology.HabitatAt(animal.Position).Id;
            }
        }

        internal static bool OnChargeLane(GridPos origin, GridPos cell, int facingX, int facingY)
        {
            int dx = Math.Sign(facingX), dy = Math.Sign(facingY);
            for (int i = 1; i <= 3; i++)
            {
                if (origin.Offset(dx * i, dy * i) == cell)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
