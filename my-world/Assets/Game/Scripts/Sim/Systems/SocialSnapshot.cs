using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.Systems
{
    public static partial class WorldSnapshot
    {
        private static void WriteSocial(BinaryWriter w, WorldState world)
        {
            w.Write(world.Social.NextSequence); w.Write(world.Social.Greetings); w.Write(world.Social.Gifts);
            w.Write(world.Social.Timeline.Count);
            foreach (SocialEvent evt in world.Social.Timeline) { WriteSocialEvent(w, evt); }
            foreach (NpcState npc in world.Entities.Npcs)
            {
                w.Write(npc.Social.NextInteractionTick); w.Write(npc.Social.Relations.Count);
                foreach (TrustRelation relation in npc.Social.Relations) { w.Write(relation.Other.Value); w.Write(relation.Trust); }
                w.Write(npc.Social.Memories.Count);
                foreach (SocialEvent evt in npc.Social.Memories) { WriteSocialEvent(w, evt); }
            }
        }
        private static void WriteSocialEvent(BinaryWriter w, SocialEvent evt)
        {
            w.Write(evt.Sequence); w.Write(evt.Tick); w.Write(evt.Actor.Value); w.Write(evt.Target.Value); w.Write((int)evt.Kind);
            w.Write(evt.TrustBefore); w.Write(evt.TrustAfter); w.Write(evt.Reason); w.Write(evt.CauseSequence);
        }
        private static SocialEvent ReadSocialEvent(BinaryReader r, WorldState world)
        {
            var evt = new SocialEvent(r.ReadInt64(), r.ReadInt64(), new EntityId(r.ReadInt32()), new EntityId(r.ReadInt32()), (SocialKind)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadString(), r.ReadInt64());
            NpcState actor, target;
            Require(evt.Sequence > 0 && evt.Sequence < world.Social.NextSequence && evt.Tick >= world.Config.StartTick && evt.Tick < world.Tick &&
                world.Entities.TryGetNpc(evt.Actor, out actor) && world.Entities.TryGetNpc(evt.Target, out target) && evt.Actor != evt.Target &&
                (evt.Kind == SocialKind.Greeting || evt.Kind == SocialKind.FoodGift) && evt.TrustBefore >= 0 && evt.TrustAfter >= evt.TrustBefore && evt.TrustAfter <= 100 &&
                evt.CauseSequence >= 0 && evt.CauseSequence < evt.Sequence);
            return evt;
        }
        private static void ReadSocial(BinaryReader r, WorldState world)
        {
            world.Social.NextSequence = r.ReadInt64(); world.Social.Greetings = r.ReadInt32(); world.Social.Gifts = r.ReadInt32();
            Require(world.Social.Greetings >= 0 && world.Social.Gifts >= 0 && world.Social.NextSequence == 1L + world.Social.Greetings + world.Social.Gifts);
            int count = ReadCount(r, SocialState.TimelineLimit); long previous = 0;
            for (int i = 0; i < count; i++)
            {
                SocialEvent evt = ReadSocialEvent(r, world); Require(evt.Sequence > previous); previous = evt.Sequence; world.Social.Append(evt);
            }
            foreach (NpcState npc in world.Entities.Npcs)
            {
                npc.Social.NextInteractionTick = r.ReadInt64(); count = ReadCount(r, world.Entities.Npcs.Count); int previousId = 0;
                Require(npc.Social.NextInteractionTick >= 0);
                for (int i = 0; i < count; i++)
                {
                    var id = new EntityId(r.ReadInt32()); int trust = r.ReadInt32(); NpcState other;
                    Require(id.Value > previousId && id != npc.Id && world.Entities.TryGetNpc(id, out other) && trust >= 0 && trust <= 100);
                    npc.Social.SetTrust(id, trust); previousId = id.Value;
                }
                count = ReadCount(r, NpcSocialState.MemoryLimit); previous = 0;
                for (int i = 0; i < count; i++)
                {
                    SocialEvent evt = ReadSocialEvent(r, world); Require(evt.Sequence > previous && (evt.Actor == npc.Id || evt.Target == npc.Id));
                    previous = evt.Sequence; npc.Social.Remember(evt);
                }
            }
        }
    }
}
