using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public sealed class EntityRegistry
    {
        private readonly List<NpcState> _npcs = new List<NpcState>();

        public IReadOnlyList<NpcState> Npcs => _npcs;

        public void AddNpc(NpcState npc)
        {
            if (npc == null)
            {
                throw new ArgumentNullException(nameof(npc));
            }

            int insertAt = _npcs.Count;
            while (insertAt > 0 && _npcs[insertAt - 1].Id.Value > npc.Id.Value)
            {
                insertAt--;
            }

            if (insertAt < _npcs.Count && _npcs[insertAt].Id == npc.Id)
            {
                throw new InvalidOperationException("Duplicate entity id: " + npc.Id);
            }

            _npcs.Insert(insertAt, npc);
        }

        public bool TryGetNpc(EntityId id, out NpcState npc)
        {
            for (int i = 0; i < _npcs.Count; i++)
            {
                if (_npcs[i].Id == id)
                {
                    npc = _npcs[i];
                    return true;
                }
            }

            npc = null;
            return false;
        }

        public bool IsCellOccupied(GridPos cell)
        {
            for (int i = 0; i < _npcs.Count; i++)
            {
                if (_npcs[i].Survival.Life != LifeState.Dead && _npcs[i].Position == cell)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class WorldState
    {
        public WorldState(WorldConfigData config, WorldMap map, DeterministicRng rng)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Rng = rng;
            Tick = config.StartTick;
            NextEntityId = 1;
            Entities = new EntityRegistry();
            Resources = new WorldResourceState(new GridPos(map.Width / 2, map.Height / 2));
            Ecology = new EcologyState(DeterministicRng.Mix((ulong)(uint)config.Seed ^ 0xEC0106UL));
            Market = new MarketState();
            Materials = new MaterialBalance();
        }

        public SurvivalWorldState Survival { get; } = new SurvivalWorldState();
        public int LivingCount { get { int count = 0; foreach (NpcState npc in Entities.Npcs) { if (npc.Survival.Life != LifeState.Dead) { count++; } } return count; } }
        public WorldConfigData Config { get; }
        public WorldMap Map { get; }
        public EntityRegistry Entities { get; }
        public WorldResourceState Resources { get; }
        public EcologyState Ecology { get; }
        public MarketState Market { get; }
        public MaterialBalance Materials { get; }
        public long Tick { get; set; }
        public int NextEntityId { get; set; }
        public DeterministicRng Rng;

        public MyAiWorld.Sim.Agents.SocialState Social { get; } = new MyAiWorld.Sim.Agents.SocialState();

        public GameDateTime Date => GameCalendar.FromTick(Tick);
        public GridPos Center => new GridPos(Map.Width / 2, Map.Height / 2);

        public EntityId AllocateEntityId()
        {
            return new EntityId(NextEntityId++);
        }
    }
}
