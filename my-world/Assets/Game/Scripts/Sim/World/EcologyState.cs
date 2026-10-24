using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
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
            BodyMass = Definition.NewbornMass + (int)((long)(Definition.AdultMass - Definition.NewbornMass) *
                Math.Min(ageTicks, Definition.MaturityTicks) / Definition.MaturityTicks);
            Energy = Definition.EnergyCapacity * 2 / 3;
            Hydration = 2000;
            CoreTemperature = 3800;
            MaxAgeTicks = Definition.Lifespan;
        }

        public EntityId Id { get; }
        public AnimalSpecies Species { get; }
        public AnimalEncounterState Encounter { get; } = new AnimalEncounterState();
        public AnimalDefinition Definition => AnimalDefinition.For(Species);
        public GridPos Position { get; internal set; }
        public int Health { get; internal set; }
        public int AgeTicks { get; internal set; }
        public bool Fleeing { get; internal set; }
        public EntityId ReservedBy { get; internal set; }
        public ulong RngState { get; internal set; }
        public int Region { get; internal set; } = -1;
        public int HabitatId { get; internal set; }
        public int Energy { get; internal set; }
        public int BodyMass { get; internal set; }
        public int Hydration { get; internal set; }
        public int CoreTemperature { get; internal set; }
        public AnimalSex Sex { get; internal set; }
        public AnimalBehavior Behavior { get; internal set; }
        public int MaxAgeTicks { get; internal set; }
        public int GroupId { get; internal set; }
        public int TerritoryHabitatId { get; internal set; }
        public EntityId MotherId { get; internal set; }
        public EntityId FatherId { get; internal set; }
        public EntityId MateId { get; internal set; }
        public EntityId PregnancyFatherId { get; internal set; }
        public int PregnancyTicks { get; internal set; }
        public int PregnancyLitter { get; internal set; }
        public int ReproductionCooldown { get; internal set; }
        public EntityId TargetAnimalId { get; internal set; }
        public EntityId LastTargetId { get; internal set; }
        public long TargetInvalidatedTick { get; internal set; } = -1;
        public GridPos TargetPosition { get; internal set; } = GridPos.Invalid;
        public int MigrationDestination { get; internal set; } = -1;
        public long LastMigrationTick { get; internal set; } = -1;
        public List<GridPos> MigrationPath { get; } = new List<GridPos>();
        public int MigrationPathIndex { get; internal set; }
        public long LastNeedsTick { get; internal set; }
        public long NextMoveTick { get; internal set; }
        public long NextDecisionTick { get; internal set; }
        public int StarvationTicks { get; internal set; }
        public int DehydrationTicks { get; internal set; }
        public int ExposureTicks { get; internal set; }
        public bool DeadProcessed { get; internal set; }
        public AnimalDeathCause DeathCause { get; internal set; }
        public EntityId LastDamageSourceId { get; internal set; }
        public AnimalLifeStage LifeStage => AgeTicks < Definition.NursingTicks ? AnimalLifeStage.Infant :
            AgeTicks < Definition.MaturityTicks ? AnimalLifeStage.Juvenile :
            AgeTicks >= MaxAgeTicks * 4L / 5 ? AnimalLifeStage.Elder : AnimalLifeStage.Adult;
    }

    public sealed class CarcassState
    {
        internal CarcassState(AnimalState animal, long tick, int meat, int hide)
        {
            Id = animal.Id;
            Position = animal.Position;
            Species = animal.Species;
            Region = animal.Region;
            HabitatId = animal.HabitatId;
            MeatRemaining = meat;
            HideRemaining = hide;
            Natural = animal.DeathCause != AnimalDeathCause.Hunt && animal.DeathCause != AnimalDeathCause.Predation;
            SourceId = animal.LastDamageSourceId;
            DeathCause = animal.DeathCause;
            CreatedTick = tick;
            LastUpdateTick = tick;
            ReservedBy = animal.ReservedBy;
        }

        public EntityId Id { get; }
        public GridPos Position { get; }
        public AnimalSpecies Species { get; }
        public int Region { get; }
        public int HabitatId { get; }
        public int MeatRemaining { get; internal set; }
        public int HideRemaining { get; internal set; }
        public EntityId ReservedBy { get; internal set; }
        public bool Natural { get; }
        public EntityId SourceId { get; }
        public AnimalDeathCause DeathCause { get; }
        public int Freshness { get; internal set; } = 10000;
        public int DecayProgress { get; internal set; }
        public long CreatedTick { get; }
        public long LastUpdateTick { get; internal set; }
        // Only materialized items, such as embedded arrows. Uncut meat/hide is ecology.
        public InventoryState Inventory { get; } = new InventoryState(256);
    }

    public sealed partial class EcologyState
    {
        // Computational birth limit, never a survival/refuge rule.
        public const int SpeciesCapacity = 256;
        public const int RefugePopulation = 0;
        public const int PatchSize = 8;
        internal readonly List<AnimalState> MutableAnimals = new List<AnimalState>();
        internal readonly List<CarcassState> MutableCarcasses = new List<CarcassState>();
        internal readonly List<HabitatPatchState> MutableHabitats = new List<HabitatPatchState>();
        internal readonly List<AnimalGroupState> MutableGroups = new List<AnimalGroupState>();
        // Derived scratch indexes are rebuilt in stable animal order and never
        // serialized; they do not select targets or advance random streams.
        private readonly Dictionary<int, AnimalState> _groupLeaders = new Dictionary<int, AnimalState>();
        private readonly Dictionary<int, int> _groupStrengths = new Dictionary<int, int>();
        private int _patchColumns;
        private int _patchRows;
        private int _nextGroupId = 1;
        private bool _initialized;
        private long _lastUpdateTick = -1;
        private long _lastResourceTick = -1;

        public EcologyState(ulong seed)
        {
            Rng = new DeterministicRng(seed);
            Animals = MutableAnimals.AsReadOnly();
            Carcasses = MutableCarcasses.AsReadOnly();
            Habitats = MutableHabitats.AsReadOnly();
            Groups = MutableGroups.AsReadOnly();
            Regions = new EcologyRegionsState(DeterministicRng.Mix(seed ^ 0x4D494752415445UL));
        }

        // Includes external residents. Region == -1 is local; no external resident
        // has a valid Position, and all external rendering/query access must filter.
        public IReadOnlyList<AnimalState> Animals { get; }
        public IReadOnlyList<CarcassState> Carcasses { get; }
        public IReadOnlyList<HabitatPatchState> Habitats { get; }
        public IReadOnlyList<AnimalGroupState> Groups { get; }
        public EcologyRegionsState Regions { get; }
        public EcologyBalance Balance { get; } = new EcologyBalance();
        public DeterministicRng Rng;
        public long BirthProgress { get; internal set; }
        public int Births { get; private set; }
        public int NaturalDeaths { get; private set; }
        public int HuntKills { get; private set; }
        public int PredationDeaths { get; private set; }
        public long IntroducedAnimals { get; private set; }
        public long TotalDeaths => (long)NaturalDeaths + HuntKills + PredationDeaths;
        public bool Initialized => _initialized;

        public int Count(AnimalSpecies species)
        {
            return CountInRegion(species, -1);
        }

        public int CountInRegion(AnimalSpecies species, int region)
        {
            int count = 0;
            foreach (AnimalState animal in Animals)
            {
                if (animal.Species == species && animal.Region == region && animal.Health > 0)
                {
                    count++;
                }
            }
            return count;
        }

        public AnimalState Find(int id) => MutableAnimals.Find(animal => animal.Id.Value == id);
        public CarcassState FindCarcass(int id) => MutableCarcasses.Find(carcass => carcass.Id.Value == id);
        public AnimalGroupState FindGroup(int id) => MutableGroups.Find(group => group.Id == id);
        public HabitatPatchState FindHabitat(int id) => id >= 0 && id < MutableHabitats.Count ? MutableHabitats[id] : null;
        public HabitatPatchState HabitatAt(GridPos position)
        {
            if (!position.IsValid || position.X / PatchSize >= _patchColumns || position.Y / PatchSize >= _patchRows)
            {
                return null;
            }
            return FindHabitat(position.Y / PatchSize * _patchColumns + position.X / PatchSize);
        }

        public bool CanHunt(AnimalState animal) => animal != null && animal.Region == -1 && animal.Health > 0 && !animal.DeadProcessed;

        public void Initialize(WorldState world, List<GridPos> reachable)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("Ecology already initialized.");
            }
            _patchColumns = (world.Map.Width + PatchSize - 1) / PatchSize;
            _patchRows = (world.Map.Height + PatchSize - 1) / PatchSize;
            // Establish only boundary entrances connected to existing land, before
            // allocating regional patch IDs. This never redraws the village.
            MigrationSystem.Initialize(world);
            for (int py = 0; py < _patchRows; py++)
            {
                for (int px = 0; px < _patchColumns; px++)
                {
                    int land = 0;
                    int forest = 0;
                    for (int y = py * PatchSize; y < Math.Min(world.Map.Height, (py + 1) * PatchSize); y++)
                    {
                        for (int x = px * PatchSize; x < Math.Min(world.Map.Width, (px + 1) * PatchSize); x++)
                        {
                            var cell = new GridPos(x, y);
                            if (world.Map.IsWalkable(cell))
                            {
                                land++;
                                if (world.Map.GetTile(cell) == TileType.Forest)
                                {
                                    forest++;
                                }
                            }
                        }
                    }
                    AddHabitat(new HabitatPatchState(MutableHabitats.Count, -1, new GridPos(px * PatchSize, py * PatchSize),
                        land, forest, DeterministicRng.Mix(Rng.State ^ (ulong)(MutableHabitats.Count + 1))), world.Tick);
                }
            }
            foreach (EcologyRegionState region in Regions.Regions)
            {
                AddHabitat(new HabitatPatchState(region.HabitatId, region.Id, GridPos.Invalid, 4096, region.Id == 0 ? 2880 : 960,
                    DeterministicRng.Mix(Regions.RngState ^ (ulong)(region.Id + 1))), world.Tick);
            }
            var cells = new List<GridPos>();
            foreach (GridPos cell in world.Map.CollectReachable(world.Center))
            {
                if (cell.ManhattanDistance(world.Center) >= 8 && world.Map.GetTile(cell) != TileType.Farmland)
                {
                    cells.Add(cell);
                }
            }
            if (cells.Count == 0)
            {
                cells.AddRange(reachable);
            }
            int[] demographics = { 12, 8, 10, 20, 4, 4, 6 };
            for (int s = 0; s < demographics.Length && cells.Count > 0; s++)
            {
                AnimalDefinition definition = AnimalDefinition.For((AnimalSpecies)s);
                GridPos anchor = cells[Rng.NextInt(0, cells.Count)];
                for (int i = 0; i < demographics[s]; i++)
                {
                    if (i % definition.GroupSize == 0)
                    {
                        anchor = cells[Rng.NextInt(0, cells.Count)];
                    }
                    int age = i % 4 == 0 ? Rng.NextInt(definition.NursingTicks, definition.MaturityTicks) :
                        Rng.NextInt(definition.MaturityTicks, definition.Lifespan * 3 / 4);
                    AnimalState animal = Spawn(world, (AnimalSpecies)s, anchor, age);
                    animal.Sex = i % 2 == 0 ? AnimalSex.Female : AnimalSex.Male;
                }
            }
            // No initial off-map animals: every later return has departed or was
            // born to actual external parents. Baseline includes introduced stock.
            Balance.InitialBiomass = EcologyBalance.CurrentBiomass(this);
            Balance.InitialWater = EcologyBalance.CurrentWater(this);
            Balance.IntroducedBiomass = 0;
            Balance.IntroducedWater = 0;
            foreach (HabitatPatchState patch in Habitats)
            {
                Balance.InitialMineral += patch.MineralRemaining;
            }
            _initialized = true;
        }

        private void AddHabitat(HabitatPatchState patch, long tick)
        {
            if (patch.Id != MutableHabitats.Count)
            {
                throw new InvalidOperationException("Non-contiguous habitat IDs.");
            }
            patch.LastUpdateTick = tick;
            MutableHabitats.Add(patch);
        }

        public AnimalState Spawn(WorldState world, AnimalSpecies species, GridPos position, int ageTicks)
        {
            if (ageTicks < 0 || !world.Map.IsWalkable(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }
            var animal = CreateAnimal(world, species, position, ageTicks);
            IntroducedAnimals++;
            Balance.IntroducedBiomass += animal.BodyMass + animal.Energy;
            Balance.IntroducedWater += animal.Hydration;
            JoinGroup(animal);
            return animal;
        }

        internal AnimalState CreateAnimal(WorldState world, AnimalSpecies species, GridPos position, int ageTicks)
        {
            var animal = new AnimalState(world.AllocateEntityId(), species, position,
                DeterministicRng.Mix(((ulong)Rng.NextUInt() << 32) | Rng.NextUInt()), ageTicks);
            var rng = new DeterministicRng(animal.RngState);
            animal.Sex = rng.Chance(50) ? AnimalSex.Female : AnimalSex.Male;
            animal.Energy = animal.Definition.EnergyCapacity * rng.NextInt(50, 91) / 100;
            animal.Hydration = rng.NextInt(800, 2801);
            animal.MaxAgeTicks = animal.Definition.Lifespan * rng.NextInt(80, 121) / 100;
            animal.ReproductionCooldown = rng.NextInt(0, 3 * GameCalendar.TicksPerDay);
            animal.LastNeedsTick = world.Tick;
            animal.NextMoveTick = world.Tick + rng.NextInt(1, animal.Definition.MoveTicks + 1);
            animal.NextDecisionTick = world.Tick;
            animal.RngState = rng.State;
            HabitatPatchState patch = HabitatAt(position);
            animal.HabitatId = patch == null ? 0 : patch.Id;
            animal.TerritoryHabitatId = animal.HabitatId;
            MutableAnimals.Add(animal);
            return animal;
        }

        internal void JoinGroup(AnimalState animal)
        {
            foreach (AnimalGroupState group in Groups)
            {
                if (group.Species != animal.Species || group.Region != animal.Region || group.TerritoryHabitatId != animal.HabitatId)
                {
                    continue;
                }
                int members = 0;
                foreach (AnimalState member in Animals)
                {
                    if (member.GroupId == group.Id)
                    {
                        members++;
                    }
                }
                if (members < animal.Definition.GroupSize)
                {
                    animal.GroupId = group.Id;
                    animal.TerritoryHabitatId = group.TerritoryHabitatId;
                    return;
                }
            }
            var created = new AnimalGroupState(_nextGroupId++, animal.Species, animal.Region, animal.HabitatId, animal.Id,
                DeterministicRng.Mix(animal.RngState ^ 0x47524F5550UL));
            MutableGroups.Add(created);
            animal.GroupId = created.Id;
        }

        public void Release(EntityId owner)
        {
            foreach (AnimalState animal in Animals)
            {
                if (animal.ReservedBy == owner)
                {
                    animal.ReservedBy = EntityId.None;
                }
            }
            foreach (CarcassState carcass in Carcasses)
            {
                if (carcass.ReservedBy == owner)
                {
                    carcass.ReservedBy = EntityId.None;
                }
            }
        }

        public void Update(WorldState world, List<SimEvent> events)
        {
            if (!_initialized || _lastUpdateTick == world.Tick)
            {
                return;
            }
            _lastUpdateTick = world.Tick;
            BirthProgress++; // Total ecological minutes observed, not a spawn timer.
            EcologySystem.Update(world, events);
            MigrationSystem.Update(world, events);
            ResolveDeaths(world, events);
        }

        public void UpdateResources(WorldState world)
        {
            if (!_initialized || _lastResourceTick == world.Tick)
            {
                return;
            }
            _lastResourceTick = world.Tick;
            EcologySystem.UpdateResources(world);
        }

        internal void RegisterBirth(AnimalState child)
        {
            Births++;
            if (child.Region >= 0)
            {
                Regions.MutableRegions[child.Region].Births++;
            }
        }

        public void ResolveDeaths(WorldState world, List<SimEvent> events)
        {
            for (int i = 0; i < MutableAnimals.Count;)
            {
                AnimalState animal = MutableAnimals[i];
                if (animal.Health > 0)
                {
                    i++;
                    continue;
                }
                if (!animal.DeadProcessed)
                {
                    if (animal.DeathCause == AnimalDeathCause.None)
                    {
                        NpcState killer;
                        AnimalState predator = Find(animal.LastDamageSourceId.Value);
                        animal.DeathCause = world.Entities.TryGetNpc(animal.LastDamageSourceId, out killer) ?
                            AnimalDeathCause.Hunt : predator != null && predator.Definition.CanPreyOn(animal.Species) ?
                            AnimalDeathCause.Predation : AnimalDeathCause.Injury;
                    }
                    animal.DeadProcessed = true;
                    animal.Behavior = AnimalBehavior.Dead;
                    HabitatPatchState patch = FindHabitat(animal.HabitatId);
                    int biomass = animal.BodyMass + animal.Energy;
                    int ageScaledMeat = (animal.Definition.FoodYield * animal.BodyMass + animal.Definition.AdultMass - 1) / animal.Definition.AdultMass;
                    int ageScaledHide = animal.Definition.HideYield * animal.BodyMass / animal.Definition.AdultMass;
                    int meat = Math.Min(ageScaledMeat, biomass / 100);
                    int hide = Math.Min(ageScaledHide, (biomass - meat * 100) / 100);
                    patch.Litter += biomass - (meat + hide) * 100;
                    int returnedWater = Math.Min(animal.Hydration, patch.WaterCapacity - patch.Water);
                    patch.Water += returnedWater;
                    Balance.Runoff += animal.Hydration - returnedWater;
                    var carcass = new CarcassState(animal, world.Tick, meat, hide);
                    MutableCarcasses.Add(carcass);
                    MutableCarcasses.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
                    if (animal.DeathCause == AnimalDeathCause.Hunt)
                    {
                        HuntKills++;
                        NpcState killer;
                        if (world.Entities.TryGetNpc(animal.LastDamageSourceId, out killer))
                        {
                            killer.HuntKills++;
                        }
                    }
                    else if (animal.DeathCause == AnimalDeathCause.Predation)
                    {
                        PredationDeaths++;
                    }
                    else
                    {
                        NaturalDeaths++;
                    }
                    Balance.DeathCounts[(int)animal.DeathCause]++;
                    if (animal.PregnancyLitter > 0)
                    {
                        Balance.PregnanciesLost++;
                    }
                    if (animal.Region >= 0)
                    {
                        Regions.MutableRegions[animal.Region].Deaths++;
                    }
                    animal.ReservedBy = EntityId.None;
                    events.Add(new SimEvent(animal.DeathCause == AnimalDeathCause.Hunt ? SimEventKind.AnimalHunted : SimEventKind.AnimalDied,
                        world.Tick, animal.Id, animal.Species + " died: " + animal.DeathCause + "; carcass #" + carcass.Id.Value + "."));
                    foreach (AnimalState other in Animals)
                    {
                        if (other.TargetAnimalId == animal.Id)
                        {
                            other.LastTargetId = animal.Id;
                            other.TargetAnimalId = EntityId.None;
                            other.TargetInvalidatedTick = world.Tick;
                        }
                        if (other.MateId == animal.Id)
                        {
                            other.MateId = EntityId.None;
                        }
                    }
                }
                MutableAnimals.RemoveAt(i);
            }
            ReconcileGroups();
        }

        internal void ReconcileGroups()
        {
            _groupLeaders.Clear();
            _groupStrengths.Clear();
            foreach (AnimalState animal in Animals)
            {
                if (animal.Health <= 0)
                {
                    continue;
                }
                AnimalState leader;
                int strength;
                _groupLeaders.TryGetValue(animal.GroupId, out leader);
                _groupStrengths.TryGetValue(animal.GroupId, out strength);
                if (leader == null || animal.Health > leader.Health ||
                    animal.Health == leader.Health && animal.Id.Value < leader.Id.Value)
                {
                    _groupLeaders[animal.GroupId] = animal;
                }
                _groupStrengths[animal.GroupId] = strength + animal.Health;
            }
            for (int i = MutableGroups.Count - 1; i >= 0; i--)
            {
                AnimalGroupState group = MutableGroups[i];
                AnimalState leader;
                if (!_groupLeaders.TryGetValue(group.Id, out leader))
                {
                    MutableGroups.RemoveAt(i);
                    continue;
                }
                group.LeaderId = leader.Id;
                if (group.Region != leader.Region)
                {
                    group.TerritoryHabitatId = leader.HabitatId;
                }
                group.Region = leader.Region;
                group.Dominance = _groupStrengths[group.Id];
            }
        }

        internal bool Strike(WorldState world, NpcState npc, AnimalState animal, List<SimEvent> events)
        {
            return CanHunt(animal) && HuntingCoordinator.Authorized(world, npc, animal) &&
                CombatSystem.TryStartHunt(world, npc, animal, events);
        }

        internal bool HarvestCarcass(WorldState world, NpcState npc, CarcassState carcass, List<SimEvent> events)
        {
            if (carcass == null || FindCarcass(carcass.Id.Value) != carcass || carcass.Region != -1 ||
                npc.Survival.Life != LifeState.Active || npc.Position.ManhattanDistance(carcass.Position) > 1 ||
                !carcass.ReservedBy.IsNone && carcass.ReservedBy != npc.Id)
            {
                return false;
            }
            int room = npc.Inventory.Capacity - npc.Inventory.TotalCount;
            int meat = Math.Min(room, carcass.MeatRemaining);
            int hide = Math.Min(room - meat, carcass.HideRemaining);
            bool changed = false;
            if (meat > 0)
            {
                if (!npc.Inventory.TryAdd(ItemKind.RawMeat, meat))
                {
                    throw new InvalidOperationException("Carcass harvest precondition changed.");
                }
                carcass.MeatRemaining -= meat;
                world.Materials.Produce(ItemKind.RawMeat, meat);
                Balance.CarcassConversions[(int)ItemKind.RawMeat] += meat;
                Balance.MaterialBiomassProduced += meat * 100;
                changed = true;
            }
            if (hide > 0)
            {
                if (!npc.Inventory.TryAdd(ItemKind.Hide, hide))
                {
                    throw new InvalidOperationException("Carcass harvest precondition changed.");
                }
                carcass.HideRemaining -= hide;
                world.Materials.Produce(ItemKind.Hide, hide);
                Balance.CarcassConversions[(int)ItemKind.Hide] += hide;
                Balance.MaterialBiomassProduced += hide * 100;
                changed = true;
            }
            for (int item = 0; item < (int)ItemKind.Count; item++)
            {
                int quantity = Math.Min(carcass.Inventory.Count((ItemKind)item), npc.Inventory.Capacity - npc.Inventory.TotalCount);
                if (quantity > 0)
                {
                    carcass.Inventory.TryRemove((ItemKind)item, quantity);
                    npc.Inventory.TryAdd((ItemKind)item, quantity);
                    changed = true;
                }
            }
            if (changed)
            {
                events.Add(new SimEvent(SimEventKind.ResourceGathered, world.Tick, npc.Id,
                    "Harvest carcass #" + carcass.Id.Value + ": raw meat " + meat + ", hide " + hide + "."));
            }
            return changed;
        }

        public long MaterialCount(ItemKind item)
        {
            long total = 0;
            foreach (CarcassState carcass in Carcasses)
            {
                total += carcass.Inventory.Count(item);
            }
            return total;
        }
    }
}
