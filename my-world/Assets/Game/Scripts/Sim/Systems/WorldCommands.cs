using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public interface IWorldCommand
    {
        string Describe();
        bool Apply(WorldState world, CommandContext context);
    }

    public sealed class CommandContext
    {
        public CommandContext(long tick, List<SimEvent> events)
        {
            Tick = tick;
            Events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public long Tick { get; }
        public List<SimEvent> Events { get; }
    }

    public sealed class TradeCommand : IWorldCommand
    {
        public TradeCommand(TradeOrder order)
        {
            Order = order ?? throw new ArgumentNullException(nameof(order));
        }

        public TradeOrder Order { get; }
        public string Describe() => "Trade " + Order.Id + " " + Order.NpcId.Value + " " + (int)Order.Side +
            " " + (int)Order.Item + " " + Order.Quantity + " " + Order.UnitPrice;
        public bool Apply(WorldState world, CommandContext context) => world.Market.Execute(world, Order, context.Events);
    }

    public sealed class SpawnNpcCommand : IWorldCommand
    {
        private readonly GridPos? _requestedCell;

        public SpawnNpcCommand()
        {
        }

        public SpawnNpcCommand(GridPos requestedCell)
        {
            _requestedCell = requestedCell;
        }

        public GridPos? RequestedCell => _requestedCell;

        public string Describe()
        {
            return _requestedCell.HasValue
                ? "Spawn NPC at " + _requestedCell.Value
                : "Spawn NPC at a free cell";
        }

        public bool Apply(WorldState world, CommandContext context)
        {
            var npc = WorldFactory.SpawnNpc(world, _requestedCell, false, context.Events);
            if (npc != null)
            {
                return true;
            }

            context.Events.Add(new SimEvent(
                SimEventKind.CommandRejected,
                context.Tick,
                EntityId.None,
                "Spawn rejected: the requested cell is occupied or unwalkable."));
            return false;
        }
    }
}
