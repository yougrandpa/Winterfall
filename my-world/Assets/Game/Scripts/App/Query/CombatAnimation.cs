using System;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public static class CombatAnimation
    {
        public static int Frame(AttackSnapshot attack, long tick, float alpha)
        {
            if (attack == null) { throw new ArgumentNullException(nameof(attack)); }
            long start = attack.Phase == CombatAttackPhase.Preparing ? attack.StartTick : attack.ReleasedTick;
            long end = attack.Phase == CombatAttackPhase.Preparing ? attack.ReleaseTick : attack.RecoveryUntilTick;
            if (attack.Kind == CombatAttackKind.Charge && attack.Phase != CombatAttackPhase.Preparing)
            {
                start = attack.ChargeRemaining > 0 ? attack.ReleasedTick : attack.RecoveryUntilTick - 2;
                end = attack.ChargeRemaining > 0 ? attack.ReleasedTick + 3 : attack.RecoveryUntilTick;
            }
            // Read models publish the next tick after committing the sampled minute.
            float progress = ((float)(tick - start - 1) + alpha) / Math.Max(1, end - start);
            return Math.Max(0, Math.Min(3, (int)Math.Floor(progress * 4f)));
        }
    }
}
