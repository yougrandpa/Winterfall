using System;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class HuntingLocomotion
    {
        internal static void Refresh(WorldState world, NpcState npc)
        {
            SurvivalNpcState state = npc.Survival;
            if (state.LastStaminaTick != world.Tick)
            {
                state.LastStaminaTick = world.Tick;
                if (state.Life == LifeState.Active && world.Combat.GetAttack(npc.Id) == null)
                {
                    int recovery = state.RecoveringStamina ? 300 : state.HuntMotion == HuntMotion.Pursuit ? 0 : 140;
                    state.Stamina = Math.Min(10000, state.Stamina + recovery);
                }
            }
            if (state.Stamina < 2000) { state.RecoveringStamina = true; }
            if (state.Stamina >= 6000)
            {
                state.RecoveringStamina = false;
                if (state.HuntMotion == HuntMotion.Recover) { state.HuntMotion = HuntMotion.Walk; }
            }
            if (state.RecoveringStamina) { state.HuntMotion = HuntMotion.Recover; }
        }
    }
}
