using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
    public sealed partial class EcologyState
    {
        private const int EcologySnapshotVersion = 1;

        public byte[] Capture()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteSnapshot(writer);
                return stream.ToArray();
            }
        }

        public void WriteSnapshot(BinaryWriter writer)
        {
            writer.Write(EcologySnapshotVersion);
            writer.Write(_initialized);
            writer.Write(_patchColumns);
            writer.Write(_patchRows);
            writer.Write(_nextGroupId);
            writer.Write(_lastUpdateTick);
            writer.Write(_lastResourceTick);
            writer.Write(Rng.State);
            writer.Write(BirthProgress);
            writer.Write(Births);
            writer.Write(NaturalDeaths);
            writer.Write(HuntKills);
            writer.Write(PredationDeaths);
            writer.Write(IntroducedAnimals);
            WriteBalance(writer);
            writer.Write(Regions.Enabled);
            writer.Write(Regions.RngState);
            writer.Write(Regions.Departures);
            writer.Write(Regions.Returns);
            writer.Write(Regions.LastUpdateTick);
            writer.Write(Regions.Regions.Count);
            foreach (EcologyRegionState region in Regions.Regions)
            {
                writer.Write(region.Id);
                WritePosition(writer, region.Exit);
                writer.Write(region.HabitatId);
                writer.Write(region.TemperatureOffset);
                writer.Write(region.LastUpdateTick);
                writer.Write(region.Immigration);
                writer.Write(region.Emigration);
                writer.Write(region.Births);
                writer.Write(region.Deaths);
            }
            writer.Write(Habitats.Count);
            foreach (HabitatPatchState patch in Habitats)
            {
                writer.Write(patch.Id);
                writer.Write(patch.Region);
                WritePosition(writer, patch.Origin);
                writer.Write(patch.LandCells);
                WriteVegetation(writer, patch.Herbaceous);
                WriteVegetation(writer, patch.Shrubs);
                WriteVegetation(writer, patch.Trees);
                writer.Write(patch.WaterCapacity);
                writer.Write(patch.Water);
                writer.Write(patch.SnowWater);
                writer.Write(patch.SnowDepth);
                writer.Write(patch.SoilFertility);
                writer.Write(patch.Litter);
                writer.Write(patch.MineralRemaining);
                writer.Write(patch.RngState);
                writer.Write(patch.LastUpdateTick);
            }
            writer.Write(Groups.Count);
            foreach (AnimalGroupState group in Groups)
            {
                writer.Write(group.Id);
                writer.Write((int)group.Species);
                writer.Write(group.Region);
                writer.Write(group.TerritoryHabitatId);
                writer.Write(group.LeaderId.Value);
                writer.Write(group.Dominance);
                writer.Write(group.Disputes);
                writer.Write(group.RngState);
                writer.Write(group.LastCompetitionTick);
            }
            writer.Write(Animals.Count);
            foreach (AnimalState animal in Animals)
            {
                WriteAnimal(writer, animal);
            }
            writer.Write(Carcasses.Count);
            foreach (CarcassState carcass in Carcasses)
            {
                writer.Write(carcass.Id.Value);
                WritePosition(writer, carcass.Position);
                writer.Write((int)carcass.Species);
                writer.Write(carcass.Region);
                writer.Write(carcass.HabitatId);
                writer.Write(carcass.MeatRemaining);
                writer.Write(carcass.HideRemaining);
                writer.Write(carcass.ReservedBy.Value);
                writer.Write(carcass.Natural);
                writer.Write(carcass.SourceId.Value);
                writer.Write((int)carcass.DeathCause);
                writer.Write(carcass.Freshness);
                writer.Write(carcass.DecayProgress);
                writer.Write(carcass.CreatedTick);
                writer.Write(carcass.LastUpdateTick);
                for (int i = 0; i < (int)ItemKind.Count; i++)
                {
                    writer.Write(carcass.Inventory.Count((ItemKind)i));
                }
            }
        }

        // Read on an unpublished candidate world. NPC/reference validation is
        // deliberately deferred until the caller has read all other modules.
        public void ReadSnapshot(BinaryReader reader)
        {
            RequireEcology(reader.ReadInt32() == EcologySnapshotVersion);
            _initialized = reader.ReadBoolean();
            _patchColumns = reader.ReadInt32();
            _patchRows = reader.ReadInt32();
            _nextGroupId = reader.ReadInt32();
            _lastUpdateTick = reader.ReadInt64();
            _lastResourceTick = reader.ReadInt64();
            Rng.State = reader.ReadUInt64();
            BirthProgress = reader.ReadInt64();
            Births = reader.ReadInt32();
            NaturalDeaths = reader.ReadInt32();
            HuntKills = reader.ReadInt32();
            PredationDeaths = reader.ReadInt32();
            IntroducedAnimals = reader.ReadInt64();
            ReadBalance(reader);
            Regions.Enabled = reader.ReadBoolean();
            Regions.RngState = reader.ReadUInt64();
            Regions.Departures = reader.ReadInt64();
            Regions.Returns = reader.ReadInt64();
            Regions.LastUpdateTick = reader.ReadInt64();
            Regions.MutableRegions.Clear();
            int count = ReadEcologyCount(reader, 8);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                GridPos exit = ReadPosition(reader);
                int patchId = reader.ReadInt32();
                int offset = reader.ReadInt32();
                var region = new EcologyRegionState(id, exit, patchId, offset)
                {
                    LastUpdateTick = reader.ReadInt64(),
                    Immigration = reader.ReadInt64(),
                    Emigration = reader.ReadInt64(),
                    Births = reader.ReadInt64(),
                    Deaths = reader.ReadInt64()
                };
                RequireEcology(id == i && Math.Abs((long)offset) <= 6000 && region.Immigration >= 0 &&
                    region.Emigration >= 0 && region.Births >= 0 && region.Deaths >= 0);
                Regions.MutableRegions.Add(region);
            }
            MutableHabitats.Clear();
            count = ReadEcologyCount(reader, 65538);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                int region = reader.ReadInt32();
                GridPos origin = ReadPosition(reader);
                int land = reader.ReadInt32();
                RequireEcology(id == i && land >= 0 && land <= 4096);
                var patch = new HabitatPatchState(id, region, origin, land, 0, 1);
                ReadVegetation(reader, patch.Herbaceous);
                ReadVegetation(reader, patch.Shrubs);
                ReadVegetation(reader, patch.Trees);
                patch.WaterCapacity = reader.ReadInt32();
                patch.Water = reader.ReadInt32();
                patch.SnowWater = reader.ReadInt32();
                patch.SnowDepth = reader.ReadInt32();
                patch.SoilFertility = reader.ReadInt32();
                patch.Litter = reader.ReadInt64();
                patch.MineralRemaining = reader.ReadInt32();
                patch.RngState = reader.ReadUInt64();
                patch.LastUpdateTick = reader.ReadInt64();
                RequireEcology(patch.WaterCapacity >= 0 && patch.WaterCapacity <= 1000000 && patch.Water >= 0 &&
                    patch.Water <= patch.WaterCapacity && patch.SnowWater >= 0 && patch.SnowWater <= patch.WaterCapacity * 2 &&
                    patch.SnowDepth >= 0 && patch.SnowDepth <= EnvironmentState.MaxSnowDepth &&
                    patch.SoilFertility >= 0 && patch.SoilFertility <= 10000 && patch.Litter >= 0 &&
                    patch.MineralRemaining >= 0 && patch.RngState != 0);
                MutableHabitats.Add(patch);
            }
            MutableGroups.Clear();
            count = ReadEcologyCount(reader, 2048);
            int previousGroup = 0;
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                AnimalSpecies species = ReadSpecies(reader);
                int region = reader.ReadInt32();
                int territory = reader.ReadInt32();
                var group = new AnimalGroupState(id, species, region, territory, new EntityId(reader.ReadInt32()), 1)
                {
                    Dominance = reader.ReadInt32(),
                    Disputes = reader.ReadInt32(),
                    RngState = reader.ReadUInt64(),
                    LastCompetitionTick = reader.ReadInt64()
                };
                RequireEcology(id > previousGroup && group.Dominance >= 0 && group.Disputes >= 0 && group.RngState != 0);
                previousGroup = id;
                MutableGroups.Add(group);
            }
            MutableAnimals.Clear();
            count = ReadEcologyCount(reader, 2048);
            int previousId = 0;
            for (int i = 0; i < count; i++)
            {
                AnimalState animal = ReadAnimal(reader);
                RequireEcology(animal.Id.Value > previousId);
                previousId = animal.Id.Value;
                MutableAnimals.Add(animal);
            }
            MutableCarcasses.Clear();
            count = ReadEcologyCount(reader, 16384);
            previousId = 0;
            for (int i = 0; i < count; i++)
            {
                EntityId id = new EntityId(reader.ReadInt32());
                GridPos position = ReadPosition(reader);
                AnimalSpecies species = ReadSpecies(reader);
                int region = reader.ReadInt32();
                int habitat = reader.ReadInt32();
                int meat = reader.ReadInt32();
                int hide = reader.ReadInt32();
                EntityId owner = new EntityId(reader.ReadInt32());
                bool natural = reader.ReadBoolean();
                EntityId source = new EntityId(reader.ReadInt32());
                AnimalDeathCause cause = (AnimalDeathCause)reader.ReadInt32();
                int freshness = reader.ReadInt32();
                int decay = reader.ReadInt32();
                long created = reader.ReadInt64();
                long updated = reader.ReadInt64();
                var animal = new AnimalState(id, species, position, 1, 0)
                {
                    Region = region,
                    HabitatId = habitat,
                    ReservedBy = owner,
                    LastDamageSourceId = source,
                    DeathCause = cause
                };
                var carcass = new CarcassState(animal, created, meat, hide)
                {
                    Freshness = freshness,
                    DecayProgress = decay,
                    LastUpdateTick = updated
                };
                RequireEcology(id.Value > previousId && natural == carcass.Natural && meat >= 0 && meat <= animal.Definition.FoodYield &&
                    hide >= 0 && hide <= animal.Definition.HideYield && freshness >= 0 && freshness <= 10000 &&
                    decay >= 0 && decay < 1000 && cause > AnimalDeathCause.None && cause <= AnimalDeathCause.Injury);
                previousId = id.Value;
                for (int item = 0; item < (int)ItemKind.Count; item++)
                {
                    int amount = reader.ReadInt32();
                    RequireEcology(amount >= 0 && (amount == 0 || carcass.Inventory.TryAdd((ItemKind)item, amount)));
                }
                MutableCarcasses.Add(carcass);
            }
            RequireEcology(BirthProgress >= 0 && Births >= 0 && NaturalDeaths >= 0 && HuntKills >= 0 &&
                PredationDeaths >= 0 && IntroducedAnimals >= 0 && Rng.State != 0 && Regions.RngState != 0 &&
                Regions.Departures >= 0 && Regions.Returns >= 0 && _nextGroupId > previousGroup);
        }

        private static void WriteAnimal(BinaryWriter writer, AnimalState animal)
        {
            writer.Write(animal.Id.Value);
            writer.Write((int)animal.Species);
            WritePosition(writer, animal.Position);
            writer.Write(animal.Health);
            writer.Write(animal.AgeTicks);
            writer.Write(animal.Fleeing);
            writer.Write(animal.ReservedBy.Value);
            writer.Write(animal.RngState);
            writer.Write(animal.Region);
            writer.Write(animal.HabitatId);
            writer.Write(animal.Energy);
            writer.Write(animal.BodyMass);
            writer.Write(animal.Hydration);
            writer.Write(animal.CoreTemperature);
            writer.Write((int)animal.Sex);
            writer.Write((int)animal.Behavior);
            writer.Write(animal.MaxAgeTicks);
            writer.Write(animal.GroupId);
            writer.Write(animal.TerritoryHabitatId);
            writer.Write(animal.MotherId.Value);
            writer.Write(animal.FatherId.Value);
            writer.Write(animal.MateId.Value);
            writer.Write(animal.PregnancyFatherId.Value);
            writer.Write(animal.PregnancyTicks);
            writer.Write(animal.PregnancyLitter);
            writer.Write(animal.ReproductionCooldown);
            writer.Write(animal.TargetAnimalId.Value);
            writer.Write(animal.LastTargetId.Value);
            writer.Write(animal.TargetInvalidatedTick);
            WritePosition(writer, animal.TargetPosition);
            writer.Write(animal.MigrationDestination);
            writer.Write(animal.LastMigrationTick);
            writer.Write(animal.MigrationPathIndex);
            writer.Write(animal.MigrationPath.Count);
            foreach (GridPos position in animal.MigrationPath)
            {
                WritePosition(writer, position);
            }
            writer.Write(animal.LastNeedsTick);
            writer.Write(animal.NextMoveTick);
            writer.Write(animal.NextDecisionTick);
            writer.Write(animal.StarvationTicks);
            writer.Write(animal.DehydrationTicks);
            writer.Write(animal.ExposureTicks);
            writer.Write(animal.DeadProcessed);
            writer.Write((int)animal.DeathCause);
            writer.Write(animal.LastDamageSourceId.Value);
        }

        private static AnimalState ReadAnimal(BinaryReader reader)
        {
            EntityId id = new EntityId(reader.ReadInt32());
            AnimalSpecies species = ReadSpecies(reader);
            var animal = new AnimalState(id, species, ReadPosition(reader), 1, 0)
            {
                Health = reader.ReadInt32(),
                AgeTicks = reader.ReadInt32(),
                Fleeing = reader.ReadBoolean(),
                ReservedBy = new EntityId(reader.ReadInt32()),
                RngState = reader.ReadUInt64(),
                Region = reader.ReadInt32(),
                HabitatId = reader.ReadInt32(),
                Energy = reader.ReadInt32(),
                BodyMass = reader.ReadInt32(),
                Hydration = reader.ReadInt32(),
                CoreTemperature = reader.ReadInt32(),
                Sex = (AnimalSex)reader.ReadInt32(),
                Behavior = (AnimalBehavior)reader.ReadInt32(),
                MaxAgeTicks = reader.ReadInt32(),
                GroupId = reader.ReadInt32(),
                TerritoryHabitatId = reader.ReadInt32(),
                MotherId = new EntityId(reader.ReadInt32()),
                FatherId = new EntityId(reader.ReadInt32()),
                MateId = new EntityId(reader.ReadInt32()),
                PregnancyFatherId = new EntityId(reader.ReadInt32()),
                PregnancyTicks = reader.ReadInt32(),
                PregnancyLitter = reader.ReadInt32(),
                ReproductionCooldown = reader.ReadInt32(),
                TargetAnimalId = new EntityId(reader.ReadInt32()),
                LastTargetId = new EntityId(reader.ReadInt32()),
                TargetInvalidatedTick = reader.ReadInt64(),
                TargetPosition = ReadPosition(reader),
                MigrationDestination = reader.ReadInt32(),
                LastMigrationTick = reader.ReadInt64(),
                MigrationPathIndex = reader.ReadInt32()
            };
            int count = ReadEcologyCount(reader, 1048576);
            for (int i = 0; i < count; i++)
            {
                animal.MigrationPath.Add(ReadPosition(reader));
            }
            animal.LastNeedsTick = reader.ReadInt64();
            animal.NextMoveTick = reader.ReadInt64();
            animal.NextDecisionTick = reader.ReadInt64();
            animal.StarvationTicks = reader.ReadInt32();
            animal.DehydrationTicks = reader.ReadInt32();
            animal.ExposureTicks = reader.ReadInt32();
            animal.DeadProcessed = reader.ReadBoolean();
            animal.DeathCause = (AnimalDeathCause)reader.ReadInt32();
            animal.LastDamageSourceId = new EntityId(reader.ReadInt32());
            RequireEcology(animal.Health >= 0 && animal.Health <= animal.Definition.Health && animal.AgeTicks >= 0 &&
                animal.Energy >= 0 && animal.Energy <= animal.Definition.EnergyCapacity && animal.BodyMass >= 0 &&
                animal.BodyMass <= animal.Definition.AdultMass && animal.Hydration >= 0 && animal.Hydration <= 3000 &&
                animal.CoreTemperature >= 0 && animal.CoreTemperature <= 4500 && animal.RngState != 0 &&
                animal.Sex >= AnimalSex.Female && animal.Sex <= AnimalSex.Male &&
                animal.Behavior >= AnimalBehavior.Rest && animal.Behavior <= AnimalBehavior.Dead &&
                animal.MaxAgeTicks >= animal.Definition.Lifespan * 80 / 100 && animal.MaxAgeTicks <= animal.Definition.Lifespan * 120 / 100 &&
                animal.PregnancyTicks >= 0 && animal.PregnancyTicks <= animal.Definition.GestationTicks + 60 &&
                animal.PregnancyLitter >= 0 && animal.PregnancyLitter <= animal.Definition.LitterSize &&
                animal.ReproductionCooldown >= 0 && animal.MigrationPathIndex >= 0 && animal.MigrationPathIndex <= count &&
                animal.StarvationTicks >= 0 && animal.DehydrationTicks >= 0 && animal.ExposureTicks >= 0 &&
                animal.DeathCause >= AnimalDeathCause.None && animal.DeathCause <= AnimalDeathCause.Injury);
            return animal;
        }

        private void WriteBalance(BinaryWriter writer)
        {
            writer.Write(Balance.InitialBiomass);
            writer.Write(Balance.IntroducedBiomass);
            writer.Write(Balance.Photosynthesis);
            writer.Write(Balance.MaterialFoodConsumed);
            writer.Write(Balance.Respiration);
            writer.Write(Balance.Decomposed);
            writer.Write(Balance.MaterialBiomassProduced);
            writer.Write(Balance.PlantEaten);
            writer.Write(Balance.CarrionEaten);
            writer.Write(Balance.NursingTransferred);
            writer.Write(Balance.BirthTransferred);
            writer.Write(Balance.GrowthTransferred);
            writer.Write(Balance.InitialWater);
            writer.Write(Balance.IntroducedWater);
            writer.Write(Balance.Precipitation);
            writer.Write(Balance.WaterUsed);
            writer.Write(Balance.Evaporation);
            writer.Write(Balance.Runoff);
            writer.Write(Balance.MaterialWaterProduced);
            writer.Write(Balance.InitialMineral);
            writer.Write(Balance.MineralProduced);
            writer.Write(Balance.BirthsBlockedByCapacity);
            writer.Write(Balance.PregnanciesLost);
            foreach (long quantity in Balance.DeathCounts)
            {
                writer.Write(quantity);
            }
            foreach (long quantity in Balance.NodeConversions)
            {
                writer.Write(quantity);
            }
            foreach (long quantity in Balance.CarcassConversions)
            {
                writer.Write(quantity);
            }
        }

        private void ReadBalance(BinaryReader reader)
        {
            Balance.InitialBiomass = ReadNonnegative(reader);
            Balance.IntroducedBiomass = ReadNonnegative(reader);
            Balance.Photosynthesis = ReadNonnegative(reader);
            Balance.MaterialFoodConsumed = ReadNonnegative(reader);
            Balance.Respiration = ReadNonnegative(reader);
            Balance.Decomposed = ReadNonnegative(reader);
            Balance.MaterialBiomassProduced = ReadNonnegative(reader);
            Balance.PlantEaten = ReadNonnegative(reader);
            Balance.CarrionEaten = ReadNonnegative(reader);
            Balance.NursingTransferred = ReadNonnegative(reader);
            Balance.BirthTransferred = ReadNonnegative(reader);
            Balance.GrowthTransferred = ReadNonnegative(reader);
            Balance.InitialWater = ReadNonnegative(reader);
            Balance.IntroducedWater = ReadNonnegative(reader);
            Balance.Precipitation = ReadNonnegative(reader);
            Balance.WaterUsed = ReadNonnegative(reader);
            Balance.Evaporation = ReadNonnegative(reader);
            Balance.Runoff = ReadNonnegative(reader);
            Balance.MaterialWaterProduced = ReadNonnegative(reader);
            Balance.InitialMineral = ReadNonnegative(reader);
            Balance.MineralProduced = ReadNonnegative(reader);
            Balance.BirthsBlockedByCapacity = ReadNonnegative(reader);
            Balance.PregnanciesLost = ReadNonnegative(reader);
            for (int i = 0; i < Balance.DeathCounts.Length; i++)
            {
                Balance.DeathCounts[i] = ReadNonnegative(reader);
            }
            for (int i = 0; i < Balance.NodeConversions.Length; i++)
            {
                Balance.NodeConversions[i] = ReadNonnegative(reader);
            }
            for (int i = 0; i < Balance.CarcassConversions.Length; i++)
            {
                Balance.CarcassConversions[i] = ReadNonnegative(reader);
            }
        }

        private static void WriteVegetation(BinaryWriter writer, VegetationState vegetation)
        {
            writer.Write((int)vegetation.Kind);
            writer.Write(vegetation.Capacity);
            writer.Write(vegetation.Biomass);
            writer.Write(vegetation.SeedBank);
            writer.Write(vegetation.GrowthRemainder);
        }

        private static void ReadVegetation(BinaryReader reader, VegetationState vegetation)
        {
            RequireEcology(reader.ReadInt32() == (int)vegetation.Kind);
            vegetation.Capacity = reader.ReadInt32();
            vegetation.Biomass = reader.ReadInt32();
            vegetation.SeedBank = reader.ReadInt32();
            vegetation.GrowthRemainder = reader.ReadInt64();
            RequireEcology(vegetation.Capacity >= 0 && vegetation.Capacity <= 10000000 && vegetation.Biomass >= 0 &&
                vegetation.Biomass <= vegetation.Capacity && vegetation.SeedBank >= 0 && vegetation.SeedBank <= 10000 &&
                vegetation.GrowthRemainder >= 0 && vegetation.GrowthRemainder < 60L * 1440 * 10000);
        }

        public void Validate(WorldState world)
        {
            RequireEcology(_initialized && _patchColumns == (world.Map.Width + PatchSize - 1) / PatchSize &&
                _patchRows == (world.Map.Height + PatchSize - 1) / PatchSize &&
                Habitats.Count == _patchColumns * _patchRows + Regions.Regions.Count &&
                _lastUpdateTick >= -1 && _lastUpdateTick <= world.Tick && _lastResourceTick >= -1 && _lastResourceTick <= world.Tick &&
                Regions.LastUpdateTick >= 0 && Regions.LastUpdateTick <= world.Tick &&
                IntroducedAnimals + Births - TotalDeaths == Animals.Count);
            long departures = 0;
            long returns = 0;
            foreach (EcologyRegionState region in Regions.Regions)
            {
                RequireEcology(region.Id >= 0 && region.Id < Regions.Regions.Count &&
                    region.HabitatId == _patchColumns * _patchRows + region.Id && world.Map.IsWalkable(region.Exit) &&
                    (region.Exit.X == 0 || region.Exit.Y == 0 || region.Exit.X == world.Map.Width - 1 || region.Exit.Y == world.Map.Height - 1) &&
                    world.Map.IsReachable(world.Center, region.Exit) && region.LastUpdateTick <= world.Tick);
                int population = 0;
                foreach (AnimalState animal in Animals)
                {
                    if (animal.Region == region.Id)
                    {
                        population++;
                    }
                }
                RequireEcology(region.Immigration + region.Births - region.Emigration - region.Deaths == population);
                departures += region.Immigration;
                returns += region.Emigration;
            }
            RequireEcology(departures == Regions.Departures && returns == Regions.Returns);
            var ids = new HashSet<int>();
            foreach (HabitatPatchState patch in Habitats)
            {
                RequireEcology(patch.Region >= -1 && patch.Region < Regions.Regions.Count && patch.LastUpdateTick >= 0 && patch.LastUpdateTick <= world.Tick);
                if (patch.Region < 0)
                {
                    RequireEcology(patch.Origin == new GridPos(patch.Id % _patchColumns * PatchSize, patch.Id / _patchColumns * PatchSize));
                }
                else
                {
                    RequireEcology(patch.Origin == GridPos.Invalid && Regions.MutableRegions[patch.Region].HabitatId == patch.Id);
                }
            }
            foreach (AnimalState animal in Animals)
            {
                RequireEcology(ids.Add(animal.Id.Value) && IsHistoricalId(world, animal.Id) && !animal.Id.IsNone &&
                    !world.Entities.TryGetNpc(animal.Id, out _) && !animal.DeadProcessed && animal.Health > 0 &&
                    animal.AgeTicks < animal.MaxAgeTicks && animal.LastNeedsTick >= 0 && animal.LastNeedsTick <= world.Tick &&
                    animal.NextMoveTick >= 0 && animal.NextDecisionTick >= 0 && animal.LastMigrationTick >= -1 && animal.LastMigrationTick <= world.Tick &&
                    animal.TargetInvalidatedTick >= -1 && animal.TargetInvalidatedTick <= world.Tick && animal.Region >= -1 && animal.Region < Regions.Regions.Count);
                ValidateLocation(world, animal.Region, animal.HabitatId, animal.Position);
                AnimalGroupState group = FindGroup(animal.GroupId);
                RequireEcology(group != null && group.Species == animal.Species && FindHabitat(animal.TerritoryHabitatId) != null &&
                    IsHistoricalId(world, animal.MotherId) && IsHistoricalId(world, animal.FatherId) &&
                    (animal.MotherId.IsNone || animal.MotherId.Value < animal.Id.Value) &&
                    (animal.FatherId.IsNone || animal.FatherId.Value < animal.Id.Value) &&
                    IsHistoricalId(world, animal.LastDamageSourceId) && IsHistoricalId(world, animal.LastTargetId) &&
                    IsHistoricalId(world, animal.PregnancyFatherId));
                RequireEcology(animal.MateId.IsNone || Find(animal.MateId.Value) != null &&
                    Find(animal.MateId.Value).Species == animal.Species && Find(animal.MateId.Value).Sex != animal.Sex);
                AnimalState target = Find(animal.TargetAnimalId.Value);
                RequireEcology(animal.TargetAnimalId.IsNone || target != null && target.Health > 0 && target.Region == animal.Region);
                RequireEcology(animal.PregnancyLitter == 0 ? animal.PregnancyTicks == 0 && animal.PregnancyFatherId.IsNone :
                    animal.Sex == AnimalSex.Female && !animal.PregnancyFatherId.IsNone);
                ValidateOwner(world, animal.ReservedBy);
                RequireEcology(animal.Region < 0 || animal.ReservedBy.IsNone);
                RequireEcology(animal.MigrationDestination >= -1 && animal.MigrationDestination < Regions.Regions.Count &&
                    (animal.MigrationDestination < 0 || animal.Region == -1 && Regions.Enabled));
                for (int i = 0; i < animal.MigrationPath.Count; i++)
                {
                    RequireEcology(world.Map.IsInside(animal.MigrationPath[i]) &&
                        (i == 0 || animal.MigrationPath[i].ManhattanDistance(animal.MigrationPath[i - 1]) == 1));
                }
            }
            foreach (CarcassState carcass in Carcasses)
            {
                RequireEcology(ids.Add(carcass.Id.Value) && IsHistoricalId(world, carcass.Id) && !carcass.Id.IsNone &&
                    !world.Entities.TryGetNpc(carcass.Id, out _) && IsHistoricalId(world, carcass.SourceId) &&
                    carcass.CreatedTick >= 0 && carcass.CreatedTick <= carcass.LastUpdateTick && carcass.LastUpdateTick <= world.Tick);
                ValidateLocation(world, carcass.Region, carcass.HabitatId, carcass.Position);
                ValidateOwner(world, carcass.ReservedBy);
                RequireEcology(carcass.Region < 0 || carcass.ReservedBy.IsNone);
            }
            foreach (AnimalGroupState group in Groups)
            {
                AnimalState leader = Find(group.LeaderId.Value);
                RequireEcology(group.Id < _nextGroupId && leader != null && leader.GroupId == group.Id &&
                    group.Region == leader.Region && FindHabitat(group.TerritoryHabitatId) != null && group.LastCompetitionTick <= world.Tick);
            }
            RequireEcology(Balance.BiomassError(this) == 0 && Balance.WaterError(this) == 0 && Balance.MineralError(this) == 0);
            long deaths = 0;
            foreach (long count in Balance.DeathCounts)
            {
                deaths += count;
            }
            RequireEcology(deaths == TotalDeaths && Balance.Deaths(AnimalDeathCause.None) == 0 &&
                Balance.Deaths(AnimalDeathCause.Hunt) == HuntKills && Balance.Deaths(AnimalDeathCause.Predation) == PredationDeaths);
            long biomassProduced = 0;
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                biomassProduced += (Balance.NodeProduced((ItemKind)i) + Balance.CarcassProduced((ItemKind)i)) * EcologyBalance.BiomassPerItem((ItemKind)i);
            }
            RequireEcology(biomassProduced == Balance.MaterialBiomassProduced &&
                Balance.NodeProduced(ItemKind.RawWater) * 100 == Balance.MaterialWaterProduced &&
                Balance.NodeProduced(ItemKind.Stone) * 100 == Balance.MineralProduced);
        }

        private void ValidateLocation(WorldState world, int region, int habitat, GridPos position)
        {
            HabitatPatchState patch = FindHabitat(habitat);
            RequireEcology(patch != null && patch.Region == region && region >= -1 && region < Regions.Regions.Count);
            RequireEcology(region < 0 ? world.Map.IsInside(position) && HabitatAt(position) == patch : position == GridPos.Invalid);
        }

        private static void ValidateOwner(WorldState world, EntityId owner)
        {
            NpcState npc;
            RequireEcology(owner.IsNone || world.Entities.TryGetNpc(owner, out npc) && npc.Survival.Life != LifeState.Dead);
        }

        private static bool IsHistoricalId(WorldState world, EntityId id) => id.Value >= 0 && id.Value < world.NextEntityId;
        private static void WritePosition(BinaryWriter writer, GridPos position)
        {
            writer.Write(position.X);
            writer.Write(position.Y);
        }
        private static GridPos ReadPosition(BinaryReader reader) => new GridPos(reader.ReadInt32(), reader.ReadInt32());
        private static long ReadNonnegative(BinaryReader reader)
        {
            long value = reader.ReadInt64();
            RequireEcology(value >= 0 && value <= long.MaxValue / 1024);
            return value;
        }
        private static AnimalSpecies ReadSpecies(BinaryReader reader)
        {
            int species = reader.ReadInt32();
            RequireEcology(species >= 0 && species <= (int)AnimalSpecies.Raven);
            return (AnimalSpecies)species;
        }
        private static int ReadEcologyCount(BinaryReader reader, int maximum)
        {
            int count = reader.ReadInt32();
            RequireEcology(count >= 0 && count <= maximum);
            return count;
        }
        private static void RequireEcology(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Snapshot violates ecology invariants.");
            }
        }
    }
}
