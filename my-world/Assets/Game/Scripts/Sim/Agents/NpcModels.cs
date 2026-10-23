using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Agents
{
    public enum NpcActionType
    {
        Idle,
        Move,
        Eat,
        Drink,
        Sleep,
        Gather,
        Work
    }

    public sealed class NpcActionState
    {
        public NpcActionType Type { get; internal set; } = NpcActionType.Idle;
        public string Label { get; internal set; } = "Resting";
        public GridPos Target { get; internal set; } = GridPos.Invalid;
        public long EndTick { get; internal set; }
        public List<GridPos> Path { get; } = new List<GridPos>();
        public int PathIndex { get; internal set; }

        public bool IsComplete(long tick)
        {
            if (Type != NpcActionType.Move)
            {
                return tick >= EndTick;
            }

            return PathIndex >= Path.Count;
        }

        public void SetIdle(string label, long endTick)
        {
            Type = NpcActionType.Idle;
            Label = label;
            EndTick = endTick;
            Target = GridPos.Invalid;
            Path.Clear();
            PathIndex = 0;
        }

        public void SetMove(string label, GridPos target, List<GridPos> path)
        {
            Type = NpcActionType.Move;
            Label = label;
            EndTick = 0;
            Target = target;
            Path.Clear();
            if (path != null)
            {
                Path.AddRange(path);
            }

            PathIndex = 0;
        }

        internal void SetTimed(NpcActionType type, string label, GridPos target, long endTick)
        {
            SetIdle(label, endTick);
            Type = type;
            Target = target;
        }

        public GridPos PeekNextStep()
        {
            if (Type != NpcActionType.Move || PathIndex < 0 || PathIndex >= Path.Count)
            {
                return GridPos.Invalid;
            }

            return Path[PathIndex];
        }

        public void AdvanceStep()
        {
            if (Type == NpcActionType.Move && PathIndex < Path.Count)
            {
                PathIndex++;
            }
        }
    }

    public sealed class NeedsState
    {
        public const int MaxValue = 10000;

        // All five values are satisfaction: 0 is critical, 10000 is fully satisfied.
        // Historical property names are retained for replay/client compatibility.
        public int Food => Hunger;
        public int Water => Thirst;
        public int Energy => Fatigue;

        public int Hunger { get; set; }
        public int Thirst { get; set; }
        public int Fatigue { get; set; }
        public int Health { get; set; }
        public int Safety { get; set; }

        public static NeedsState CreateDefault()
        {
            return new NeedsState
            {
                Hunger = 7800,
                Thirst = 8200,
                Fatigue = 8500,
                Health = 10000,
                Safety = 9000
            };
        }

        public string Summary =>
        "Food " + Percent(Hunger) + "%  Water " + Percent(Thirst) +
            "%  Energy " + Percent(Fatigue) + "%  Health " + Percent(Health) +
            "%  Safety " + Percent(Safety) + "%";

        private static int Percent(int value)
        {
            if (value < 0)
            {
                value = 0;
            }
            else if (value > MaxValue)
            {
                value = MaxValue;
            }

            return value / 100;
        }
    }

    public sealed class Personality
    {
        public int Diligence { get; set; }
        public int Caution { get; set; }
        public int Sociability { get; set; }
        public int Curiosity { get; set; }

        public string Summary =>
        "Diligence " + Diligence + "  Caution " + Caution +
            "  Sociability " + Sociability + "  Curiosity " + Curiosity;
    }

    public sealed class NpcIdentity
    {
        public NpcIdentity(string name, string role, int age)
        {
            Name = name;
            Role = role;
            Age = age;
        }

        public string Name { get; }
        public string Role { get; }
        public int Age { get; }
    }

    public sealed class NpcState
    {
        public NpcState(
            EntityId id,
            NpcIdentity identity,
            Personality personality,
            GridPos home,
            ulong rngState)
        {
            Id = id;
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Personality = personality ?? throw new ArgumentNullException(nameof(personality));
            Home = home;
            Position = home;
            RngState = rngState;
            Needs = NeedsState.CreateDefault();
            Action = new NpcActionState();
            Action.SetIdle("Arriving", 0);
            LastDecision = NpcDecisionTrace.Empty;
            Inventory = new InventoryState(12);
            Plan = new NpcPlanState();
        }

        public EntityId Id { get; }
        public NpcIdentity Identity { get; }
        public Personality Personality { get; }
        public NeedsState Needs { get; }
        public GridPos Position { get; set; }
        public GridPos Home { get; }
        public ulong RngState { get; set; }
        public NpcActionState Action { get; }
        public NpcDecisionTrace LastDecision { get; set; }
        public InventoryState Inventory { get; }
        public NpcPlanState Plan { get; }

        public NpcSocialState Social { get; } = new NpcSocialState();

        public long Coins { get; internal set; } = 100;
        public SurvivalNpcState Survival { get; } = new SurvivalNpcState();
        public bool HasHuntingTool
        {
            get => Survival.Durability(ItemKind.Bow) > 0 || Survival.Durability(ItemKind.Spear) > 0;
            internal set { Survival.SetTool(ItemKind.Bow, value ? SurvivalRules.ToolDurability : 0); if (!value) { Survival.SetTool(ItemKind.Spear, 0); } }
        }
        public int HuntingSkill { get; internal set; } = 20;
        public int HuntKills { get; internal set; }
        public int GatheredFood { get; internal set; }
        public int HarvestedFood { get; internal set; }
        public int Purchases { get; internal set; }
        public int Sales { get; internal set; }

        public string Name => Identity.Name;
        public string Role => Identity.Role;
    }
}
