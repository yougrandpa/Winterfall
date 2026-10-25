using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class HuntMemberSnapshot
    {
        internal HuntMemberSnapshot(WorldState world, HuntMemberState member)
        {
            if (world.Entities.TryGetNpc(member.NpcId, out var npc))
            { Stamina = npc.Survival.Stamina; Energy = npc.Needs.Energy; Motion = npc.Survival.HuntMotion; Activity = npc.Action.Label; Food = npc.Inventory.Food; Water = npc.Inventory.Water; Coat = npc.Survival.CoatDurability > 0; }
            NpcId = member.NpcId;
            Role = member.Role;
            WorkCell = member.WorkCell;
        }

        public int Stamina { get; }
        public int Energy { get; }
        public HuntMotion Motion { get; }
        public string Activity { get; }
        public int Food { get; }
        public int Water { get; }
        public bool Coat { get; }
        public EntityId NpcId
        {
            get;
        }

        public HuntRole Role
        {
            get;
        }

        public GridPos WorkCell
        {
            get;
        }
    }

    public sealed class HuntTaskSnapshot
    {
        internal HuntTaskSnapshot(WorldState world, HuntTaskState task)
        {
            int pending = 0; foreach (var person in task.Returns) { if (!person.Returned && !person.Dead) { pending++; } }
            var participants = new List<EntityId>(); foreach (var person in task.Returns) { participants.Add(person.NpcId); }
            Participants = participants.AsReadOnly();
            PendingReturns = pending;
            Id = task.Id;
            TargetId = task.TargetId;
            LeaderId = task.LeaderId;
            Stage = task.Stage;
            LastSeenPosition = task.LastSeenPosition;
            DeadlineTick = task.DeadlineTick;
            Reason = task.Reason;
            var members = new List<HuntMemberSnapshot>();
            foreach (HuntMemberState member in task.Members)
            {
                members.Add(new HuntMemberSnapshot(world, member));
            }

            Members = members.AsReadOnly();
        }

        public IReadOnlyList<EntityId> Participants { get; }
        public int PendingReturns { get; }
        public int Id
        {
            get;
        }

        public EntityId TargetId
        {
            get;
        }

        public EntityId LeaderId
        {
            get;
        }

        public HuntStage Stage
        {
            get;
        }

        public GridPos LastSeenPosition
        {
            get;
        }

        public long DeadlineTick
        {
            get;
        }

        public string Reason
        {
            get;
        }

        public IReadOnlyList<HuntMemberSnapshot> Members
        {
            get;
        }
    }

    public sealed class HuntCandidateSnapshot
    {
        internal HuntCandidateSnapshot(WorldState world, MyAiWorld.Sim.Agents.NpcState npc)
        {
            Id = npc.Id;
            Name = npc.Name;
            Skill = npc.HuntingSkill;
            HasSpear = npc.Survival.Durability(ItemKind.Spear) > 0;
            HasBow = npc.Survival.Durability(ItemKind.Bow) > 0;
            HasArrows = npc.Inventory.Count(ItemKind.Arrow) > 0;
            CanPrepareBow = HuntingCoordinator.CanPrepareWeapon(world, npc, ItemKind.Bow);
            CanPrepareSpear = HuntingCoordinator.CanPrepareWeapon(world, npc, ItemKind.Spear);
        }

        public EntityId Id
        {
            get;
        }

        public string Name
        {
            get;
        }

        public int Skill
        {
            get;
        }

        public bool HasSpear
        {
            get;
        }

        public bool HasBow { get; }
        public bool HasArrows { get; }
        public bool CanPrepareBow { get; }
        public bool CanPrepareSpear { get; }
    }

    public sealed class HuntingReadModel
    {
        public HuntingReadModel(WorldState world)
        {
            RecentResults = new List<HuntResultState>(world.Hunting.RecentResults).AsReadOnly();
            var tasks = new List<HuntTaskSnapshot>();
            foreach (HuntTaskState task in world.Hunting.Tasks)
            {
                tasks.Add(new HuntTaskSnapshot(world, task));
            }

            Tasks = tasks.AsReadOnly();
            var candidates = new List<HuntCandidateSnapshot>();
            foreach (var npc in world.Entities.Npcs)
            {
                if (HuntingCoordinator.Eligible(world, npc))
                {
                    candidates.Add(new HuntCandidateSnapshot(world, npc));
                }
            }

            Candidates = candidates.AsReadOnly();
        }

        public IReadOnlyList<HuntResultState> RecentResults { get; }
        public IReadOnlyList<HuntTaskSnapshot> Tasks
        {
            get;
        }

        public IReadOnlyList<HuntCandidateSnapshot> Candidates
        {
            get;
        }

        public HuntTaskSnapshot ForTarget(EntityId id)
        {
            foreach (var task in Tasks)
            {
                if (task.TargetId == id)
                {
                    return task;
                }
            }

            return null;
        }

        public HuntTaskSnapshot ForNpc(EntityId id)
        {
            foreach (var task in Tasks)
            {
                foreach (EntityId participant in task.Participants) { if (participant == id) { return task; } }
                foreach (var member in task.Members)
                {
                    if (member.NpcId == id)
                    {
                        return task;
                    }
                }
            }

            return null;
        }
    }
}
