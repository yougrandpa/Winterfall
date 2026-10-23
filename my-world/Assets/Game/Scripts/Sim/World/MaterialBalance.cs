using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
    // Explicit sources and sinks make transfers independently auditable.
    public sealed partial class MaterialBalance
    {
        private readonly long[] _initial = new long[(int)ItemKind.Count];
        private readonly long[] _produced = new long[(int)ItemKind.Count];
        private readonly long[] _consumed = new long[(int)ItemKind.Count];

        public long Initial(ItemKind item) => _initial[(int)item];
        public long Produced(ItemKind item) => _produced[(int)item];
        public long Consumed(ItemKind item) => _consumed[(int)item];
        internal void Produce(ItemKind item, int quantity) => _produced[(int)item] += quantity;
        internal void Consume(ItemKind item, int quantity) => _consumed[(int)item] += quantity;

        internal void Initialize(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                _initial[i] = Count(world, (ItemKind)i);
                _produced[i] = 0; _consumed[i] = 0;
            }
        }

        public long Error(WorldState world, ItemKind item) => Count(world, item) -
            (Initial(item) + Produced(item) - Consumed(item));

        public static long Count(WorldState world, ItemKind item)
        {
            long total = world.Resources.Stockpile.Count(item) + world.Market.Inventory.Count(item);
            for (int i = 0; i < world.Entities.Npcs.Count; i++)
            {
                total += world.Entities.Npcs[i].Inventory.Count(item);
            }
            for (int i = 0; i < world.Resources.Nodes.Count; i++)
            {
                if (world.Resources.Nodes[i].Item == item)
                {
                    total += world.Resources.Nodes[i].Amount;
                }
            }
            total += world.Survival.Count(item);
            foreach (var npc in world.Entities.Npcs) { total += npc.Survival.EquipmentCount(item); }
            return total;
        }
    }
}
