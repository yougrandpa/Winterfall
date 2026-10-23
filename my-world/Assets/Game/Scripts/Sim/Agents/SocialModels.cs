using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.Agents
{
    public enum SocialKind { Greeting, FoodGift }

    public sealed class TrustRelation
    {
        internal TrustRelation(EntityId other, int trust) { Other = other; Trust = trust; }
        public EntityId Other { get; }
        public int Trust { get; internal set; }
    }

    public sealed class SocialEvent
    {
        internal SocialEvent(long sequence, long tick, EntityId actor, EntityId target, SocialKind kind,
            int trustBefore, int trustAfter, string reason, long causeSequence)
        {
            Sequence = sequence; Tick = tick; Actor = actor; Target = target; Kind = kind;
            TrustBefore = trustBefore; TrustAfter = trustAfter; Reason = reason; CauseSequence = causeSequence;
        }
        public long Sequence { get; }
        public long Tick { get; }
        public EntityId Actor { get; }
        public EntityId Target { get; }
        public SocialKind Kind { get; }
        // Target's directional trust in Actor. Actor's trust is independent.
        public int TrustBefore { get; }
        public int TrustAfter { get; }
        public string Reason { get; }
        public long CauseSequence { get; }
    }

    public sealed class NpcSocialState
    {
        public const int MemoryLimit = 32;
        private readonly List<TrustRelation> _relations = new List<TrustRelation>();
        private readonly List<SocialEvent> _memories = new List<SocialEvent>();
        public NpcSocialState() { Relations = _relations.AsReadOnly(); Memories = _memories.AsReadOnly(); }
        public IReadOnlyList<TrustRelation> Relations { get; }
        public IReadOnlyList<SocialEvent> Memories { get; }
        public long NextInteractionTick { get; internal set; }
        public int TrustIn(EntityId other)
        {
            foreach (TrustRelation r in _relations) { if (r.Other == other) { return r.Trust; } }
            return 0;
        }
        internal void SetTrust(EntityId other, int value)
        {
            int index = 0;
            while (index < _relations.Count && _relations[index].Other.Value < other.Value) { index++; }
            if (index < _relations.Count && _relations[index].Other == other) { _relations[index].Trust = value; }
            else { _relations.Insert(index, new TrustRelation(other, value)); }
        }
        internal void Remember(SocialEvent evt)
        {
            _memories.Add(evt);
            if (_memories.Count > MemoryLimit)
            {
                int expendable = _memories.FindIndex(memory => memory.Kind == SocialKind.Greeting);
                _memories.RemoveAt(expendable >= 0 ? expendable : 0);
            }
        }
        public long HelpReceivedFrom(EntityId other, EntityId self)
        {
            for (int i = _memories.Count - 1; i >= 0; i--)
            {
                SocialEvent evt = _memories[i];
                if (evt.Kind == SocialKind.FoodGift && evt.Actor == other && evt.Target == self) { return evt.Sequence; }
            }
            return 0;
        }
    }

    public sealed class SocialState
    {
        public const int TimelineLimit = 256;
        private readonly List<SocialEvent> _timeline = new List<SocialEvent>();
        public SocialState() { Timeline = _timeline.AsReadOnly(); }
        public IReadOnlyList<SocialEvent> Timeline { get; }
        public long NextSequence { get; internal set; } = 1;
        public int Greetings { get; internal set; }
        public int Gifts { get; internal set; }
        internal void Append(SocialEvent evt)
        {
            _timeline.Add(evt);
            if (_timeline.Count > TimelineLimit) { _timeline.RemoveAt(0); }
        }
    }
}
