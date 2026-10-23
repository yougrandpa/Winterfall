using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.Ai
{
    public enum NpcGoalType
    {
        Rest,
        Wander,
        Eat,
        Drink,
        Sleep,
        GatherFood,
        GatherWater,
        Farm,
        Produce,
        Rescue,
        Treat,
        Hunt,
        Trade,
        Greet,
        GiveFood
    }

    public sealed class NpcDecisionOption
    {
        public NpcDecisionOption(string id, int score)
        {
            Id = id;
            Score = score;
        }

        public string Id { get; }
        public int Score { get; }
    }

    public sealed class NpcDecisionTrace
    {
        public string Goal { get; set; } = "Rest";
        public string ReasonCode { get; set; } = "initial";
        public int ChosenScore { get; set; }
        public List<NpcDecisionOption> Options { get; } = new List<NpcDecisionOption>();

        public static NpcDecisionTrace Empty
        {
            get
            {
                var trace = new NpcDecisionTrace();
                trace.ReasonCode = "none";
                return trace;
            }
        }

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < Options.Count; i++)
                {
                    parts.Add(Options[i].Id + "=" + Options[i].Score);
                }

                string scores = parts.Count == 0 ? "no candidates" : string.Join("  ", parts);
                return Goal + " / " + ReasonCode + "  [" + scores + "]";
            }
        }
    }

    public sealed class NpcDecision
    {
        public NpcDecision(
            NpcGoalType goal,
            GridPos target,
            int durationTicks,
            ulong updatedRngState,
            NpcDecisionTrace trace, EntityId targetEntity = default(EntityId))
        {
            TargetEntity = targetEntity;
            Goal = goal;
            Target = target;
            DurationTicks = durationTicks;
            UpdatedRngState = updatedRngState;
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public EntityId TargetEntity { get; }
        public NpcGoalType Goal { get; }
        public GridPos Target { get; }
        public int DurationTicks { get; }
        public ulong UpdatedRngState { get; }
        public NpcDecisionTrace Trace { get; }
    }

    public sealed class NpcDecisionContext
    {
        public NpcDecisionContext(NpcState npc, WorldMap map, long tick, WorldState world = null)
        {
            Npc = npc ?? throw new ArgumentNullException(nameof(npc));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Tick = tick;
            World = world;
        }

        public WorldState World { get; }
        public NpcState Npc { get; }
        public WorldMap Map { get; }
        public long Tick { get; }
    }

    public interface INpcDecisionPolicy
    {
        NpcDecision Decide(NpcDecisionContext context);
    }
}
