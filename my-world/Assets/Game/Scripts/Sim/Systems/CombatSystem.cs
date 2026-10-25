using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class CombatSystem
    {
        public static void RecordPositions(WorldState world)
        {
            CombatState state = world.Combat;
            if (state.RecordedTick == world.Tick) { return; }
            state.RecordedTick = world.Tick;
            state.StartPositions.Clear();
            state.BeginTraceTick();
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (world.Map.IsInside(npc.Position)) { state.StartPositions[npc.Id.Value] = npc.Position; }
            }
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                // External residents share the registry, but have no local movement endpoint.
                if (HasLocalPosition(world, animal)) { state.StartPositions[animal.Id.Value] = animal.Position; }
            }
        }

        public static bool TryStartHunt(WorldState world, NpcState npc, AnimalState animal, List<SimEvent> events)
        {
            if (npc == null || animal == null || !world.Entities.TryGetNpc(npc.Id, out NpcState registered) || registered != npc ||
                world.Ecology.Find(animal.Id.Value) != animal || animal.Health <= 0 || !HuntingCoordinator.Authorized(world, npc, animal) ||
                !HasLocalPosition(world, animal) || !world.Map.IsInside(npc.Position) ||
                !Ready(npc) || !HuntingSystem.CanStrike(world, npc, animal)) { return false; }
            ItemKind weapon = HuntingSystem.Weapon(world, npc);
            if (npc.Survival.Durability(weapon) <= 0 || (weapon == ItemKind.Bow && npc.Inventory.Count(ItemKind.Arrow) <= 0)) { return false; }
            return Start(world, npc.Id, animal.Id, false, true,
                weapon == ItemKind.Bow ? CombatAttackKind.Bow : CombatAttackKind.Spear, npc.Position, animal.Position, false, events);
        }

        public static bool RequestCharge(WorldState world, AnimalState boar, NpcState target, List<SimEvent> events)
        {
            if (boar == null || target == null || boar.Species != AnimalSpecies.Boar || boar.Region != -1 ||
                boar.Health <= 0 || target.Survival.Life != LifeState.Active ||
                boar.Position.ManhattanDistance(target.Position) > 4 || !HuntingSystem.Visible(world, boar.Position, target.Position)) { return false; }
            return Start(world, boar.Id, target.Id, true, false, CombatAttackKind.Charge, boar.Position, target.Position, false, events);
        }

        public static bool RequestAnimalAttack(WorldState world, AnimalState attacker, AnimalState target, List<SimEvent> events)
        {
            return target != null && world.Ecology.Find(target.Id.Value) == target &&
                StartAnimal(world, attacker, target.Id, true, false, events);
        }

        public static bool RequestNpcAttack(WorldState world, AnimalState attacker, NpcState target, List<SimEvent> events)
        {
            return target != null && world.Entities.TryGetNpc(target.Id, out NpcState registered) && registered == target &&
                StartAnimal(world, attacker, target.Id, false, false, events);
        }

        private static bool StartAnimal(WorldState world, AnimalState attacker, EntityId target, bool targetIsAnimal,
            bool counterattack, List<SimEvent> events)
        {
            if (attacker == null || world.Ecology.Find(attacker.Id.Value) != attacker || attacker.Id == target) { return false; }
            CombatAttackKind kind = attacker.Species == AnimalSpecies.Boar ? CombatAttackKind.Gore :
                attacker.Species == AnimalSpecies.Deer ? CombatAttackKind.Kick : CombatAttackKind.Bite;
            if (attacker.Region >= 0)
            {
                if (!targetIsAnimal || !TryRegionalActors(world, attacker.Id, target, attacker.Region, attacker.HabitatId,
                    out AnimalState _, out AnimalState _)) { return false; }
                return Start(world, attacker.Id, target, true, true, kind, GridPos.Zero, GridPos.Zero,
                    counterattack, events, attacker.Region, attacker.HabitatId);
            }
            if (!TryPosition(world, attacker.Id, true, out GridPos from) ||
                !TryPosition(world, target, targetIsAnimal, out GridPos to) ||
                !InMeleeRange(from, to) || !HuntingSystem.Visible(world, from, to)) { return false; }
            return Start(world, attacker.Id, target, true, targetIsAnimal, kind, from, to, counterattack, events);
        }

        private static bool Start(WorldState world, EntityId attacker, EntityId target, bool attackerIsAnimal,
            bool targetIsAnimal, CombatAttackKind kind, GridPos from, GridPos to, bool counterattack, List<SimEvent> events,
            int region = -1, int habitatId = -1)
        {
            RecordPositions(world);
            CombatState state = world.Combat;
            CombatAttackState existing = state.GetAttack(attacker);
            if (existing != null)
            {
                if (existing.Phase != CombatAttackPhase.Cooldown || world.Tick < existing.CooldownUntilTick) { return false; }
                state.MutableAttacks.Remove(existing);
            }
            if (state.NextAttackId == long.MaxValue) { throw new InvalidOperationException("Combat attack IDs exhausted."); }
            int preparation = kind == CombatAttackKind.Bow || kind == CombatAttackKind.Charge ? 2 : 1;
            var attack = new CombatAttackState(state.NextAttackId++, attacker, target)
            {
                AttackerIsAnimal = attackerIsAnimal, TargetIsAnimal = targetIsAnimal, Kind = kind,
                Region = region, HabitatId = habitatId,
                Phase = CombatAttackPhase.Preparing, StartTick = world.Tick, ReleaseTick = world.Tick + preparation,
                RecoveryUntilTick = world.Tick + preparation + 1, CooldownUntilTick = world.Tick + preparation + 3,
                FacingX = (to.X - from.X) * CombatState.UnitsPerCell, FacingY = (to.Y - from.Y) * CombatState.UnitsPerCell,
                IsCounterattack = counterattack, AimPosition = to
            };
            if (kind == CombatAttackKind.Charge)
            {
                attack.FacingX = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y) ? Math.Sign(to.X - from.X) * CombatState.UnitsPerCell : 0;
                attack.FacingY = attack.FacingX == 0 ? Math.Sign(to.Y - from.Y) * CombatState.UnitsPerCell : 0;
                attack.RecoveryUntilTick = attack.ReleaseTick + 5; attack.CooldownUntilTick = attack.ReleaseTick + 7;
            }
            int index = state.MutableAttacks.FindIndex(a => a.Attacker.Value > attacker.Value);
            state.MutableAttacks.Insert(index < 0 ? state.MutableAttacks.Count : index, attack);
            if (counterattack) { state.Counterattacks++; }
            AddEvent(world, attack, CombatEventKind.Started, from, 0);
            events?.Add(new SimEvent(SimEventKind.NpcActionStarted, world.Tick, attacker, kind + " preparing against " + target + "."));
            return true;
        }

        public static void CancelUnreleased(WorldState world, EntityId attacker)
        {
            CombatAttackState attack = world.Combat.GetAttack(attacker);
            if (attack == null || attack.Phase != CombatAttackPhase.Preparing) { return; }
            RecordPositions(world);
            if (attack.Region == -1)
            {
                GridPos position = TryPosition(world, attacker, attack.AttackerIsAnimal, out GridPos cell) ? cell :
                    world.Combat.StartPosition(attacker, GridPos.Zero);
                AddEvent(world, attack, CombatEventKind.Cancelled, position, 0);
            }
            world.Combat.MutableAttacks.Remove(attack);
        }

        public static void Update(WorldState world, List<SimEvent> events)
        {
            RecordPositions(world);
            CombatState state = world.Combat;
            if (state.LastUpdateTick == world.Tick) { return; }
            state.LastUpdateTick = world.Tick;
            // In-flight contacts occur within the minute; releases/melee contact occur at
            // its end. Resolve arrows first so a lethally struck actor cannot release later.
            // Finish charge movement endpoints before arrow sweeps, but delay charge contact until
            // after arrows: an animal killed during this minute cannot subsequently gore someone.
            var chargeContacts = new Dictionary<long, EntityId>();
            foreach (CombatAttackState charge in state.MutableAttacks.ToArray())
            {
                if (charge.Kind == CombatAttackKind.Charge)
                {
                    EntityId contact = AdvanceCharge(world, charge);
                    if (!contact.IsNone) { chargeContacts.Add(charge.Id, contact); }
                }
            }
            ProjectileSystem.Update(world, events);
            // Reactions may insert new attacks; never execute a newly-created reaction this minute.
            CombatAttackState[] attacks = state.MutableAttacks.ToArray();
            // Regional residents have no local geometry. Use only authority captured at request.
            foreach (CombatAttackState attack in attacks)
            {
                if (attack.Region >= 0) { UpdateRegionalAttack(world, attack, events); }
            }
            foreach (CombatAttackState attack in attacks)
            {
                if (attack.Region >= 0) { continue; }
                if (attack.Kind == CombatAttackKind.Charge)
                {
                    if (chargeContacts.TryGetValue(attack.Id, out EntityId victim) &&
                        TryPosition(world, attack.Attacker, true, out GridPos source) &&
                        TryPosition(world, victim, false, out GridPos destination) && source == destination)
                    {
                        AnimalState boar = world.Ecology.Find(attack.Attacker.Value);
                        ApplyDamage(world, attack.Attacker, true, victim, false, boar.Definition.InjuryDamage * 3 / 2,
                            attack.Id, 0, CombatState.TimeUnitsPerTick, events);
                    }
                    UpdateChargeCooldown(world, attack); continue;
                }
                if (attack.Phase == CombatAttackPhase.Preparing)
                {
                    if (!TryPosition(world, attack.Attacker, attack.AttackerIsAnimal, out GridPos from) ||
                        !TryPosition(world, attack.Target, attack.TargetIsAnimal, out GridPos to) ||
                        (!attack.AttackerIsAnimal && world.Entities.TryGetNpc(attack.Attacker, out NpcState preparingNpc) &&
                         preparingNpc.Survival.Life != LifeState.Active))
                    {
                        CancelUnreleased(world, attack.Attacker);
                        continue;
                    }
                    GridPos previousAim = attack.AimPosition;
                    bool visible = HuntingSystem.Visible(world, from, to);
                    if (attack.Kind == CombatAttackKind.Bow && world.Entities.TryGetNpc(attack.Attacker, out NpcState observer))
                    {
                        visible = HuntingSystem.Perceived(world, observer, to);
                    }
                    if (visible)
                    {
                        attack.AimPosition = to;
                        attack.FacingX = (to.X - from.X) * CombatState.UnitsPerCell;
                        attack.FacingY = (to.Y - from.Y) * CombatState.UnitsPerCell;
                    }
                    if (world.Tick < attack.ReleaseTick) { continue; }
                    if (!attack.AttackerIsAnimal &&
                        (!world.Entities.TryGetNpc(attack.Attacker, out NpcState npc) || !Ready(npc) ||
                         npc.Survival.Durability(attack.Kind == CombatAttackKind.Bow ? ItemKind.Bow : ItemKind.Spear) <= 0))
                    {
                        CancelUnreleased(world, attack.Attacker);
                        continue;
                    }
                    if (attack.Kind == CombatAttackKind.Bow)
                    {
                        world.Entities.TryGetNpc(attack.Attacker, out NpcState shooter);
                        GridPos observedPrevious = visible ? previousAim : attack.AimPosition;
                        GridPos aim = PredictAim(world, shooter, from, attack.AimPosition, observedPrevious, out int leadX, out int leadY);
                        GridPos predicted = aim.Offset((int)Math.Round((double)leadX / CombatState.UnitsPerCell),
                            (int)Math.Round((double)leadY / CombatState.UnitsPerCell));
                        if (shooter.Inventory.Count(ItemKind.Arrow) <= 0 ||
                            !HuntingSystem.ClearShot(world, shooter.Id, from, aim) ||
                            !HuntingSystem.ClearShot(world, shooter.Id, from, predicted))
                        {
                            CancelUnreleased(world, attack.Attacker);
                            continue;
                        }
                        // Initiation enforces range. A moving visible target may leave it during the draw;
                        // releasing toward the last visible point must not create an endless cancel/draw loop.
                        ReleaseArrow(world, shooter, attack, from, aim, leadX, leadY, observedPrevious);
                    }
                    else
                    {
                        if (!attack.AttackerIsAnimal)
                        {
                            world.Entities.TryGetNpc(attack.Attacker, out NpcState meleeNpc);
                            SpendWeapon(world, meleeNpc, ItemKind.Spear);
                        }
                        if (InMeleeRange(from, to) && HuntingSystem.Visible(world, from, to))
                        {
                            int damage = MeleeDamage(world, attack);
                            ApplyDamage(world, attack.Attacker, attack.AttackerIsAnimal, attack.Target, attack.TargetIsAnimal,
                                damage, attack.Id, 0, CombatState.TimeUnitsPerTick, events);
                        }
                        else
                        {
                            state.Misses++;
                            AddEvent(world, attack, CombatEventKind.Missed, from, 0);
                        }
                    }
                    attack.ReleasedTick = world.Tick;
                    attack.Phase = CombatAttackPhase.Recovery;
                    events?.Add(new SimEvent(SimEventKind.NpcActionCompleted, world.Tick, attack.Attacker, attack.Kind + " released."));
                }
                if (attack.Phase == CombatAttackPhase.Recovery && world.Tick >= attack.RecoveryUntilTick)
                {
                    attack.Phase = CombatAttackPhase.Cooldown;
                }
                if (attack.Phase == CombatAttackPhase.Cooldown && world.Tick >= attack.CooldownUntilTick)
                {
                    state.MutableAttacks.Remove(attack);
                }
            }
            ProjectileSystem.UpdateEmbedded(world);
            // A melee contact can invalidate an attack already visited above.
            foreach (CombatAttackState attack in state.MutableAttacks.ToArray())
            {
                if (attack.Phase == CombatAttackPhase.Preparing &&
                    (attack.Region >= 0 ? !RegionalExecutable(world, attack) :
                        !TryPosition(world, attack.Attacker, attack.AttackerIsAnimal, out GridPos _) ||
                        !TryPosition(world, attack.Target, attack.TargetIsAnimal, out GridPos _)))
                {
                    CancelUnreleased(world, attack.Attacker);
                }
            }
        }

        private static void UpdateRegionalAttack(WorldState world, CombatAttackState attack, List<SimEvent> events)
        {
            if (attack.Phase == CombatAttackPhase.Preparing)
            {
                if (!RegionalExecutable(world, attack))
                {
                    CancelUnreleased(world, attack.Attacker);
                    return;
                }
                if (world.Tick < attack.ReleaseTick) { return; }
                ApplyDamage(world, attack.Attacker, true, attack.Target, true, MeleeDamage(world, attack),
                    attack.Id, 0, CombatState.TimeUnitsPerTick, events, attack.Region, attack.HabitatId);
                attack.ReleasedTick = world.Tick;
                attack.Phase = CombatAttackPhase.Recovery;
                events?.Add(new SimEvent(SimEventKind.NpcActionCompleted, world.Tick, attack.Attacker, attack.Kind + " released."));
            }
            if (attack.Phase == CombatAttackPhase.Recovery && world.Tick >= attack.RecoveryUntilTick)
            {
                attack.Phase = CombatAttackPhase.Cooldown;
            }
            if (attack.Phase == CombatAttackPhase.Cooldown && world.Tick >= attack.CooldownUntilTick)
            {
                world.Combat.MutableAttacks.Remove(attack);
            }
        }

        internal static bool ValidRegionalHabitat(WorldState world, int region, int habitatId)
        {
            if (region < 0 || region >= world.Ecology.Regions.Regions.Count) { return false; }
            EcologyRegionState savedRegion = world.Ecology.Regions.Regions[region];
            HabitatPatchState habitat = world.Ecology.FindHabitat(habitatId);
            return savedRegion.Id == region && savedRegion.HabitatId == habitatId && habitat != null && habitat.Region == region;
        }

        internal static bool TryRegionalActors(WorldState world, EntityId attackerId, EntityId targetId, int region, int habitatId,
            out AnimalState attacker, out AnimalState target)
        {
            attacker = world.Ecology.Find(attackerId.Value);
            target = world.Ecology.Find(targetId.Value);
            return attackerId != targetId && ValidRegionalHabitat(world, region, habitatId) &&
                attacker != null && target != null && attacker.Health > 0 && target.Health > 0 &&
                !attacker.DeadProcessed && !target.DeadProcessed && attacker.Region == region && target.Region == region &&
                attacker.HabitatId == habitatId && target.HabitatId == habitatId &&
                attacker.Position == GridPos.Invalid && target.Position == GridPos.Invalid;
        }

        internal static bool RegionalExecutable(WorldState world, CombatAttackState attack) =>
            attack.AttackerIsAnimal && attack.TargetIsAnimal &&
            (attack.Kind == CombatAttackKind.Bite || attack.Kind == CombatAttackKind.Gore) &&
            TryRegionalActors(world, attack.Attacker, attack.Target, attack.Region, attack.HabitatId,
                out AnimalState _, out AnimalState _);

        private static bool Ready(NpcState npc) => npc.Survival.Life == LifeState.Active && npc.Needs.Energy >= 1800 && npc.Needs.Health >= 3000;
        internal static bool InMeleeRange(GridPos from, GridPos to)
        {
            long dx = to.X - from.X, dy = to.Y - from.Y;
            return dx * dx + dy * dy <= 1;
        }
        // Region -1 alone denotes a local animal; health does not imply map presence.
        internal static bool HasLocalPosition(WorldState world, AnimalState animal) =>
            animal != null && animal.Region == -1 && world.Map.IsInside(animal.Position);

        internal static bool TryPosition(WorldState world, EntityId id, bool animal, out GridPos position)
        {
            if (animal)
            {
                AnimalState state = world.Ecology.Find(id.Value);
                if (HasLocalPosition(world, state) && state.Health > 0) { position = state.Position; return true; }
            }
            else if (world.Entities.TryGetNpc(id, out NpcState npc) && npc.Survival.Life != LifeState.Dead &&
                npc.Needs.Health > 0 && world.Map.IsInside(npc.Position))
            {
                position = npc.Position;
                return true;
            }
            position = GridPos.Invalid;
            return false;
        }

        private static void SpendWeapon(WorldState world, NpcState npc, ItemKind weapon)
        {
            npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - (weapon == ItemKind.Bow ? 100 : 140));
            ProductionSystem.Wear(world, npc, weapon);
        }
        private static int MeleeDamage(WorldState world, CombatAttackState attack)
        {
            if (!attack.AttackerIsAnimal)
            {
                world.Entities.TryGetNpc(attack.Attacker, out NpcState npc);
                return 28 + npc.HuntingSkill / 10;
            }
            AnimalState animal = world.Ecology.Find(attack.Attacker.Value);
            if (!attack.TargetIsAnimal) { return animal.Definition.InjuryDamage; }
            return animal.Species == AnimalSpecies.Wolf ? 24 : animal.Species == AnimalSpecies.Boar ? 18 :
                animal.Species == AnimalSpecies.Fox ? 14 : 6;
        }

        private static GridPos PredictAim(WorldState world, NpcState npc, GridPos from, GridPos seen, GridPos previous,
            out int leadX, out int leadY)
        {
            int scale = CombatState.UnitsPerCell;
            long dx = (long)(seen.X - from.X) * scale, dy = (long)(seen.Y - from.Y) * scale;
            int flight = Math.Max(1, (int)((IntegerSqrt(dx * dx + dy * dy) + CombatState.ArrowSpeed - 1) / CombatState.ArrowSpeed));
            int limit = HuntingRules.MaxPredictionCells * scale;
            leadX = Math.Max(-limit, Math.Min(limit, (seen.X - previous.X) * scale * flight * npc.HuntingSkill / 200));
            leadY = Math.Max(-limit, Math.Min(limit, (seen.Y - previous.Y) * scale * flight * npc.HuntingSkill / 200));
            GridPos predicted = seen.Offset((int)Math.Round((double)leadX / scale), (int)Math.Round((double)leadY / scale));
            if (!HuntingSystem.Visible(world, from, predicted)) { leadX = leadY = 0; }
            return seen;
        }

        private static void ReleaseArrow(WorldState world, NpcState npc, CombatAttackState attack, GridPos from, GridPos to,
            int leadX, int leadY, GridPos previous)
        {
            CombatState state = world.Combat;
            if (state.NextProjectileId == long.MaxValue) { throw new InvalidOperationException("Combat projectile IDs exhausted."); }
            int scale = CombatState.UnitsPerCell;
            int x = from.X * scale + scale / 2, y = from.Y * scale + scale / 2;
            int dx = (to.X - from.X) * scale, dy = (to.Y - from.Y) * scale;
            long distance = IntegerSqrt((long)dx * dx + (long)dy * dy);
            dx += leadX; dy += leadY;
            GridPos shooterStart = state.StartPosition(npc.Id, from);
            int motion = shooterStart.ManhattanDistance(from) * 150 + previous.ManhattanDistance(to) * (100 - npc.HuntingSkill) * 3;
            EnvironmentState environment = world.Environment;
            int cold = Math.Max(0, 3500 - npc.Survival.BodyTemperature) / 3 + Math.Max(0, -environment.Temperature) / 30;
            int weather = environment.Weather == WeatherKind.Blizzard ? 350 : environment.Weather == WeatherKind.Snow ? 120 : 0;
            int error = 35 + (100 - npc.HuntingSkill) * 4 + (int)distance / 12 + motion +
                (10000 - npc.Needs.Energy) / 20 + (10000 - npc.Needs.Health) / 20 + cold +
                (10000 - environment.Daylight) / 30 + environment.Wind * 3 + weather;
            error = Math.Min(4096, error);
            var rng = new DeterministicRng(state.RngState);
            dx += rng.NextInt(-error, error + 1);
            dy += rng.NextInt(-error, error + 1);
            // Arrow quality/breakage is also fixed at release: collision draws no random numbers.
            int integrity = rng.NextInt(0, 100) < 12 ? 0 : 1;
            state.RngState = rng.State;
            long length = IntegerSqrt((long)dx * dx + (long)dy * dy);
            if (length == 0) { dx = scale; length = scale; }
            npc.Inventory.TryRemove(ItemKind.Arrow, 1);
            SpendWeapon(world, npc, ItemKind.Bow);
            var arrow = new ProjectileState(state.NextProjectileId++, npc.Id)
            {
                AttackId = attack.Id, Phase = ProjectilePhase.Flying, X = x, Y = y,
                VelocityX = (int)((long)dx * CombatState.ArrowSpeed / length),
                VelocityY = (int)((long)dy * CombatState.ArrowSpeed / length),
                ReleasedTick = world.Tick, LastAdvancedTick = world.Tick, RemainingTicks = CombatState.ArrowLifetime,
                Damage = 20 + npc.HuntingSkill / 10, Integrity = integrity,
                LastWalkablePosition = from, IgnoreInitialShooterOverlap = true
            };
            state.MutableProjectiles.Add(arrow);
            state.ShotsReleased++;
            state.AddEvent(new CombatVisualEvent(world.Tick, attack.Id, arrow.Id, npc.Id, EntityId.None,
                CombatEventKind.Released, x, y, CombatState.TimeUnitsPerTick, 0));
        }

        internal static long IntegerSqrt(long value)
        {
            if (value <= 0) { return 0; }
            long low = 1, high = Math.Min(value, 3037000499L), answer = 0;
            while (low <= high)
            {
                long middle = low + (high - low) / 2;
                if (middle <= value / middle) { answer = middle; low = middle + 1; }
                else { high = middle - 1; }
            }
            return answer;
        }

        internal static void ApplyDamage(WorldState world, EntityId source, bool sourceIsAnimal, EntityId target,
            bool targetIsAnimal, int damage, long attackId, long projectileId, int time, List<SimEvent> events,
            int region = -1, int habitatId = -1)
        {
            GridPos position = GridPos.Invalid;
            if (damage <= 0 || region < -1) { return; }
            if (region >= 0)
            {
                // Explicit animal melee authority only; projectiles remain strictly local.
                if (!sourceIsAnimal || !targetIsAnimal || projectileId != 0 ||
                    !TryRegionalActors(world, source, target, region, habitatId, out AnimalState _, out AnimalState _)) { return; }
            }
            else if (!TryPosition(world, target, targetIsAnimal, out position)) { return; }
            bool killed;
            int actual;
            if (targetIsAnimal)
            {
                AnimalState animal = world.Ecology.Find(target.Value);
                actual = Math.Min(animal.Health, damage);
                animal.Health -= actual;
                animal.LastDamageSourceId = source;
                animal.Fleeing = animal.Health > 0;
                AnimalEncounterSystem.Hit(world, animal, source, sourceIsAnimal, projectileId);
                killed = animal.Health == 0;
                if (killed)
                {
                    animal.DeathCause = sourceIsAnimal ? AnimalDeathCause.Predation : AnimalDeathCause.Hunt;
                    world.Combat.AnimalKills++;
                    events?.Add(new SimEvent(SimEventKind.NpcActionCompleted,
                        world.Tick, source, target + " lethally struck; unique death/carcass event belongs to ecology."));
                }
                if (!sourceIsAnimal && world.Entities.TryGetNpc(source, out NpcState npc))
                {
                    npc.HuntingSkill = Math.Min(100, npc.HuntingSkill + 1);
                    // Ecology.ResolveDeaths owns both NPC/Ecology HuntKills exactly once.
                }
                if (!killed && (animal.Species == AnimalSpecies.Boar || animal.Species == AnimalSpecies.Wolf))
                {
                    StartAnimal(world, animal, source, sourceIsAnimal, true, events);
                }
            }
            else
            {
                world.Entities.TryGetNpc(target, out NpcState npc);
                actual = Math.Min(npc.Needs.Health, damage);
                npc.Needs.Health -= actual;
                killed = npc.Needs.Health == 0;
                if (killed) { npc.Survival.DeathCause = NpcDeathCause.Combat; }
                world.Combat.NpcHits++;
                events?.Add(new SimEvent(SimEventKind.HunterInjured, world.Tick, target,
                    npc.Name + " hit by " + source + "; health -" + actual + "."));
                // Life state/work interruption belongs to the engine's post-combat HealthSystem pass.
            }
            world.Combat.Hits++;
            world.Combat.RecordDamage(new CombatDamageRecord(target, source, targetIsAnimal, sourceIsAnimal,
                world.Tick, attackId, projectileId, actual, killed));
            if (region == -1)
            {
                world.Combat.AddEvent(new CombatVisualEvent(world.Tick, attackId, projectileId, source, target,
                    CombatEventKind.Contact, position.X * CombatState.UnitsPerCell + 512,
                    position.Y * CombatState.UnitsPerCell + 512, time, actual));
            }
        }

        private static EntityId AdvanceCharge(WorldState world, CombatAttackState attack)
        {
            if (attack.Phase == CombatAttackPhase.Preparing)
            {
                if (!TryPosition(world, attack.Attacker, true, out GridPos _))
                { CancelUnreleased(world, attack.Attacker); return EntityId.None; }
                if (world.Tick < attack.ReleaseTick) { return EntityId.None; }
                attack.Phase = CombatAttackPhase.Recovery; attack.ReleasedTick = attack.ReleaseTick; attack.ChargeRemaining = 3;
                AddEvent(world, attack, CombatEventKind.Released, world.Ecology.Find(attack.Attacker.Value).Position, 0);
            }
            EntityId contact = EntityId.None;
            if (attack.Phase == CombatAttackPhase.Recovery && attack.ChargeRemaining > 0 && attack.LastChargeTick != world.Tick)
            {
                attack.LastChargeTick = world.Tick;
                AnimalState animal = world.Ecology.Find(attack.Attacker.Value);
                if (!HasLocalPosition(world, animal) || animal.Health <= 0) { attack.ChargeRemaining = 0; }
                else
                {
                    GridPos next = animal.Position.Offset(Math.Sign(attack.FacingX), Math.Sign(attack.FacingY));
                    if (!world.Map.IsWalkable(next)) { attack.ChargeRemaining = 0; }
                    else
                    {
                        animal.Position = next; animal.HabitatId = world.Ecology.HabitatAt(next).Id;
                        attack.ChargeRemaining--;
                        foreach (NpcState npc in world.Entities.Npcs)
                        {
                            if (npc.Survival.Life != LifeState.Active || npc.Position != next) { continue; }
                            contact = npc.Id; attack.ChargeRemaining = 0; break;
                        }
                    }
                }
                if (attack.ChargeRemaining == 0)
                { attack.RecoveryUntilTick = world.Tick + 2; attack.CooldownUntilTick = world.Tick + 4; }
            }
            return contact;
        }

        private static void UpdateChargeCooldown(WorldState world, CombatAttackState attack)
        {
            if (attack.Phase == CombatAttackPhase.Recovery && world.Tick >= attack.RecoveryUntilTick) { attack.Phase = CombatAttackPhase.Cooldown; }
            if (attack.Phase == CombatAttackPhase.Cooldown && world.Tick >= attack.CooldownUntilTick) { world.Combat.MutableAttacks.Remove(attack); }
        }

        private static void AddEvent(WorldState world, CombatAttackState attack, CombatEventKind kind, GridPos position, int damage)
        {
            if (attack.Region >= 0) { return; }
            world.Combat.AddEvent(new CombatVisualEvent(world.Tick, attack.Id, 0, attack.Attacker, attack.Target, kind,
                position.X * CombatState.UnitsPerCell + 512, position.Y * CombatState.UnitsPerCell + 512,
                kind == CombatEventKind.Started ? 0 : CombatState.TimeUnitsPerTick, damage));
        }

        public static int RecoverArrows(WorldState world, NpcState npc, GridPos position)
        {
            if (npc == null || npc.Survival.Life != LifeState.Active || npc.Needs.Health <= 0 ||
                !world.Entities.TryGetNpc(npc.Id, out NpcState registered) || registered != npc ||
                !world.Map.IsWalkable(position) || npc.Position.ManhattanDistance(position) > 1) { return 0; }
            RecordPositions(world);
            ProjectileSystem.UpdateEmbedded(world);
            int recovered = 0;
            foreach (ProjectileState arrow in world.Combat.MutableProjectiles.ToArray())
            {
                if (arrow.Phase != ProjectilePhase.Embedded || ProjectileSystem.Cell(arrow.X, arrow.Y) != position ||
                    !npc.Inventory.TryAdd(ItemKind.Arrow, 1)) { continue; }
                world.Combat.MutableProjectiles.Remove(arrow);
                world.Combat.AddEvent(new CombatVisualEvent(world.Tick, arrow.AttackId, arrow.Id, arrow.Shooter,
                    arrow.EmbeddedTarget, CombatEventKind.Recovered, arrow.X, arrow.Y, CombatState.TimeUnitsPerTick, 0));
                recovered++;
            }
            foreach (GroundStockState stock in world.Survival.Ground)
            {
                if (stock.Position != position || (!stock.ReservedBy.IsNone && stock.ReservedBy != npc.Id)) { continue; }
                int count = Math.Min(stock.Inventory.Count(ItemKind.Arrow), npc.Inventory.Capacity - npc.Inventory.TotalCount);
                if (count <= 0) { continue; }
                stock.Inventory.TryRemove(ItemKind.Arrow, count);
                npc.Inventory.TryAdd(ItemKind.Arrow, count);
                recovered += count;
            }
            world.Combat.ArrowsRecovered += recovered;
            return recovered;
        }
    }
}
