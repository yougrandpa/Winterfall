using MyAiWorld.App.Query;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class CombatAnimationTests
    {
        [TestCase(CombatAttackKind.Bow, 361, 0f, 0)]
        [TestCase(CombatAttackKind.Bow, 361, 0.5f, 1)]
        [TestCase(CombatAttackKind.Bow, 362, 0f, 2)]
        [TestCase(CombatAttackKind.Bow, 363, 0f, 3)]
        [TestCase(CombatAttackKind.Spear, 361, 0f, 0)]
        [TestCase(CombatAttackKind.Spear, 361, 0.5f, 2)]
        [TestCase(CombatAttackKind.Spear, 362, 0f, 3)]
        public void Preparing_AdvancesWithinItsOwnAttackAndClampsAtTheEnd(CombatAttackKind kind, long tick, float alpha, int frame)
        {
            var attack = new CombatAttackState(7, new EntityId(1), new EntityId(2))
            {
                Kind = kind, Phase = CombatAttackPhase.Preparing, StartTick = 360,
                ReleaseTick = kind == CombatAttackKind.Bow ? 362 : 361, RecoveryUntilTick = 363
            };
            var snapshot = new AttackSnapshot(attack);
            Assert.That(CombatAnimation.Frame(snapshot, tick, alpha), Is.EqualTo(frame));
            Assert.That(CombatAnimation.Frame(snapshot, tick, alpha), Is.EqualTo(frame), "Repeating a paused sample cannot advance a frame.");
            attack.StartTick += 10000; attack.ReleaseTick += 10000; attack.RecoveryUntilTick += 10000;
            Assert.That(CombatAnimation.Frame(new AttackSnapshot(attack), tick + 10000, alpha), Is.EqualTo(frame));
        }

        [Test]
        public void Charge_RushAndRecoveryHaveIndependentProgressAndRetainedSnapshots()
        {
            var attack = new CombatAttackState(8, new EntityId(2), new EntityId(1))
            {
                Kind = CombatAttackKind.Charge, Phase = CombatAttackPhase.Recovery, StartTick = 360,
                ReleaseTick = 362, ReleasedTick = 362, RecoveryUntilTick = 367, ChargeRemaining = 3
            };
            var rush = new AttackSnapshot(attack);
            Assert.That(CombatAnimation.Frame(rush, 363, 0f), Is.Zero);
            Assert.That(CombatAnimation.Frame(rush, 364, 0.5f), Is.EqualTo(2));
            attack.ChargeRemaining = 0;
            attack.RecoveryUntilTick = 366;
            var recovery = new AttackSnapshot(attack);
            Assert.That(CombatAnimation.Frame(recovery, 365, 0f), Is.Zero);
            Assert.That(CombatAnimation.Frame(recovery, 366, 0f), Is.EqualTo(2));
            Assert.That(CombatAnimation.Frame(recovery, 400, 0f), Is.EqualTo(3));
            Assert.That(rush.ChargeRemaining, Is.EqualTo(3));
            Assert.That(CombatAnimation.Frame(rush, 363, 0f), Is.Zero);
        }
    }
}
