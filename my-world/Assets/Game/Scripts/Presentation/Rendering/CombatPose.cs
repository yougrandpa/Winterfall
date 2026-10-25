using MyAiWorld.App.Query;

namespace MyAiWorld.Presentation.Rendering
{
    internal static class CombatPose
    {
        // A phase owns its frames: drawing a bow never loops back to frame zero.
        internal static int Frame(AttackSnapshot attack, long tick, float alpha)
        {
            return CombatAnimation.Frame(attack, tick, alpha);
        }
    }
}
