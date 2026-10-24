using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App.Query
{
    public sealed class HuntMemberSnapshot
    {
        internal HuntMemberSnapshot(HuntMemberState member)
        {
            NpcId = member.NpcId;
            Role = member.Role;
            WorkCell = member.WorkCell;
        }

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
        internal HuntTaskSnapshot(HuntTaskState task)
        {
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
                members.Add(new HuntMemberSnapshot(member));
            }

            Members = members.AsReadOnly();
        }

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
        internal HuntCandidateSnapshot(MyAiWorld.Sim.Agents.NpcState npc)
        {
            Id = npc.Id;
            Name = npc.Name;
            Skill = npc.HuntingSkill;
            HasSpear = npc.Survival.Durability(ItemKind.Spear) > 0;
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
    }

    public sealed class HuntingReadModel
    {
        public HuntingReadModel(WorldState world)
        {
            var tasks = new List<HuntTaskSnapshot>();
            foreach (HuntTaskState task in world.Hunting.Tasks)
            {
                tasks.Add(new HuntTaskSnapshot(task));
            }

            Tasks = tasks.AsReadOnly();
            var candidates = new List<HuntCandidateSnapshot>();
            foreach (var npc in world.Entities.Npcs)
            {
                if (HuntingCoordinator.Eligible(world, npc))
                {
                    candidates.Add(new HuntCandidateSnapshot(npc));
                }
            }

            Candidates = candidates.AsReadOnly();
        }

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
