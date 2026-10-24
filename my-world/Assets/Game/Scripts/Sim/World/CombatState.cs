using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum CombatAttackKind { Bow, Spear, Bite, Gore, Kick, Charge }
    public enum CombatAttackPhase { Preparing, Recovery, Cooldown }
    public enum ProjectilePhase { Flying, Embedded }
    public enum CombatEventKind { Started, Released, Flying, Contact, Missed, Cancelled, Embedded, Grounded, Broken, Lost, Recovered }

    public sealed class CombatAttackState
    {
        internal CombatAttackState(long id, EntityId attacker, EntityId target)
        {
            Id = id;
            Attacker = attacker;
            Target = target;
        }

        public long Id { get; }
        public EntityId Attacker { get; }
        public EntityId Target { get; internal set; }
        public bool AttackerIsAnimal { get; internal set; }
        public bool TargetIsAnimal { get; internal set; }
        // Captured at request time; local attacks never acquire regional authority.
        public int Region { get; internal set; } = -1;
        public int HabitatId { get; internal set; } = -1;
        public CombatAttackKind Kind { get; internal set; }
        public CombatAttackPhase Phase { get; internal set; }
        public long StartTick { get; internal set; }
        public long ReleaseTick { get; internal set; }
        public long ReleasedTick { get; internal set; } = -1;
        public long RecoveryUntilTick { get; internal set; }
        public long CooldownUntilTick { get; internal set; }
        public int FacingX { get; internal set; }
        public int FacingY { get; internal set; }
        public bool IsCounterattack { get; internal set; }
        public GridPos AimPosition { get; internal set; } = GridPos.Invalid;
        public int ChargeRemaining { get; internal set; }
        public long LastChargeTick { get; internal set; } = -1;
    }

    // A single Arrow belongs here until it is transferred to a bag/ground or consumed.
    public sealed class ProjectileState
    {
        internal ProjectileState(long id, EntityId shooter) { Id = id; Shooter = shooter; }
        public long Id { get; }
        public EntityId Shooter { get; }
        public long AttackId { get; internal set; }
        public ItemKind Ammunition => ItemKind.Arrow;
        public ProjectilePhase Phase { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int VelocityX { get; internal set; }
        public int VelocityY { get; internal set; }
        public long ReleasedTick { get; internal set; }
        public long LastAdvancedTick { get; internal set; }
        public int RemainingTicks { get; internal set; }
        public int Damage { get; internal set; }
        public int Integrity { get; internal set; }
        public EntityId EmbeddedTarget { get; internal set; }
        public bool EmbeddedTargetIsAnimal { get; internal set; }
        public GridPos LastWalkablePosition { get; internal set; }
        public bool IgnoreInitialShooterOverlap { get; internal set; }
    }

    // Immutable, one-minute visual facts, never authoritative/hash data.
    public readonly struct CombatTraceSegment
    {
        public CombatTraceSegment(long tick, long projectileId, EntityId shooter, int fromX, int fromY,
            int toX, int toY, EntityId hitId, CombatEventKind state, int fromTime, int toTime)
        {
            Tick = tick; ProjectileId = projectileId; Shooter = shooter;
            FromX = fromX; FromY = fromY; ToX = toX; ToY = toY;
            HitId = hitId; State = state; FromTime = fromTime; ToTime = toTime;
        }
        public long Tick { get; }
        public long ProjectileId { get; }
        public EntityId Shooter { get; }
        public int FromX { get; }
        public int FromY { get; }
        public int ToX { get; }
        public int ToY { get; }
        public EntityId HitId { get; }
        public CombatEventKind State { get; }
        public int FromTime { get; }
        public int ToTime { get; }
    }

    public readonly struct CombatVisualEvent
    {
        public CombatVisualEvent(long tick, long attackId, long projectileId, EntityId shooter,
            EntityId hitId, CombatEventKind state, int x, int y, int time, int damage)
        {
            Tick = tick; AttackId = attackId; ProjectileId = projectileId; Shooter = shooter;
            HitId = hitId; State = state; X = x; Y = y; Time = time; Damage = damage;
        }
        public long Tick { get; }
        public long AttackId { get; }
        public long ProjectileId { get; }
        public EntityId Shooter { get; }
        public EntityId HitId { get; }
        public CombatEventKind State { get; }
        public int X { get; }
        public int Y { get; }
        public int Time { get; }
        public int Damage { get; }
    }

    public readonly struct CombatDamageRecord
    {
        public CombatDamageRecord(EntityId target, EntityId source, bool targetIsAnimal, bool sourceIsAnimal,
            long tick, long attackId, long projectileId, int damage, bool healthDepleted)
        {
            Target = target; Source = source; TargetIsAnimal = targetIsAnimal; SourceIsAnimal = sourceIsAnimal;
            Tick = tick; AttackId = attackId; ProjectileId = projectileId; Damage = damage; HealthDepleted = healthDepleted;
        }
        public EntityId Target { get; }
        public EntityId Source { get; }
        public bool TargetIsAnimal { get; }
        public bool SourceIsAnimal { get; }
        public long Tick { get; }
        public long AttackId { get; }
        public long ProjectileId { get; }
        public int Damage { get; }
        public bool HealthDepleted { get; }
    }

    public sealed class CombatState
    {
        public const int UnitsPerCell = 1024;
        public const int TimeUnitsPerTick = 65536;
        public const int ArrowSpeed = 2048;
        public const int ArrowLifetime = 4;
        public const int MaxVelocity = 16 * UnitsPerCell;
        internal readonly List<CombatAttackState> MutableAttacks = new List<CombatAttackState>();
        internal readonly List<ProjectileState> MutableProjectiles = new List<ProjectileState>();
        internal readonly List<CombatDamageRecord> MutableDamageRecords = new List<CombatDamageRecord>();
        internal readonly Dictionary<int, GridPos> StartPositions = new Dictionary<int, GridPos>();
        internal long RecordedTick = -1;
        private List<CombatTraceSegment> _traces;
        private List<CombatVisualEvent> _events;

        public CombatState(ulong seed)
        {
            RngState = new DeterministicRng(seed).State;
            Attacks = MutableAttacks.AsReadOnly();
            Projectiles = MutableProjectiles.AsReadOnly();
            DamageRecords = MutableDamageRecords.AsReadOnly();
            BeginTraceTick();
        }

        public IReadOnlyList<CombatAttackState> Attacks { get; }
        public IReadOnlyList<ProjectileState> Projectiles { get; }
        public IReadOnlyList<CombatDamageRecord> DamageRecords { get; }
        public IReadOnlyList<CombatTraceSegment> TraceSegments { get; private set; }
        public IReadOnlyList<CombatVisualEvent> CombatEvents { get; private set; }
        public ulong RngState { get; internal set; }
        public long NextAttackId { get; internal set; } = 1;
        public long NextProjectileId { get; internal set; } = 1;
        public long LastUpdateTick { get; internal set; } = -1;
        public long ShotsReleased { get; internal set; }
        public long Hits { get; internal set; }
        public long Misses { get; internal set; }
        public long AnimalKills { get; internal set; }
        public long NpcHits { get; internal set; }
        public long Counterattacks { get; internal set; }
        public long ArrowsRecovered { get; internal set; }
        public long ArrowsLost { get; internal set; }

        public CombatAttackState GetAttack(EntityId attacker) => MutableAttacks.Find(a => a.Attacker == attacker);
        public CombatDamageRecord? GetLastDamage(EntityId target)
        {
            foreach (var record in DamageRecords) { if (record.Target == target) { return record; } }
            return null;
        }
        public long Count(ItemKind item) => item == ItemKind.Arrow ? MutableProjectiles.Count : 0;

        internal void BeginTraceTick()
        {
            // Replace rather than Clear: retained TickResults cannot change next minute.
            _traces = new List<CombatTraceSegment>();
            _events = new List<CombatVisualEvent>();
            TraceSegments = _traces.AsReadOnly();
            CombatEvents = _events.AsReadOnly();
        }
        public IReadOnlyList<CombatTraceSegment> CaptureTraceSegments() => Array.AsReadOnly(_traces.ToArray());
        public IReadOnlyList<CombatVisualEvent> CaptureCombatEvents() => Array.AsReadOnly(_events.ToArray());
        internal void AddTrace(CombatTraceSegment trace) => _traces.Add(trace);
        internal void AddEvent(CombatVisualEvent combatEvent) => _events.Add(combatEvent);
        internal GridPos StartPosition(EntityId id, GridPos fallback) => StartPositions.TryGetValue(id.Value, out GridPos start) ? start : fallback;
        internal void RecordDamage(CombatDamageRecord record)
        {
            int index = MutableDamageRecords.FindIndex(r => r.Target.Value >= record.Target.Value);
            if (index >= 0 && MutableDamageRecords[index].Target == record.Target) { MutableDamageRecords[index] = record; }
            else { MutableDamageRecords.Insert(index < 0 ? MutableDamageRecords.Count : index, record); }
        }
    }
}
