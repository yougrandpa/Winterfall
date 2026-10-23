using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class AttackSnapshot
    {
        public AttackSnapshot(CombatAttackState attack)
        {
            if (attack == null) { throw new ArgumentNullException(nameof(attack)); }
            Id = attack.Id; Attacker = attack.Attacker; Target = attack.Target;
            AttackerIsAnimal = attack.AttackerIsAnimal; TargetIsAnimal = attack.TargetIsAnimal;
            Kind = attack.Kind; Phase = attack.Phase; StartTick = attack.StartTick;
            ReleaseTick = attack.ReleaseTick; ReleasedTick = attack.ReleasedTick;
            RecoveryUntilTick = attack.RecoveryUntilTick; CooldownUntilTick = attack.CooldownUntilTick;
            FacingX = attack.FacingX; FacingY = attack.FacingY; IsCounterattack = attack.IsCounterattack;
        }

        public long Id { get; }
        public EntityId Attacker { get; }
        public EntityId Target { get; }
        public bool AttackerIsAnimal { get; }
        public bool TargetIsAnimal { get; }
        public CombatAttackKind Kind { get; }
        public CombatAttackPhase Phase { get; }
        public long StartTick { get; }
        public long ReleaseTick { get; }
        public long ReleasedTick { get; }
        public long RecoveryUntilTick { get; }
        public long CooldownUntilTick { get; }
        public int FacingX { get; }
        public int FacingY { get; }
        public bool IsCounterattack { get; }
    }

    public sealed class ProjectileSnapshot
    {
        public ProjectileSnapshot(ProjectileState projectile)
        {
            if (projectile == null) { throw new ArgumentNullException(nameof(projectile)); }
            Id = projectile.Id; Shooter = projectile.Shooter; AttackId = projectile.AttackId;
            Ammunition = projectile.Ammunition; Phase = projectile.Phase;
            X = projectile.X; Y = projectile.Y; VelocityX = projectile.VelocityX; VelocityY = projectile.VelocityY;
            ReleasedTick = projectile.ReleasedTick; LastAdvancedTick = projectile.LastAdvancedTick;
            RemainingTicks = projectile.RemainingTicks; Damage = projectile.Damage; Integrity = projectile.Integrity;
            EmbeddedTarget = projectile.EmbeddedTarget; EmbeddedTargetIsAnimal = projectile.EmbeddedTargetIsAnimal;
            LastWalkablePosition = projectile.LastWalkablePosition;
            IgnoreInitialShooterOverlap = projectile.IgnoreInitialShooterOverlap;
        }

        public long Id { get; }
        public EntityId Shooter { get; }
        public long AttackId { get; }
        public ItemKind Ammunition { get; }
        public ProjectilePhase Phase { get; }
        public int X { get; }
        public int Y { get; }
        public int VelocityX { get; }
        public int VelocityY { get; }
        public long ReleasedTick { get; }
        public long LastAdvancedTick { get; }
        public int RemainingTicks { get; }
        public int Damage { get; }
        public int Integrity { get; }
        public EntityId EmbeddedTarget { get; }
        public bool EmbeddedTargetIsAnimal { get; }
        public GridPos LastWalkablePosition { get; }
        public bool IgnoreInitialShooterOverlap { get; }
    }

    // Each rebuild owns fresh DTOs/collections. Never retains writable combat state.
    public sealed class CombatReadModel
    {
        private readonly Dictionary<EntityId, AttackSnapshot> _actors = new Dictionary<EntityId, AttackSnapshot>();

        public CombatReadModel(CombatState combat)
        {
            if (combat == null) { throw new ArgumentNullException(nameof(combat)); }
            var attacks = new List<AttackSnapshot>();
            foreach (CombatAttackState attack in combat.Attacks)
            {
                var snapshot = new AttackSnapshot(attack);
                attacks.Add(snapshot); _actors[snapshot.Attacker] = snapshot;
            }
            var projectiles = new List<ProjectileSnapshot>();
            foreach (ProjectileState projectile in combat.Projectiles) { projectiles.Add(new ProjectileSnapshot(projectile)); }
            Attacks = attacks.AsReadOnly(); Projectiles = projectiles.AsReadOnly();
            ShotsReleased = combat.ShotsReleased; Hits = combat.Hits; Misses = combat.Misses;
            AnimalKills = combat.AnimalKills; NpcHits = combat.NpcHits; Counterattacks = combat.Counterattacks;
            ArrowsRecovered = combat.ArrowsRecovered; ArrowsLost = combat.ArrowsLost;
        }

        public IReadOnlyList<AttackSnapshot> Attacks { get; }
        public IReadOnlyList<ProjectileSnapshot> Projectiles { get; }
        public long ShotsReleased { get; }
        public long Hits { get; }
        public long Misses { get; }
        public long AnimalKills { get; }
        public long NpcHits { get; }
        public long Counterattacks { get; }
        public long ArrowsRecovered { get; }
        public long ArrowsLost { get; }
        public AttackSnapshot GetAttack(EntityId actor) => _actors.TryGetValue(actor, out AttackSnapshot attack) ? attack : null;
    }
}
