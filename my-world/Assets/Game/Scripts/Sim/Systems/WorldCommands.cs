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

    public sealed class RequestHuntCommand : IWorldCommand
    {
        public RequestHuntCommand(EntityId targetId, IReadOnlyList<EntityId> participantIds = null)
        {
            TargetId = targetId;
            ParticipantIds = Array.AsReadOnly(participantIds == null ? Array.Empty<EntityId>() : new List<EntityId>(participantIds).ToArray());
        }
        public EntityId TargetId { get; }
        public IReadOnlyList<EntityId> ParticipantIds { get; }
        public string Describe() => "Request hunt " + TargetId.Value + " members " + string.Join(",", ParticipantIds);
        public bool Apply(WorldState world, CommandContext context)
        {
            if (!world.Campaign.CanAdvance)
            {
                context.Events.Add(new SimEvent(SimEventKind.CommandRejected, context.Tick, TargetId, "挑战已结算，无法发起狩猎"));
                return false;
            }
            bool result = HuntingCoordinator.TryRequest(world, TargetId, ParticipantIds, true, context.Events, out string reason);
            if (result && world.Campaign.Result == null) { world.Campaign.Assisted = true; }
            if (!result) { context.Events.Add(new SimEvent(SimEventKind.CommandRejected, context.Tick, TargetId, reason)); }
            return result;
        }
    }

    public sealed class CancelHuntCommand : IWorldCommand
    {
        public CancelHuntCommand(int taskId) { TaskId = taskId; }
        public int TaskId { get; }
        public string Describe() => "Cancel hunt " + TaskId;
        public bool Apply(WorldState world, CommandContext context)
        {
            HuntTaskState task = world.Hunting.Find(TaskId);
            if (!world.Campaign.CanAdvance || task == null || task.Stage == HuntStage.Retreating)
            {
                string reason = !world.Campaign.CanAdvance ? "挑战已结算，无法变更狩猎" :
                    task == null ? "狩猎任务不存在或已结束" : "队伍已在撤退";
                context.Events.Add(new SimEvent(SimEventKind.CommandRejected, context.Tick, task?.TargetId ?? EntityId.None, reason));
                return false;
            }
            HuntingCoordinator.Retreat(world, task, "玩家取消狩猎", context.Events);
            if (world.Campaign.Result == null) { world.Campaign.Assisted = true; }
            return true;
        }
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
