using MyAiWorld.App.Query;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.Rendering
{
    internal static class CombatPose
    {
        // A phase owns its frames: drawing a bow never loops back to frame zero.
        internal static int Frame(AttackSnapshot attack, long tick, float alpha)
        {
            long start = attack.Phase == CombatAttackPhase.Preparing ? attack.StartTick : attack.ReleasedTick;
            long end = attack.Phase == CombatAttackPhase.Preparing ? attack.ReleaseTick : attack.RecoveryUntilTick;
            if (attack.Kind == CombatAttackKind.Charge && attack.Phase != CombatAttackPhase.Preparing)
            {
                start = attack.ChargeRemaining > 0 ? attack.ReleasedTick : attack.RecoveryUntilTick - 2;
                end = attack.ChargeRemaining > 0 ? attack.ReleasedTick + 3 : attack.RecoveryUntilTick;
            }
            // Read models publish the next tick after committing the sampled minute.
            float progress = ((float)(tick - start - 1) + alpha) / Mathf.Max(1, end - start);
            return Mathf.Clamp(Mathf.FloorToInt(progress * 4f), 0, 3);
        }
    }
}
