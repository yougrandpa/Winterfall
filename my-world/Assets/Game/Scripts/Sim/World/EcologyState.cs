using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
    public enum AnimalSpecies
    {
        Rabbit, Boar, Deer
    }

    public sealed class AnimalDefinition
    {
        public static readonly AnimalDefinition Rabbit = new AnimalDefinition(30, 3, 8, 250, 18 * 1440);
        public static readonly AnimalDefinition Boar = new AnimalDefinition(70, 5, 25, 600, 24 * 1440);
        public static readonly AnimalDefinition Deer = new AnimalDefinition(45, 6, 5, 180, 30 * 1440);
        private AnimalDefinition(int health, int foodYield, int risk, int injury, int lifespan)
        {
            Health = health;
            FoodYield = foodYield;
            InjuryRisk = risk;
            InjuryDamage = injury;
            Lifespan = lifespan;
        }

        public int Health { get; }
        public int FoodYield { get; }
        public int InjuryRisk { get; }
        public int InjuryDamage { get; }
        public int Lifespan { get; }
        public static AnimalDefinition For(AnimalSpecies species) => species == AnimalSpecies.Rabbit ? Rabbit : species == AnimalSpecies.Deer ? Deer : Boar;
    }

    public sealed class AnimalState
    {
        internal AnimalState(EntityId id, AnimalSpecies species, GridPos position, ulong rngState, int ageTicks)
        {
            Id = id;
            Species = species;
            Position = position;
            RngState = rngState;
            AgeTicks = ageTicks;
            Health = Definition.Health;
        }

        public EntityId Id { get; }
        public AnimalSpecies Species { get; }
        public AnimalDefinition Definition => AnimalDefinition.For(Species);
        public GridPos Position { get; internal set; }
        public int Health { get; internal set; }
        public int AgeTicks { get; internal set; }
        public bool Fleeing { get; internal set; }
        public EntityId ReservedBy { get; internal set; }
        public ulong RngState { get; internal set; }
    }

    public sealed partial class EcologyState
    {
        public const int SpeciesCapacity = 24;
        public const int RefugePopulation = 4;
        private static readonly int[] Dx = { 0, 1, 0, -1 };
        private static readonly int[] Dy = { 1, 0, -1, 0 };
        private readonly List<AnimalState> _animals = new List<AnimalState>();

        public EcologyState(ulong seed)
        {
            Rng = new DeterministicRng(seed);
            Animals = _animals.AsReadOnly();
        }

        public IReadOnlyList<AnimalState> Animals { get; }
        public DeterministicRng Rng;
        public long BirthProgress { get; internal set; }
        public int Births { get; private set; }
        public int NaturalDeaths { get; private set; }
        public int HuntKills { get; private set; }
        public int Count(AnimalSpecies species)
        {
            int count = 0;
            for (int i = 0; i < _animals.Count; i++)
            {
                if (_animals[i].Species == species && _animals[i].Health > 0)
                {
                    count++;
                }
            }
            return count;
        }

        public AnimalState Find(int id) => _animals.Find(animal => animal.Id.Value == id);
        public bool CanHunt(AnimalState animal) => animal != null && animal.Health > 0 &&
            Count(animal.Species) > RefugePopulation;

        internal void Initialize(WorldState world, List<GridPos> reachable)
        {
            var habitat = new List<GridPos>();
            for (int i = 0; i < reachable.Count; i++)
            {
                int distance = reachable[i].ManhattanDistance(world.Center);
                if (distance >= 8 && distance <= 22)
                {
                    habitat.Add(reachable[i]);
                }
            }
            if (habitat.Count == 0)
            {
                habitat.AddRange(reachable);
            }
            for (int i = 0; i < 24 && habitat.Count > 0; i++)
            {
                Spawn(world, (AnimalSpecies)(i % 3), habitat[Rng.NextInt(0, habitat.Count)], Rng.NextInt(1440, 7200));
            }
        }

        internal AnimalState Spawn(WorldState world, AnimalSpecies species, GridPos cell, int ageTicks)
        {
            var animal = new AnimalState(world.AllocateEntityId(), species, cell,
                DeterministicRng.Mix(((ulong)Rng.NextUInt() << 32) | Rng.NextUInt()), ageTicks);
            _animals.Add(animal);
            return animal;
        }

        internal void Release(EntityId owner)
        {
            for (int i = 0; i < _animals.Count; i++)
            {
                if (_animals[i].ReservedBy == owner)
                {
                    _animals[i].ReservedBy = EntityId.None;
                }
            }
        }

        internal void Update(WorldState world, List<SimEvent> events)
        {
            for (int i = _animals.Count - 1; i >= 0; i--)
            {
                if (_animals[i].Health <= 0)
                {
                    _animals.RemoveAt(i);
                }
            }
            for (int i = 0; i < _animals.Count; i++)
            {
                AnimalState animal = _animals[i];
                animal.AgeTicks++;
                if (animal.AgeTicks >= animal.Definition.Lifespan)
                {
                    animal.Health = 0;
                    animal.ReservedBy = EntityId.None;
                    NaturalDeaths++;
                    events.Add(new SimEvent(SimEventKind.AnimalDied, world.Tick, animal.Id, animal.Species + " died of age."));
                    continue;
                }
                if ((world.Tick + animal.Id.Value) % (animal.Species == AnimalSpecies.Deer && animal.Fleeing ? 3 : animal.Fleeing ? 6 : 12) == 0)
                {
                    Move(world, animal);
                }
            }
            BirthProgress++;
            if (BirthProgress < 240)
            {
                return;
            }
            BirthProgress = 0;
            for (int speciesIndex = 0; speciesIndex < 3; speciesIndex++)
            {
                AnimalSpecies species = (AnimalSpecies)speciesIndex;
                if (Count(species) >= SpeciesCapacity)
                {
                    continue;
                }
                AnimalState parent = null;
                int adults = 0;
                for (int i = 0; i < _animals.Count; i++)
                {
                    if (_animals[i].Species == species && _animals[i].Health > 0 && _animals[i].AgeTicks >= 1440)
                    {
                        parent = parent ?? _animals[i];
                        adults++;
                    }
                }
                if (adults < 2)
                {
                    continue;
                }
                AnimalState child = Spawn(world, species, parent.Position, 0);
                Births++;
                events.Add(new SimEvent(SimEventKind.AnimalBorn, world.Tick, child.Id, species + " born at " + child.Position + "."));
            }
        }

        private static void Move(WorldState world, AnimalState animal)
        {
            NpcState threat = null;
            int nearest = 6;
            for (int i = 0; i < world.Entities.Npcs.Count; i++)
            {
                NpcState npc = world.Entities.Npcs[i];
                int distance = npc.Position.ManhattanDistance(animal.Position);
                if (npc.Survival.Life == LifeState.Active && npc.HasHuntingTool && distance < nearest)
                {
                    nearest = distance;
                    threat = npc;
                }
            }
            animal.Fleeing = threat != null;
            var rng = new DeterministicRng(animal.RngState);
            int start = rng.NextInt(0, 4);
            GridPos best = animal.Position;
            int score = threat != null ? nearest : int.MinValue;
            for (int direction = 0; direction < 4; direction++)
            {
                int index = (start + direction) % 4;
                GridPos cell = animal.Position.Offset(Dx[index], Dy[index]);
                if (!world.Map.IsWalkable(cell))
                {
                    continue;
                }
                int candidateScore = threat != null ? cell.ManhattanDistance(threat.Position) :
                    -Math.Abs(cell.ManhattanDistance(world.Center) - 14);
                if (candidateScore > score)
                {
                    score = candidateScore;
                    best = cell;
                }
            }
            animal.Position = best;
            animal.RngState = rng.State;
        }

        // Called only by the tick executor, after approach and reservation checks.
        internal bool Strike(WorldState world, NpcState npc, AnimalState animal, List<SimEvent> events)
        {
            if (!CanHunt(animal) || animal.ReservedBy != npc.Id || !npc.HasHuntingTool ||
                npc.Needs.Energy < 1800 || npc.Needs.Health < 3000 ||
                !HuntingSystem.CanStrike(world, npc, animal))
            {
                return false;
            }
            var rng = new DeterministicRng(npc.RngState);
            npc.Needs.Fatigue = Math.Max(0, npc.Needs.Fatigue - 100);
            if (npc.Position.ManhattanDistance(animal.Position) <= 1 && rng.Chance(animal.Definition.InjuryRisk))
            {
                npc.Needs.Health = Math.Max(0, npc.Needs.Health - animal.Definition.InjuryDamage);
                events.Add(new SimEvent(SimEventKind.HunterInjured, world.Tick, npc.Id, npc.Name + " injured hunting " + animal.Species + "."));
            }
            npc.RngState = rng.State;
            ItemKind weapon = HuntingSystem.Weapon(npc);
            ProductionSystem.Wear(world, npc, weapon);
            animal.Health = Math.Max(0, animal.Health - (weapon == ItemKind.Spear ? 28 : 20) - npc.HuntingSkill / 10);
            npc.HuntingSkill = Math.Min(100, npc.HuntingSkill + 1);
            if (animal.Health == 0)
            {
                int food = animal.Definition.FoodYield;
                var drop = world.Survival.Drop(animal.Position);
                drop.Inventory.TryAdd(ItemKind.Food, food); drop.ReservedBy = npc.Id; npc.Survival.HuntDropId = drop.Id;
                world.Materials.Produce(ItemKind.Food, food);
                animal.ReservedBy = EntityId.None;
                npc.HuntKills++;
                HuntKills++;
                events.Add(new SimEvent(SimEventKind.AnimalHunted, world.Tick, npc.Id,
                    npc.Name + " hunted " + animal.Species + " #" + animal.Id + "; food +" + food + "."));
            }
            return true;
        }
    }
}
