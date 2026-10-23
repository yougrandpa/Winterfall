using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    // Versioned canonical binary authority, suitable for both world saves and hashing.
    // No trace packets, recorded movement, or presentation facts enter this format.
    public static class CombatSnapshot
    {
        public const int CurrentVersion = 2;
        private const int MaxRecords = 1000000;

        public static byte[] Capture(WorldState world)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                Write(writer, world);
                return stream.ToArray();
            }
        }

        public static void Write(BinaryWriter writer, WorldState world)
        {
            CombatState state = world.Combat;
            writer.Write(CurrentVersion);
            writer.Write(state.RngState); writer.Write(state.NextAttackId); writer.Write(state.NextProjectileId); writer.Write(state.LastUpdateTick);
            writer.Write(state.ShotsReleased); writer.Write(state.Hits); writer.Write(state.Misses); writer.Write(state.AnimalKills);
            writer.Write(state.NpcHits); writer.Write(state.Counterattacks); writer.Write(state.ArrowsRecovered); writer.Write(state.ArrowsLost);
            writer.Write(state.Attacks.Count);
            foreach (CombatAttackState attack in state.Attacks)
            {
                writer.Write(attack.Id); writer.Write(attack.Attacker.Value); writer.Write(attack.Target.Value);
                writer.Write(attack.AttackerIsAnimal); writer.Write(attack.TargetIsAnimal);
                writer.Write((int)attack.Kind); writer.Write((int)attack.Phase);
                writer.Write(attack.StartTick); writer.Write(attack.ReleaseTick); writer.Write(attack.ReleasedTick);
                writer.Write(attack.RecoveryUntilTick); writer.Write(attack.CooldownUntilTick);
                writer.Write(attack.FacingX); writer.Write(attack.FacingY); writer.Write(attack.IsCounterattack);
                writer.Write(attack.Region); writer.Write(attack.HabitatId);
            }
            writer.Write(state.Projectiles.Count);
            foreach (ProjectileState arrow in state.Projectiles)
            {
                writer.Write(arrow.Id); writer.Write(arrow.Shooter.Value); writer.Write(arrow.AttackId); writer.Write((int)arrow.Phase);
                writer.Write(arrow.X); writer.Write(arrow.Y); writer.Write(arrow.VelocityX); writer.Write(arrow.VelocityY);
                writer.Write(arrow.ReleasedTick); writer.Write(arrow.LastAdvancedTick); writer.Write(arrow.RemainingTicks);
                writer.Write(arrow.Damage); writer.Write(arrow.Integrity);
                writer.Write(arrow.EmbeddedTarget.Value); writer.Write(arrow.EmbeddedTargetIsAnimal);
                writer.Write(arrow.LastWalkablePosition.X); writer.Write(arrow.LastWalkablePosition.Y);
                writer.Write(arrow.IgnoreInitialShooterOverlap);
            }
            writer.Write(state.DamageRecords.Count);
            foreach (CombatDamageRecord record in state.DamageRecords)
            {
                writer.Write(record.Target.Value); writer.Write(record.Source.Value);
                writer.Write(record.TargetIsAnimal); writer.Write(record.SourceIsAnimal); writer.Write(record.Tick);
                writer.Write(record.AttackId); writer.Write(record.ProjectileId); writer.Write(record.Damage); writer.Write(record.HealthDepleted);
            }
        }

        public static void Restore(byte[] bytes, WorldState world)
        {
            if (bytes == null || bytes.Length > 64 * 1024 * 1024) { throw new InvalidDataException("Invalid combat snapshot size."); }
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                CombatState candidate = Decode(reader);
                Require(stream.Position == stream.Length, "Trailing combat data.");
                Validate(world, candidate);
                Publish(world, candidate);
            }
        }

        // Call after NPC/animal state and NextEntityId have been restored into the candidate world.
        public static void Read(BinaryReader reader, WorldState world)
        {
            CombatState candidate = Decode(reader);
            Validate(world, candidate);
            Publish(world, candidate);
        }

        private static CombatState Decode(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            Require(version == CurrentVersion, "Unsupported combat version.");
            ulong rng = reader.ReadUInt64();
            var state = new CombatState(rng) { RngState = rng };
            state.NextAttackId = reader.ReadInt64(); state.NextProjectileId = reader.ReadInt64(); state.LastUpdateTick = reader.ReadInt64();
            state.ShotsReleased = reader.ReadInt64(); state.Hits = reader.ReadInt64(); state.Misses = reader.ReadInt64(); state.AnimalKills = reader.ReadInt64();
            state.NpcHits = reader.ReadInt64(); state.Counterattacks = reader.ReadInt64(); state.ArrowsRecovered = reader.ReadInt64(); state.ArrowsLost = reader.ReadInt64();
            int count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                var attack = new CombatAttackState(reader.ReadInt64(), new EntityId(reader.ReadInt32()), new EntityId(reader.ReadInt32()))
                {
                    AttackerIsAnimal = reader.ReadBoolean(), TargetIsAnimal = reader.ReadBoolean(),
                    Kind = (CombatAttackKind)reader.ReadInt32(), Phase = (CombatAttackPhase)reader.ReadInt32(),
                    StartTick = reader.ReadInt64(), ReleaseTick = reader.ReadInt64(), ReleasedTick = reader.ReadInt64(),
                    RecoveryUntilTick = reader.ReadInt64(), CooldownUntilTick = reader.ReadInt64(),
                    FacingX = reader.ReadInt32(), FacingY = reader.ReadInt32(), IsCounterattack = reader.ReadBoolean(),
                    Region = reader.ReadInt32(), HabitatId = reader.ReadInt32()
                };
                state.MutableAttacks.Add(attack);
            }
            count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                var arrow = new ProjectileState(reader.ReadInt64(), new EntityId(reader.ReadInt32()))
                {
                    AttackId = reader.ReadInt64(), Phase = (ProjectilePhase)reader.ReadInt32(),
                    X = reader.ReadInt32(), Y = reader.ReadInt32(), VelocityX = reader.ReadInt32(), VelocityY = reader.ReadInt32(),
                    ReleasedTick = reader.ReadInt64(), LastAdvancedTick = reader.ReadInt64(), RemainingTicks = reader.ReadInt32(),
                    Damage = reader.ReadInt32(), Integrity = reader.ReadInt32(),
                    EmbeddedTarget = new EntityId(reader.ReadInt32()), EmbeddedTargetIsAnimal = reader.ReadBoolean(),
                    LastWalkablePosition = new GridPos(reader.ReadInt32(), reader.ReadInt32()), IgnoreInitialShooterOverlap = reader.ReadBoolean()
                };
                state.MutableProjectiles.Add(arrow);
            }
            count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                state.MutableDamageRecords.Add(new CombatDamageRecord(new EntityId(reader.ReadInt32()), new EntityId(reader.ReadInt32()),
                    reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadBoolean()));
            }
            return state;
        }

        public static void Validate(WorldState world) => Validate(world, world.Combat);

        private static void Validate(WorldState world, CombatState state)
        {
            Require(world.Tick >= 0 && world.Tick <= long.MaxValue - 8 && world.NextEntityId > 0, "Invalid world combat clock/IDs.");
            Require(state.RngState != 0 && state.NextAttackId > 0 && state.NextAttackId < long.MaxValue &&
                state.NextProjectileId > 0 && state.NextProjectileId < long.MaxValue &&
                state.LastUpdateTick >= -1 && state.LastUpdateTick <= world.Tick, "Invalid combat clock/IDs/RNG.");
            Require(state.ShotsReleased >= 0 && state.Hits >= 0 && state.Misses >= 0 && state.AnimalKills >= 0 &&
                state.NpcHits >= 0 && state.Counterattacks >= 0 && state.ArrowsRecovered >= 0 && state.ArrowsLost >= 0 &&
                state.AnimalKills <= state.Hits && state.NpcHits <= state.Hits, "Invalid combat counters.");
            Require(state.Attacks.Count <= MaxRecords && state.Projectiles.Count <= MaxRecords && state.DamageRecords.Count <= MaxRecords, "Too many combat records.");
            var attackIds = new HashSet<long>();
            int previousActor = 0;
            foreach (CombatAttackState attack in state.Attacks)
            {
                Require(attack.Id > 0 && attack.Id < state.NextAttackId && attackIds.Add(attack.Id) &&
                    Historical(world, attack.Attacker) && Historical(world, attack.Target) && attack.Attacker != attack.Target &&
                    attack.Attacker.Value > previousActor, "Duplicate/invalid attack actor or target.");
                previousActor = attack.Attacker.Value;
                Require(Enum.IsDefined(typeof(CombatAttackKind), attack.Kind) && Enum.IsDefined(typeof(CombatAttackPhase), attack.Phase) &&
                    (attack.AttackerIsAnimal ? attack.Kind == CombatAttackKind.Bite || attack.Kind == CombatAttackKind.Gore :
                        (attack.Kind == CombatAttackKind.Bow || attack.Kind == CombatAttackKind.Spear) && attack.TargetIsAnimal), "Invalid attack kind.");
                int preparation = attack.Kind == CombatAttackKind.Bow ? 2 : 1;
                Require(attack.StartTick >= 0 && attack.StartTick <= world.Tick && attack.StartTick <= long.MaxValue - 8 &&
                    attack.ReleaseTick == attack.StartTick + preparation && attack.RecoveryUntilTick == attack.ReleaseTick + 1 &&
                    attack.CooldownUntilTick == attack.ReleaseTick + 3 && Math.Abs((long)attack.FacingX) <= (long)world.Map.Width * CombatState.UnitsPerCell &&
                    Math.Abs((long)attack.FacingY) <= (long)world.Map.Height * CombatState.UnitsPerCell, "Invalid attack timing/facing.");
                Require(attack.Region == -1 ? attack.HabitatId == -1 :
                    CombatSystem.ValidRegionalHabitat(world, attack.Region, attack.HabitatId) &&
                    attack.AttackerIsAnimal && attack.TargetIsAnimal && attack.FacingX == 0 && attack.FacingY == 0,
                    "Invalid attack region/habitat or regional geometry.");
                if (attack.Phase == CombatAttackPhase.Preparing)
                {
                    Require(attack.ReleasedTick == -1 && world.Tick <= attack.ReleaseTick &&
                        (attack.Region >= 0 ? CombatSystem.RegionalExecutable(world, attack) :
                            CombatSystem.TryPosition(world, attack.Attacker, attack.AttackerIsAnimal, out GridPos _) &&
                            CombatSystem.TryPosition(world, attack.Target, attack.TargetIsAnimal, out GridPos _)),
                        "Dangling/nonlocal executable attack.");
                }
                else
                {
                    Require(attack.ReleasedTick == attack.ReleaseTick && attack.ReleasedTick <= world.Tick &&
                        world.Tick <= attack.CooldownUntilTick && (attack.Phase == CombatAttackPhase.Recovery ?
                            world.Tick <= attack.RecoveryUntilTick : world.Tick >= attack.RecoveryUntilTick), "Invalid released attack timing.");
                    // Recovery/cooldown never execute damage; dead/historical sources and targets are valid.
                }
            }
            long previousProjectile = 0;
            foreach (ProjectileState arrow in state.Projectiles)
            {
                Require(arrow.Id > previousProjectile && arrow.Id < state.NextProjectileId && Historical(world, arrow.Shooter) &&
                    arrow.AttackId > 0 && arrow.AttackId < state.NextAttackId && Enum.IsDefined(typeof(ProjectilePhase), arrow.Phase), "Invalid projectile identity.");
                previousProjectile = arrow.Id;
                Require(arrow.ReleasedTick >= 0 && arrow.ReleasedTick <= world.Tick && arrow.LastAdvancedTick >= arrow.ReleasedTick &&
                    arrow.LastAdvancedTick <= world.Tick && arrow.RemainingTicks >= 0 && arrow.RemainingTicks <= CombatState.ArrowLifetime &&
                    arrow.Damage > 0 && arrow.Damage <= 10000 && (arrow.Integrity == 0 || arrow.Integrity == 1) &&
                    arrow.X >= 0 && arrow.Y >= 0 && (long)arrow.X < (long)world.Map.Width * CombatState.UnitsPerCell &&
                    (long)arrow.Y < (long)world.Map.Height * CombatState.UnitsPerCell &&
                    world.Map.IsInside(arrow.LastWalkablePosition), "Invalid projectile position/lifetime/damage.");
                if (arrow.Phase == ProjectilePhase.Flying)
                {
                    Require(arrow.EmbeddedTarget.IsNone && !arrow.EmbeddedTargetIsAnimal && arrow.RemainingTicks > 0 &&
                        (arrow.VelocityX != 0 || arrow.VelocityY != 0) && Math.Abs((long)arrow.VelocityX) <= CombatState.MaxVelocity &&
                        Math.Abs((long)arrow.VelocityY) <= CombatState.MaxVelocity &&
                        arrow.LastAdvancedTick - arrow.ReleasedTick == CombatState.ArrowLifetime - arrow.RemainingTicks,
                        "Invalid flying projectile.");
                }
                else
                {
                    Require(Historical(world, arrow.EmbeddedTarget) && arrow.VelocityX == 0 && arrow.VelocityY == 0 &&
                        arrow.Integrity == 1 && arrow.RemainingTicks < CombatState.ArrowLifetime &&
                        arrow.LastAdvancedTick - arrow.ReleasedTick == CombatState.ArrowLifetime - arrow.RemainingTicks,
                        "Invalid embedded arrow.");
                    // A missing/dead/external target is a legitimate pending ground transfer.
                    // The position checks above still require the retained arrow to be local;
                    // never accept the external resident's invalid map coordinate.
                }
            }
            int previousTarget = 0;
            foreach (CombatDamageRecord record in state.DamageRecords)
            {
                Require(record.Target.Value > previousTarget && Historical(world, record.Target) && Historical(world, record.Source) &&
                    record.Tick >= 0 && record.Tick <= world.Tick && record.AttackId > 0 && record.AttackId < state.NextAttackId &&
                    record.ProjectileId >= 0 && record.ProjectileId < state.NextProjectileId && record.Damage > 0 && record.Damage <= 10000,
                    "Invalid damage attribution.");
                previousTarget = record.Target.Value;
            }
        }

        private static void Publish(WorldState world, CombatState candidate)
        {
            CombatState state = world.Combat;
            state.MutableAttacks.Clear(); state.MutableAttacks.AddRange(candidate.Attacks);
            state.MutableProjectiles.Clear(); state.MutableProjectiles.AddRange(candidate.Projectiles);
            state.MutableDamageRecords.Clear(); state.MutableDamageRecords.AddRange(candidate.DamageRecords);
            state.RngState = candidate.RngState; state.NextAttackId = candidate.NextAttackId; state.NextProjectileId = candidate.NextProjectileId;
            state.LastUpdateTick = candidate.LastUpdateTick; state.ShotsReleased = candidate.ShotsReleased;
            state.Hits = candidate.Hits; state.Misses = candidate.Misses; state.AnimalKills = candidate.AnimalKills;
            state.NpcHits = candidate.NpcHits; state.Counterattacks = candidate.Counterattacks;
            state.ArrowsRecovered = candidate.ArrowsRecovered; state.ArrowsLost = candidate.ArrowsLost;
            state.RecordedTick = -1;
            CombatSystem.RecordPositions(world);
        }
        private static bool Historical(WorldState world, EntityId id) => id.Value > 0 && id.Value < world.NextEntityId;
        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32(); Require(count >= 0 && count <= MaxRecords, "Invalid combat collection count."); return count;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidDataException(message); }
        }
    }
}
