using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class HuntingSnapshot
    {
        public static void Write(BinaryWriter writer, WorldState world)
        {
            writer.Write(world.Hunting.NextTaskId);
            writer.Write(world.Hunting.Tasks.Count);
            foreach (HuntTaskState task in world.Hunting.Tasks)
            {
                writer.Write(task.Id);
                writer.Write(task.TargetId.Value);
                writer.Write(task.LeaderId.Value);
                writer.Write((int)task.Stage);
                writer.Write(task.StartedTick);
                writer.Write(task.DeadlineTick);
                writer.Write(task.LastSeenTick);
                Position(writer, task.LastSeenPosition);
                Position(writer, task.PreviousSeenPosition);
                Position(writer, task.RallyCell);
                writer.Write(task.PlayerRequested);
                writer.Write(task.Reason);
                writer.Write(task.Returnees.Count);
                foreach (HuntReturnState person in task.Returnees) { writer.Write(person.NpcId.Value); writer.Write(person.Returned); writer.Write(person.Dead); writer.Write(person.ReturnedTick); Position(writer, person.ReturnedCell); }
                foreach (int amount in task.Delivered) { writer.Write(amount); }
                writer.Write(task.Members.Count);
                foreach (HuntMemberState member in task.Members)
                {
                    writer.Write(member.NpcId.Value);
                    writer.Write((int)member.Role);
                    Position(writer, member.WorkCell);
                }
            }

            writer.Write(world.Hunting.Cooldowns.Count);
            foreach (var pair in world.Hunting.Cooldowns)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }

            writer.Write(world.Hunting.RecentResults.Count);
            foreach (HuntResultState result in world.Hunting.RecentResults)
            {
                writer.Write(result.Id); writer.Write(result.TargetId.Value); writer.Write(result.Tick); writer.Write(result.Success); writer.Write(result.Reason);
                foreach (int amount in result.Items) { writer.Write(amount); }
                writer.Write(result.Participants.Count);
                for (int i = 0; i < result.Participants.Count; i++) { writer.Write(result.Participants[i].Value); writer.Write(result.ReturnTicks[i]); Position(writer, result.ReturnCells[i]); }
            }
            writer.Write(world.Ecology.Animals.Count);
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                AnimalEncounterState state = animal.Encounter;
                writer.Write(animal.Id.Value);
                writer.Write((int)state.Phase);
                writer.Write(state.ThreatId.Value);
                writer.Write(state.ThreatIsAnimal);
                Position(writer, state.LastThreatPosition);
                writer.Write(state.ThreatUntilTick);
                writer.Write(state.AlertTick);
                writer.Write(state.NextStepTick);
                writer.Write(state.Stamina);
            }
        }

        public static void Read(BinaryReader reader, WorldState world)
        {
            world.Hunting.NextTaskId = reader.ReadInt32();
            int count = Count(reader, world.Entities.Npcs.Count);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                var target = new EntityId(reader.ReadInt32());
                var leader = new EntityId(reader.ReadInt32());
                var stage = (HuntStage)reader.ReadInt32();
                long started = reader.ReadInt64();
                var task = new HuntTaskState(id, target, leader, started)
                {Stage = stage, DeadlineTick = reader.ReadInt64(), LastSeenTick = reader.ReadInt64(), LastSeenPosition = Position(reader), PreviousSeenPosition = Position(reader), RallyCell = Position(reader), PlayerRequested = reader.ReadBoolean(), Reason = reader.ReadString()};
                int returnees = Count(reader, HuntingRules.MaxMembers);
                for (int n = 0; n < returnees; n++) { task.Returnees.Add(new HuntReturnState(new EntityId(reader.ReadInt32())) { Returned = reader.ReadBoolean(), Dead = reader.ReadBoolean(), ReturnedTick = reader.ReadInt64(), ReturnedCell = Position(reader) }); }
                for (int item = 0; item < task.Delivered.Length; item++) { task.Delivered[item] = reader.ReadInt32(); Require(task.Delivered[item] >= 0); }
                int members = Count(reader, HuntingRules.MaxMembers);
                for (int n = 0; n < members; n++)
                {
                    task.MutableMembers.Add(new HuntMemberState(new EntityId(reader.ReadInt32()), (HuntRole)reader.ReadInt32())
                    {WorkCell = Position(reader)});
                }

                world.Hunting.MutableTasks.Add(task);
            }

            count = Count(reader, world.Entities.Npcs.Count);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                long tick = reader.ReadInt64();
                Require(!world.Hunting.Cooldowns.ContainsKey(id));
                world.Hunting.Cooldowns.Add(id, tick);
            }

            count = Count(reader, 16);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32(); EntityId target = new EntityId(reader.ReadInt32()); long tick = reader.ReadInt64(); bool success = reader.ReadBoolean(); string reason = reader.ReadString();
                Require(id > 0 && id < world.Hunting.NextTaskId && tick >= 0 && tick <= world.Tick && reason.Length <= 512);
                var items = new int[(int)ItemKind.Count];
                for (int item = 0; item < items.Length; item++) { items[item] = reader.ReadInt32(); Require(items[item] >= 0); }
                int participants = Count(reader, HuntingRules.MaxMembers);
                var ids = new List<EntityId>(); var returnTicks = new List<long>(); var returnCells = new List<GridPos>();
                for (int n = 0; n < participants; n++) { EntityId person = new EntityId(reader.ReadInt32()); Require(world.Entities.TryGetNpc(person, out _)); ids.Add(person); returnTicks.Add(reader.ReadInt64()); returnCells.Add(Position(reader)); }
                world.Hunting.MutableResults.Add(new HuntResultState(id, target, tick, success, reason, items, ids, returnTicks, returnCells));
            }
            count = Count(reader, world.Ecology.Animals.Count);
            Require(count == world.Ecology.Animals.Count);
            for (int i = 0; i < count; i++)
            {
                Require(reader.ReadInt32() == world.Ecology.Animals[i].Id.Value);
                AnimalEncounterState state = world.Ecology.Animals[i].Encounter;
                state.Phase = (EncounterPhase)reader.ReadInt32();
                state.ThreatId = new EntityId(reader.ReadInt32());
                state.ThreatIsAnimal = reader.ReadBoolean();
                state.LastThreatPosition = Position(reader);
                state.ThreatUntilTick = reader.ReadInt64();
                state.AlertTick = reader.ReadInt64();
                state.NextStepTick = reader.ReadInt64();
                state.Stamina = reader.ReadInt32();
            }

            Validate(world);
        }

        public static void Validate(WorldState world)
        {
            Require(world.Hunting.NextTaskId > 0);
            var targets = new HashSet<int>();
            var members = new HashSet<int>();
            int previous = 0;
            foreach (HuntTaskState task in world.Hunting.Tasks)
            {
                Require(task.Id > previous && task.Id < world.Hunting.NextTaskId && targets.Add(task.TargetId.Value));
                previous = task.Id;
                Require((world.Ecology.Find(task.TargetId.Value) != null || world.Ecology.FindCarcass(task.TargetId.Value) != null || task.Stage == HuntStage.Returning || task.Stage == HuntStage.Retreating) && task.Members.Count <= HuntingRules.MaxMembers && (task.Member(task.LeaderId) != null || task.Members.Count == 0 && (task.Stage == HuntStage.Retreating || task.Stage == HuntStage.Returning)) && Enum.IsDefined(typeof(HuntStage), task.Stage) && task.StartedTick >= 0 && task.StartedTick <= world.Tick && task.DeadlineTick >= task.StartedTick && (task.LastSeenTick == -1 || task.LastSeenTick >= task.StartedTick && task.LastSeenTick <= world.Tick) && world.Map.IsWalkable(task.LastSeenPosition) && world.Map.IsWalkable(task.PreviousSeenPosition) && world.Map.IsWalkable(task.RallyCell) && task.Reason.Length <= 256);
                Require(task.Returnees.Count > 0 && task.Returnees.Count <= HuntingRules.MaxMembers);
                var returnIds = new HashSet<int>();
                foreach (HuntReturnState person in task.Returnees) { Require(returnIds.Add(person.NpcId.Value) && world.Entities.TryGetNpc(person.NpcId, out _) && (!person.Returned || person.ReturnedTick >= task.StartedTick && person.ReturnedTick <= world.Tick && world.Map.IsWalkable(person.ReturnedCell))); }
                int previousMember = 0;
                foreach (HuntMemberState member in task.Members)
                {
                    Require(member.NpcId.Value > previousMember && members.Add(member.NpcId.Value) && world.Entities.TryGetNpc(member.NpcId, out NpcState npc) && npc.Survival.Life == LifeState.Active && npc.Plan.IsActive && npc.Plan.Goal == MyAiWorld.Sim.Ai.NpcGoalType.Hunt && npc.Plan.CurrentStep.Type == MyAiWorld.Sim.Ai.PlanStepType.Hunt && npc.Plan.CurrentStep.ResourceId == task.TargetId.Value && Enum.IsDefined(typeof(HuntRole), member.Role) && (!member.WorkCell.IsValid || world.Map.IsWalkable(member.WorkCell)));
                    previousMember = member.NpcId.Value;
                }

                AnimalState prey = world.Ecology.Find(task.TargetId.Value);
                if (prey != null && prey.Health > 0 && task.Stage != HuntStage.Retreating && task.Stage != HuntStage.Returning)
                {
                    Require(prey.Region == -1 && prey.ReservedBy == task.LeaderId);
                }
            }

            foreach (var cooldown in world.Hunting.Cooldowns)
            {
                Require(world.Entities.TryGetNpc(new EntityId(cooldown.Key), out NpcState _) && cooldown.Value >= 0);
            }

            foreach (AnimalState animal in world.Ecology.Animals)
            {
                AnimalEncounterState state = animal.Encounter;
                Require(Enum.IsDefined(typeof(EncounterPhase), state.Phase) && state.Stamina >= 0 && state.Stamina <= HuntingRules.MaxStamina && state.ThreatId.Value >= 0 && state.ThreatId.Value < world.NextEntityId && (!state.LastThreatPosition.IsValid || world.Map.IsInside(state.LastThreatPosition)) && state.ThreatUntilTick >= 0 && state.AlertTick >= 0 && state.NextStepTick >= 0);
            }
        }

        private static void Position(BinaryWriter writer, GridPos pos)
        {
            writer.Write(pos.X);
            writer.Write(pos.Y);
        }

        private static GridPos Position(BinaryReader reader) => new GridPos(reader.ReadInt32(), reader.ReadInt32());
        private static int Count(BinaryReader reader, int maximum)
        {
            int count = reader.ReadInt32();
            Require(count >= 0 && count <= maximum);
            return count;
        }

        private static void Require(bool valid)
        {
            if (!valid)
            {
                throw new InvalidDataException("Invalid hunting task or encounter state.");
            }
        }
    }
}
