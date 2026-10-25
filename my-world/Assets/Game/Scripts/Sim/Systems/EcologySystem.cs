using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class EcologySystem
    {
        private static readonly int[] Dx = { 0, 1, 0, -1 };
        private static readonly int[] Dy = { 1, 0, -1, 0 };

        public static void Update(WorldState world, List<SimEvent> events)
        {
            EcologyState ecology = world.Ecology;
            int count = ecology.Animals.Count; // Newborns first act next update.
            for (int i = 0; i < count; i++)
            {
                AnimalState animal = ecology.Animals[i];
                if (animal.Health <= 0)
                {
                    continue;
                }
                long elapsed = world.Tick - animal.LastNeedsTick;
                if (elapsed >= 60)
                {
                    int dt = checked((int)elapsed);
                    animal.LastNeedsTick = world.Tick;
                    UpdateNeeds(world, animal, dt);
                    if (animal.Health <= 0)
                    {
                        continue;
                    }
                    Feed(world, animal, dt);
                    Drink(world, animal, dt);
                    Nurse(world, animal, dt);
                    Reproduce(world, animal, dt, events);
                }
                if (animal.Region >= 0)
                {
                    if (elapsed >= 60)
                    {
                        Decide(world, animal, events);
                    }
                    continue;
                }
                if (AnimalEncounterSystem.Update(world, animal, events)) { continue; }
                if (world.Tick >= animal.NextDecisionTick)
                {
                    Decide(world, animal, events);
                    animal.NextDecisionTick = world.Tick + 30;
                }
                if (world.Tick >= animal.NextMoveTick)
                {
                    Move(world, animal);
                    animal.NextMoveTick = world.Tick + WinterEcologyRules.MoveInterval(world, animal, Math.Max(3, animal.Definition.MoveTicks /
                        (animal.Fleeing || animal.Behavior == AnimalBehavior.Hunt ? 2 : 1)));
                }
            }
            UpdateCarcasses(world);
            if (world.Tick % 360 == 0)
            {
                Compete(world, events);
            }
        }

        private static void UpdateNeeds(WorldState world, AnimalState animal, int dt)
        {
            EcologyBalance balance = world.Ecology.Balance;
            AnimalDefinition definition = animal.Definition;
            HabitatPatchState patch = world.Ecology.FindHabitat(animal.HabitatId);
            bool wasJuvenile = animal.AgeTicks < definition.MaturityTicks;
            animal.AgeTicks = checked(animal.AgeTicks + dt);
            if (wasJuvenile && animal.AgeTicks >= definition.MaturityTicks && !animal.MotherId.IsNone)
            {
                animal.GroupId = 0;
                world.Ecology.JoinGroup(animal);
            }
            animal.ReproductionCooldown = Math.Max(0, animal.ReproductionCooldown - dt);
            int temperature = world.Environment.Temperature + (animal.Region < 0 ? 0 :
                world.Ecology.Regions.MutableRegions[animal.Region].TemperatureOffset);
            int protection = patch.Shelter / 5;
            if (animal.Species == AnimalSpecies.Vole)
            {
                protection += 2500 + Math.Min(1500, patch.SnowDepth / 4);
            }
            else if (animal.Species == AnimalSpecies.Rabbit)
            {
                protection += 1000;
            }
            if (animal.Behavior == AnimalBehavior.Shelter || animal.Behavior == AnimalBehavior.Rest)
            {
                protection += patch.Shelter / 5;
            }
            int cold = Math.Max(0, definition.ColdTolerance - temperature - protection + world.Environment.Wind * 5);
            int targetTemperature = Math.Max(2000, 3800 - cold / 2);
            animal.CoreTemperature += (targetTemperature - animal.CoreTemperature) / 4;
            int metabolism = Math.Max(1, (definition.MetabolismPerHour + cold / 40 + WinterEcologyRules.SnowCost(world, animal) +
                (animal.PregnancyLitter > 0 ? definition.MetabolismPerHour / 4 : 0)) * dt / 60);
            if (animal.LifeStage == AnimalLifeStage.Infant)
            {
                metabolism = Math.Max(1, metabolism / 3);
            }
            int spent = Math.Min(animal.Energy, metabolism);
            animal.Energy -= spent;
            balance.Respiration += spent;
            int waterSpent = Math.Min(animal.Hydration, Math.Max(1, definition.WaterPerHour * dt / 60));
            animal.Hydration -= waterSpent;
            balance.WaterUsed += waterSpent;
            animal.StarvationTicks = animal.Energy == 0 ? checked(animal.StarvationTicks + dt) : Math.Max(0, animal.StarvationTicks - dt);
            animal.DehydrationTicks = animal.Hydration == 0 ? checked(animal.DehydrationTicks + dt) : 0;
            animal.ExposureTicks = animal.CoreTemperature < 3200 ? checked(animal.ExposureTicks + dt) : 0;
            if (animal.AgeTicks >= animal.MaxAgeTicks)
            {
                Die(animal, AnimalDeathCause.Age);
                return;
            }
            int damage = 0;
            AnimalDeathCause cause = AnimalDeathCause.None;
            if (animal.StarvationTicks > 12 * 60)
            {
                damage += Math.Max(1, dt / 60);
                cause = AnimalDeathCause.Starvation;
            }
            if (animal.DehydrationTicks > 6 * 60)
            {
                damage += Math.Max(1, dt / 30);
                cause = AnimalDeathCause.Dehydration;
            }
            if (animal.ExposureTicks > 3 * 60)
            {
                damage += Math.Max(1, (3200 - animal.CoreTemperature) * dt / 6000);
                cause = AnimalDeathCause.Exposure;
            }
            animal.Health = Math.Max(0, animal.Health - damage);
            if (animal.Health == 0)
            {
                Die(animal, cause);
                return;
            }
            if (damage == 0 && animal.Energy > definition.EnergyCapacity / 2 && animal.Hydration > 1000)
            {
                int healing = Math.Min(definition.Health - animal.Health, Math.Max(1, dt / 180));
                int healingCost = Math.Min(animal.Energy, healing * 5);
                animal.Energy -= healingCost;
                balance.Respiration += healingCost;
                animal.Health += healing;
            }
            int desiredMass = definition.NewbornMass + (int)((long)(definition.AdultMass - definition.NewbornMass) *
                Math.Min(animal.AgeTicks, definition.MaturityTicks) / definition.MaturityTicks);
            int growth = Math.Min(Math.Max(0, desiredMass - animal.BodyMass), Math.Max(0, animal.Energy - definition.EnergyCapacity / 3));
            animal.BodyMass += growth;
            animal.Energy -= growth;
            balance.GrowthTransferred += growth;
        }

        private static void Die(AnimalState animal, AnimalDeathCause cause)
        {
            animal.Health = 0;
            animal.DeathCause = cause;
            animal.LastDamageSourceId = EntityId.None;
        }

        private static void Feed(WorldState world, AnimalState animal, int dt)
        {
            if (animal.Energy >= animal.Definition.EnergyCapacity * 4 / 5 || animal.LifeStage == AnimalLifeStage.Infant)
            {
                return;
            }
            HabitatPatchState patch = world.Ecology.FindHabitat(animal.HabitatId);
            int appetite = Math.Min(animal.Definition.EnergyCapacity - animal.Energy,
                Math.Max(1, animal.Definition.MetabolismPerHour * 4 * dt / 60));
            int eaten = 0;
            if (animal.Definition.EatsCarrion)
            {
                foreach (CarcassState carcass in world.Ecology.Carcasses)
                {
                    if (carcass.Region != animal.Region || carcass.HabitatId != animal.HabitatId ||
                        animal.Region < 0 && animal.Position.ManhattanDistance(carcass.Position) > 1 || carcass.MeatRemaining == 0)
                    {
                        continue;
                    }
                    int units = Math.Min(carcass.MeatRemaining, Math.Min(appetite / 100,
                        (animal.Definition.EnergyCapacity - animal.Energy) / 100));
                    if (units > 0)
                    {
                        carcass.MeatRemaining -= units;
                        int biomass = units * 100;
                        animal.Energy += biomass;
                        eaten += biomass;
                        appetite -= biomass;
                        world.Ecology.Balance.CarrionEaten += biomass;
                        animal.Behavior = AnimalBehavior.Scavenge;
                    }
                    break;
                }
            }
            if (!animal.Definition.IsPredator && appetite > 0)
            {
                int plantEaten = patch.ConsumePlantFood(animal.Species, world.Date.Season, appetite);
                eaten += plantEaten;
                animal.Energy += plantEaten;
                world.Ecology.Balance.PlantEaten += plantEaten;
                animal.Behavior = AnimalBehavior.Forage;
                if (eaten == 0 && animal.Region < 0)
                {
                    EatMaterializedFood(world, animal);
                    DamageCrops(world, animal, dt);
                }
            }
        }

        private static void EatMaterializedFood(WorldState world, AnimalState animal)
        {
            foreach (ResourceNodeState node in world.Resources.Nodes)
            {
                if (node.Item != ItemKind.Food || node.Amount <= 0 || !node.ReservedBy.IsNone ||
                    node.Position.ManhattanDistance(animal.Position) > 1 || animal.Energy + 100 > animal.Definition.EnergyCapacity)
                {
                    continue;
                }
                node.Amount--;
                world.Materials.Consume(ItemKind.Food, 1);
                world.Ecology.Balance.MaterialFoodConsumed += 100;
                animal.Energy += 100;
                return;
            }
        }

        private static void DamageCrops(WorldState world, AnimalState animal, int dt)
        {
            if (animal.Species != AnimalSpecies.Boar && animal.Species != AnimalSpecies.Deer && animal.Species != AnimalSpecies.Rabbit)
            {
                return;
            }
            foreach (FarmPlotState plot in world.Resources.FarmPlots)
            {
                if (plot.Crop != CropType.None && !GrowingBedSystem.Protected(GrowingBedSystem.Bed(world, plot)) &&
                    plot.Position.ManhattanDistance(animal.Position) <= 1)
                {
                    // Unharvested crops are not materialized food. Damage reduces
                    // future yield, but does not invent a second biomass pool.
                    AgricultureSystem.ApplyDamage(world, plot, dt);
                    return;
                }
            }
        }

        private static void Drink(WorldState world, AnimalState animal, int dt)
        {
            if (animal.Hydration >= 2000)
            {
                return;
            }
            HabitatPatchState patch = world.Ecology.FindHabitat(animal.HabitatId);
            int temperature = world.Environment.Temperature + (animal.Region < 0 ? 0 :
                world.Ecology.Regions.MutableRegions[animal.Region].TemperatureOffset);
            int wanted = Math.Min(3000 - animal.Hydration, animal.Definition.WaterPerHour * 8 * dt / 60);
            int taken = 0;
            if (temperature >= 0 || patch.Shelter > 4000)
            {
                // Sheltered springs provide only a finite fraction of frozen water.
                taken = Math.Min(patch.Water, temperature >= 0 ? wanted : wanted / 4);
                patch.Water -= taken;
            }
            if (taken < wanted && patch.SnowWater > 0)
            {
                int snow = Math.Min(patch.SnowWater, (wanted - taken) / 2);
                patch.SnowWater -= snow;
                taken += snow;
                int meltCost = Math.Min(animal.Energy, snow / 4);
                animal.Energy -= meltCost;
                world.Ecology.Balance.Respiration += meltCost;
            }
            animal.Hydration += taken;
            if (taken > 0)
            {
                animal.Behavior = AnimalBehavior.Drink;
            }
        }

        private static void Nurse(WorldState world, AnimalState mother, int dt)
        {
            if (mother.Sex != AnimalSex.Female || mother.Energy < mother.Definition.EnergyCapacity / 3)
            {
                return;
            }
            foreach (AnimalState child in world.Ecology.Animals)
            {
                if (child.MotherId != mother.Id || child.LifeStage != AnimalLifeStage.Infant || child.Health <= 0 ||
                    child.Region != mother.Region || child.HabitatId != mother.HabitatId ||
                    mother.Region < 0 && child.Position.ManhattanDistance(mother.Position) > 2)
                {
                    continue;
                }
                int milk = Math.Min(child.Definition.MetabolismPerHour * dt / 60,
                    Math.Min(child.Definition.EnergyCapacity - child.Energy, Math.Max(0, mother.Energy - mother.Definition.EnergyCapacity / 4)));
                child.Energy += milk;
                mother.Energy -= milk;
                int water = Math.Min(Math.Min(mother.Hydration, child.Definition.WaterPerHour * dt / 60), 3000 - child.Hydration);
                mother.Hydration -= water;
                child.Hydration += water;
                world.Ecology.Balance.NursingTransferred += milk;
                mother.Behavior = AnimalBehavior.Nurse;
            }
        }

        private static void Reproduce(WorldState world, AnimalState mother, int dt, List<SimEvent> events)
        {
            if (mother.Sex != AnimalSex.Female)
            {
                return;
            }
            EcologyState ecology = world.Ecology;
            AnimalDefinition definition = mother.Definition;
            if (mother.PregnancyLitter > 0)
            {
                mother.PregnancyTicks += dt;
                if (mother.Energy < definition.EnergyCapacity / 8 || mother.Health < definition.Health / 3)
                {
                    mother.PregnancyTicks = 0;
                    mother.PregnancyLitter = 0;
                    mother.PregnancyFatherId = EntityId.None;
                    mother.ReproductionCooldown = definition.NursingTicks;
                    ecology.Balance.PregnanciesLost++;
                    return;
                }
                if (mother.PregnancyTicks < definition.GestationTicks)
                {
                    return;
                }
                int litter = mother.PregnancyLitter;
                for (int i = 0; i < litter; i++)
                {
                    int population = 0;
                    foreach (AnimalState resident in ecology.Animals)
                    {
                        if (resident.Species == mother.Species && resident.Region == mother.Region && resident.Health > 0)
                        {
                            population++;
                        }
                    }
                    int resourceCap = BirthSafetyCapacity(world, mother);
                    if (population >= resourceCap || ecology.Animals.Count >= 2048)
                    {
                        ecology.Balance.BirthsBlockedByCapacity++;
                        continue;
                    }
                    int cost = definition.NewbornMass * 3;
                    if (mother.Energy < cost + definition.EnergyCapacity / 4)
                    {
                        break;
                    }
                    AnimalState child = ecology.CreateAnimal(world, mother.Species, mother.Position, 0);
                    child.Region = mother.Region;
                    child.HabitatId = mother.HabitatId;
                    child.TerritoryHabitatId = mother.TerritoryHabitatId;
                    child.GroupId = mother.GroupId;
                    child.MotherId = mother.Id;
                    child.FatherId = mother.PregnancyFatherId;
                    child.Energy = definition.NewbornMass * 2;
                    child.BodyMass = definition.NewbornMass;
                    child.Hydration = 0;
                    mother.Energy -= cost;
                    ecology.Balance.BirthTransferred += cost;
                    ecology.RegisterBirth(child);
                    events.Add(new SimEvent(SimEventKind.AnimalBorn, world.Tick, child.Id,
                        mother.Species + " born to #" + mother.Id.Value + ", region " + mother.Region + "."));
                }
                mother.PregnancyTicks = 0;
                mother.PregnancyLitter = 0;
                mother.PregnancyFatherId = EntityId.None;
                mother.ReproductionCooldown = definition.NursingTicks + 3 * GameCalendar.TicksPerDay;
                return;
            }
            if (mother.LifeStage != AnimalLifeStage.Adult || mother.ReproductionCooldown > 0 ||
                !WinterEcologyRules.CanBreed(world, mother) || mother.Energy < definition.EnergyCapacity * 3 / 4 ||
                mother.Health < definition.Health * 4 / 5 || mother.Hydration < 1000 ||
                mother.MigrationDestination >= 0 || ecology.FindHabitat(mother.HabitatId).Shelter < 1500)
            {
                return;
            }
            AnimalState mate = null;
            foreach (AnimalState male in ecology.Animals)
            {
                if (male.Species == mother.Species && male.Region == mother.Region && male.HabitatId == mother.HabitatId &&
                    male.Sex == AnimalSex.Male && male.LifeStage == AnimalLifeStage.Adult && male.Health >= definition.Health * 4 / 5 &&
                    male.Energy > definition.EnergyCapacity / 2 && male.ReproductionCooldown == 0 &&
                    (mother.Region >= 0 || mother.Position.ManhattanDistance(male.Position) <= 3) &&
                    male.Id != mother.FatherId && male.MotherId != mother.Id &&
                    (mother.MotherId.IsNone || mother.MotherId != male.MotherId))
                {
                    mate = male;
                    break;
                }
            }
            if (mate == null)
            {
                return;
            }
            var rng = new DeterministicRng(mother.RngState);
            int litterSize = rng.NextInt(1, definition.LitterSize + 1);
            mother.RngState = rng.State;
            int courtshipCost = definition.NewbornMass / 2;
            mother.Energy -= courtshipCost;
            mate.Energy -= courtshipCost;
            ecology.Balance.Respiration += courtshipCost * 2;
            mother.MateId = mate.Id;
            mate.MateId = mother.Id;
            mother.PregnancyFatherId = mate.Id;
            mother.PregnancyLitter = litterSize;
            mother.PregnancyTicks = 0;
            mate.ReproductionCooldown = GameCalendar.TicksPerDay;
        }

        private static int BirthSafetyCapacity(WorldState world, AnimalState parent)
        {
            EcologyState ecology = world.Ecology;
            long food = 0;
            if (parent.Definition.IsPredator)
            {
                foreach (AnimalState prey in ecology.Animals)
                {
                    if (prey.Region == parent.Region && prey.Health > 0 && parent.Definition.CanPreyOn(prey.Species))
                    {
                        food += prey.BodyMass;
                    }
                }
                foreach (CarcassState carcass in ecology.Carcasses)
                {
                    if (carcass.Region == parent.Region)
                    {
                        food += carcass.MeatRemaining * 100;
                    }
                }
            }
            else
            {
                foreach (HabitatPatchState patch in ecology.Habitats)
                {
                    if (patch.Region == parent.Region)
                    {
                        food += patch.AvailablePlantFood(parent.Species, world.Date.Season);
                    }
                }
            }
            return (int)Math.Min(EcologyState.SpeciesCapacity, Math.Max(2,
                food / Math.Max(1, parent.Definition.MetabolismPerHour * 72) + parent.Definition.GroupSize));
        }

        private static int Sight(WorldState world, AnimalState animal)
        {
            int sight = animal.Definition.Sight;
            if (world.Environment.Daylight < 2000 && !animal.Definition.IsPredator)
            {
                sight = Math.Max(2, sight / 2);
            }
            if (world.Environment.Weather == WeatherKind.Blizzard)
            {
                sight = Math.Max(1, sight / 3);
            }
            return sight;
        }

        private static bool Perceives(WorldState world, AnimalState observer, AnimalState target)
        {
            if (target.Region != observer.Region || target.Health <= 0)
            {
                return false;
            }
            if (observer.Region >= 0)
            {
                return observer.HabitatId == target.HabitatId;
            }
            int concealment = world.Ecology.FindHabitat(target.HabitatId).Shelter / 3500;
            return observer.Position.ManhattanDistance(target.Position) <= Math.Max(1, Sight(world, observer) - concealment) &&
                HuntingSystem.Visible(world, observer.Position, target.Position);
        }

        private static void Decide(WorldState world, AnimalState animal, List<SimEvent> events)
        {
            HabitatPatchState patch = world.Ecology.FindHabitat(animal.HabitatId);
            animal.Fleeing = false;
            animal.TargetAnimalId = EntityId.None;
            AnimalState threat = null;
            int nearest = int.MaxValue;
            foreach (AnimalState other in world.Ecology.Animals)
            {
                bool dangerous = other.Definition.CanPreyOn(animal.Species) ||
                    animal.Species == AnimalSpecies.Fox && other.Species == AnimalSpecies.Wolf;
                if (other == animal || !dangerous || !Perceives(world, animal, other))
                {
                    continue;
                }
                int distance = animal.Region >= 0 ? 1 : animal.Position.ManhattanDistance(other.Position);
                if (distance < nearest)
                {
                    nearest = distance;
                    threat = other;
                }
            }
            if (threat != null)
            {
                animal.TargetAnimalId = threat.Id;
                animal.TargetPosition = threat.Position;
                bool defend = animal.Species == AnimalSpecies.Boar && nearest <= 1 ||
                    animal.Species == AnimalSpecies.Wolf && nearest <= 1;
                animal.Behavior = defend ? AnimalBehavior.Defend : AnimalBehavior.Flee;
                animal.Fleeing = !defend;
                if (defend)
                {
                    CombatSystem.RequestAnimalAttack(world, animal, threat, events);
                }
                return;
            }
            if (animal.MigrationDestination >= 0)
            {
                animal.Behavior = AnimalBehavior.Migrate;
                animal.TargetPosition = world.Ecology.Regions.MutableRegions[animal.MigrationDestination].Exit;
                return;
            }
            if (animal.LifeStage == AnimalLifeStage.Infant)
            {
                AnimalState mother = world.Ecology.Find(animal.MotherId.Value);
                animal.Behavior = AnimalBehavior.Nurse;
                animal.TargetPosition = mother != null && mother.Region == animal.Region ? mother.Position : animal.Position;
                return;
            }
            if (animal.CoreTemperature < 3400 || world.Environment.Weather == WeatherKind.Blizzard &&
                animal.Energy > animal.Definition.EnergyCapacity / 3)
            {
                animal.Behavior = AnimalBehavior.Shelter;
                ChooseHabitatTarget(world, animal, true);
                return;
            }
            if (animal.Hydration < 500)
            {
                animal.Behavior = AnimalBehavior.Drink;
                ChooseHabitatTarget(world, animal, false);
                return;
            }
            if (animal.Energy < animal.Definition.EnergyCapacity * 3 / 4)
            {
                if (animal.Definition.EatsCarrion)
                {
                    foreach (CarcassState carcass in world.Ecology.Carcasses)
                    {
                        if (carcass.Region == animal.Region && carcass.MeatRemaining > 0 &&
                            (animal.Region >= 0 || animal.Position.ManhattanDistance(carcass.Position) <= Sight(world, animal) &&
                            HuntingSystem.Visible(world, animal.Position, carcass.Position)))
                        {
                            animal.Behavior = AnimalBehavior.Scavenge;
                            animal.TargetPosition = carcass.Position;
                            return;
                        }
                    }
                }
                if (animal.Definition.IsPredator)
                {
                    AnimalState prey = null;
                    int bestScore = int.MinValue;
                    foreach (AnimalState candidate in world.Ecology.Animals)
                    {
                        if (!animal.Definition.CanPreyOn(candidate.Species) || !Perceives(world, animal, candidate))
                        {
                            continue;
                        }
                        int support = 1;
                        foreach (AnimalState member in world.Ecology.Animals)
                        {
                            if (member != animal && member.GroupId == animal.GroupId && member.Region == animal.Region && member.Health > 0 &&
                                (animal.Region >= 0 || member.Position.ManhattanDistance(animal.Position) <= 3))
                            {
                                support++;
                            }
                        }
                        // Large healthy prey requires nearby pack support.
                        if (candidate.Health > animal.Health * support * 2 && animal.Energy > animal.Definition.EnergyCapacity / 6)
                        {
                            continue;
                        }
                        int distance = animal.Region >= 0 ? 1 : animal.Position.ManhattanDistance(candidate.Position);
                        int score = candidate.Definition.FoodYield * 15 - candidate.Health - distance * 20;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            prey = candidate;
                        }
                    }
                    if (prey != null)
                    {
                        animal.TargetAnimalId = prey.Id;
                        animal.LastTargetId = prey.Id;
                        animal.TargetPosition = prey.Position;
                        animal.Behavior = AnimalBehavior.Hunt;
                        if (animal.Region >= 0 || animal.Position.ManhattanDistance(prey.Position) <= 1)
                        {
                            CombatSystem.RequestAnimalAttack(world, animal, prey, events);
                        }
                        return;
                    }
                }
                animal.Behavior = AnimalBehavior.Forage;
                ChooseHabitatTarget(world, animal, false);
                return;
            }
            animal.Behavior = AnimalBehavior.Rest;
            animal.TargetPosition = animal.Position;
            AnimalGroupState herd = world.Ecology.FindGroup(animal.GroupId);
            AnimalState herdLeader = herd == null ? null : world.Ecology.Find(herd.LeaderId.Value);
            if (herdLeader != null && herdLeader != animal && herdLeader.Region == animal.Region && animal.Region < 0 &&
                animal.Position.ManhattanDistance(herdLeader.Position) > 3)
            {
                animal.TargetPosition = herdLeader.Position;
                animal.Behavior = AnimalBehavior.Forage;
            }
            else if (patch.Shelter < 2000)
            {
                ChooseHabitatTarget(world, animal, true);
            }
        }

        private static void ChooseHabitatTarget(WorldState world, AnimalState animal, bool shelter)
        {
            if (animal.Region >= 0)
            {
                animal.TargetPosition = GridPos.Invalid;
                return;
            }
            GridPos best = animal.Position;
            long bestScore = long.MinValue;
            int radius = Math.Min(6, Sight(world, animal));
            var patchScores = new Dictionary<int, long>();
            for (int y = Math.Max(0, animal.Position.Y - radius); y <= Math.Min(world.Map.Height - 1, animal.Position.Y + radius); y++)
            {
                for (int x = Math.Max(0, animal.Position.X - radius); x <= Math.Min(world.Map.Width - 1, animal.Position.X + radius); x++)
                {
                    var cell = new GridPos(x, y);
                    if (!world.Map.IsWalkable(cell) || animal.Position.ManhattanDistance(cell) > radius)
                    {
                        continue;
                    }
                    HabitatPatchState patch = world.Ecology.HabitatAt(cell);
                    long score;
                    if (!patchScores.TryGetValue(patch.Id, out score))
                    {
                        score = shelter ? patch.Shelter : animal.Behavior == AnimalBehavior.Drink ?
                            patch.AvailableWater(world.Environment.Temperature) : patch.AvailablePlantFood(animal.Species, world.Date.Season);
                        int competition = 0;
                        foreach (AnimalState resident in world.Ecology.Animals)
                        {
                            if (resident.Health > 0 && resident.HabitatId == patch.Id)
                            {
                                competition += resident.Definition.MetabolismPerHour;
                            }
                        }
                        score -= competition * 2;
                        patchScores.Add(patch.Id, score);
                    }
                    score -= animal.Position.ManhattanDistance(cell) * 80;
                    AnimalGroupState group = world.Ecology.FindGroup(animal.GroupId);
                    if (group != null && patch.Id == group.TerritoryHabitatId)
                    {
                        score += 150;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = cell;
                    }
                }
            }
            animal.TargetPosition = best;
        }

        private static void Move(WorldState world, AnimalState animal)
        {
            if (animal.LifeStage == AnimalLifeStage.Infant && animal.Behavior != AnimalBehavior.Nurse ||
                animal.Behavior == AnimalBehavior.Rest || animal.Behavior == AnimalBehavior.Shelter &&
                animal.Position == animal.TargetPosition)
            {
                return;
            }
            if (animal.Species == AnimalSpecies.Raven && world.Environment.Weather == WeatherKind.Blizzard)
            {
                return;
            }
            if (animal.Behavior == AnimalBehavior.Migrate && animal.MigrationPathIndex < animal.MigrationPath.Count)
            {
                GridPos next = animal.MigrationPath[animal.MigrationPathIndex];
                if (world.Map.IsWalkable(next) && animal.Position.ManhattanDistance(next) == 1)
                {
                    animal.Position = next;
                    animal.MigrationPathIndex++;
                    animal.HabitatId = world.Ecology.HabitatAt(next).Id;
                }
                else
                {
                    animal.MigrationDestination = -1;
                    animal.MigrationPath.Clear();
                    animal.MigrationPathIndex = 0;
                }
                return;
            }
            var rng = new DeterministicRng(animal.RngState);
            int start = rng.NextInt(0, 4);
            GridPos best = animal.Position;
            int currentDistance = animal.TargetPosition.IsValid ? best.ManhattanDistance(animal.TargetPosition) : 0;
            int score = animal.Fleeing ? currentDistance : -currentDistance;
            for (int i = 0; i < 4; i++)
            {
                int d = (start + i) % 4;
                GridPos cell = animal.Position.Offset(Dx[d], Dy[d]);
                if (!world.Map.IsWalkable(cell))
                {
                    continue;
                }
                int distance = animal.TargetPosition.IsValid ? cell.ManhattanDistance(animal.TargetPosition) : 0;
                int candidate = animal.Fleeing ? distance : -distance;
                if (candidate > score)
                {
                    best = cell;
                    score = candidate;
                }
            }
            if (best == animal.Position && !animal.Fleeing && animal.TargetPosition.IsValid &&
                animal.Position.ManhattanDistance(animal.TargetPosition) > 1)
            {
                PathResult path = new GridPathfinder().FindPath(world.Map, animal.Position, animal.TargetPosition, 256);
                if (path.Found && path.Path.Count > 0)
                {
                    best = path.Path[0];
                }
            }
            if (best == animal.Position && animal.Behavior == AnimalBehavior.Forage && rng.Chance(30))
            {
                GridPos cell = animal.Position.Offset(Dx[start], Dy[start]);
                if (world.Map.IsWalkable(cell))
                {
                    best = cell;
                }
            }
            animal.Position = best;
            animal.HabitatId = world.Ecology.HabitatAt(best).Id;
            animal.RngState = rng.State;
        }

        private static void Compete(WorldState world, List<SimEvent> events)
        {
            world.Ecology.ReconcileGroups();
            foreach (AnimalGroupState group in world.Ecology.Groups)
            {
                AnimalState leader = world.Ecology.Find(group.LeaderId.Value);
                group.LastCompetitionTick = world.Tick;
                group.TerritoryHabitatId = leader.HabitatId;
                foreach (AnimalGroupState rival in world.Ecology.Groups)
                {
                    if (rival.Id <= group.Id || rival.Region != group.Region || rival.TerritoryHabitatId != group.TerritoryHabitatId ||
                        rival.Species != group.Species)
                    {
                        continue;
                    }
                    group.Disputes++;
                    rival.Disputes++;
                    AnimalState rivalLeader = world.Ecology.Find(rival.LeaderId.Value);
                    bool carnivore = leader.Definition.IsPredator;
                    if (carnivore && Perceives(world, leader, rivalLeader) &&
                        (leader.Region >= 0 || leader.Position.ManhattanDistance(rivalLeader.Position) <= 1))
                    {
                        CombatSystem.RequestAnimalAttack(world, leader, rivalLeader, events);
                    }
                    AnimalState loser = group.Dominance >= rival.Dominance ? rivalLeader : leader;
                    loser.Behavior = AnimalBehavior.Forage;
                    var rng = new DeterministicRng(group.RngState);
                    int direction = rng.NextInt(0, 4);
                    group.RngState = rng.State;
                    if (loser.Region < 0)
                    {
                        GridPos displaced = loser.Position.Offset(Dx[direction] * EcologyState.PatchSize, Dy[direction] * EcologyState.PatchSize);
                        if (world.Map.IsWalkable(displaced))
                        {
                            loser.TargetPosition = displaced;
                            loser.NextDecisionTick = world.Tick + 180;
                        }
                    }
                }
            }
        }

        public static void UpdateResources(WorldState world)
        {
            EcologyState ecology = world.Ecology;
            foreach (HabitatPatchState patch in ecology.Habitats)
            {
                if (world.Tick - patch.LastUpdateTick < 60)
                {
                    continue;
                }
                int dt = checked((int)(world.Tick - patch.LastUpdateTick));
                patch.LastUpdateTick = world.Tick;
                UpdatePatch(world, patch, dt);
                if (patch.Region >= 0)
                {
                    ecology.Regions.MutableRegions[patch.Region].LastUpdateTick = world.Tick;
                }
            }
            foreach (ResourceNodeState node in world.Resources.Nodes)
            {
                if (node.Amount >= node.Capacity)
                {
                    node.RegenerationProgress = 0;
                    continue;
                }
                HabitatPatchState patch = ecology.HabitatAt(node.Position);
                if (patch == null)
                {
                    continue;
                }
                if (node.RegenerationProgress + 1 < node.RegenerationTicks)
                {
                    node.RegenerationProgress++;
                    continue;
                }
                node.RegenerationProgress = node.RegenerationTicks - 1;
                bool converted = false;
                int mass = node.Item == ItemKind.Food || node.Item == ItemKind.Wood ||
                    node.Item == ItemKind.Fiber || node.Item == ItemKind.Herb ? EcologyBalance.BiomassPerItem(node.Item) : 0;
                if (node.Item == ItemKind.RawWater && !world.Environment.WaterFrozen)
                {
                    // Springs and wells stay tied to the regional groundwater pool:
                    // a dry local patch must not strand a fixed node while the rest
                    // of its region still holds finite water.
                    HabitatPatchState pool = patch.Water >= 100 ? patch : FindWaterPool(ecology, patch);
                    if (pool != null && pool.Water >= 100)
                    {
                        pool.Water -= 100;
                        ecology.Balance.MaterialWaterProduced += 100;
                        converted = true;
                    }
                }
                else if (node.Item == ItemKind.Stone && patch.MineralRemaining >= 100)
                {
                    patch.MineralRemaining -= 100;
                    ecology.Balance.MineralProduced += 100;
                    converted = true;
                }
                else if (node.Item == ItemKind.Food)
                {
                    HabitatPatchState fruit = patch.AvailableFruit >= mass ? patch : FindFruitPool(ecology, patch, mass);
                    if (patch.SnowDepth < 8000 && fruit != null && fruit.ConsumeFruit(mass) == mass)
                    { ecology.Balance.MaterialBiomassProduced += mass; converted = true; }
                }
                else if (mass > 0)
                {
                    VegetationKind kind = node.Item == ItemKind.Wood ? VegetationKind.Tree : VegetationKind.Herbaceous;
                    HabitatPatchState pool = patch;
                    VegetationState vegetation = pool.Vegetation(kind);
                    // A fixed node may sit on a small forest edge whose local patch is
                    // exhausted long before the surrounding region is. Draw from the
                    // nearest patch that still holds the same functional group, so the
                    // harvest stays inside one finite, auditable biomass pool.
                    if (vegetation.Biomass < mass)
                    {
                        pool = FindBiomassPool(ecology, patch, kind, mass) ?? patch;
                        vegetation = pool.Vegetation(kind);
                    }
                    if (vegetation.Biomass >= mass &&
                        (node.Item != ItemKind.Food || patch.SnowDepth < 8000))
                    {
                        pool.RemoveBiomass(kind, mass);
                        ecology.Balance.MaterialBiomassProduced += mass;
                        converted = true;
                    }
                }
                if (converted)
                {
                    node.Amount++;
                    node.RegenerationProgress = 0;
                    world.Materials.Produce(node.Item, 1);
                    ecology.Balance.NodeConversions[(int)node.Item]++;
                }
            }
        }

        private static HabitatPatchState FindFruitPool(EcologyState ecology, HabitatPatchState local, int amount)
        {
            HabitatPatchState best = null; int distance = int.MaxValue;
            foreach (var candidate in ecology.Habitats)
            {
                if (candidate.Region != local.Region || candidate.AvailableFruit < amount) { continue; }
                int d = candidate.Origin.ManhattanDistance(local.Origin);
                if (d < distance) { best = candidate; distance = d; }
            }
            return best;
        }

        private static HabitatPatchState FindWaterPool(EcologyState ecology, HabitatPatchState local)
        {
            HabitatPatchState best = null;
            int bestDistance = int.MaxValue;
            foreach (HabitatPatchState candidate in ecology.Habitats)
            {
                if (candidate.Id == local.Id || candidate.Region != local.Region || candidate.Water < 100) { continue; }
                int distance = local.Region < 0 && local.Origin.IsValid && candidate.Origin.IsValid ?
                    local.Origin.ManhattanDistance(candidate.Origin) : Math.Abs(candidate.Id - local.Id);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        private static HabitatPatchState FindBiomassPool(EcologyState ecology, HabitatPatchState local,
            VegetationKind kind, int mass)
        {
            HabitatPatchState best = null;
            int bestDistance = int.MaxValue;
            foreach (HabitatPatchState candidate in ecology.Habitats)
            {
                if (candidate.Id == local.Id || candidate.Region != local.Region) { continue; }
                VegetationState pool = candidate.Vegetation(kind);
                if (pool.Biomass < mass) { continue; }
                int distance = local.Region < 0 && local.Origin.IsValid && candidate.Origin.IsValid ?
                    local.Origin.ManhattanDistance(candidate.Origin) : Math.Abs(candidate.Id - local.Id);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        private static void UpdatePatch(WorldState world, HabitatPatchState patch, int dt)
        {
            EcologyBalance balance = world.Ecology.Balance;
            int temperature = world.Environment.Temperature + (patch.Region < 0 ? 0 :
                world.Ecology.Regions.MutableRegions[patch.Region].TemperatureOffset);
            var rng = new DeterministicRng(patch.RngState);
            int rain = 0;
            bool snowing = world.Environment.Weather == WeatherKind.Snow || world.Environment.Weather == WeatherKind.Blizzard;
            if (snowing)
            {
                rain = patch.LandCells * dt / 60;
            }
            else if (world.Date.Season != Season.Winter && rng.Chance(world.Date.Season == Season.Spring ? 25 : 12))
            {
                // Explicit finite seasonal precipitation, never an invisible reservoir.
                rain = patch.LandCells * rng.NextInt(1, 4) * dt / 60;
            }
            patch.RngState = rng.State;
            balance.Precipitation += rain;
            if (temperature < 0)
            {
                int snowRoom = Math.Max(0, patch.WaterCapacity * 2 - patch.SnowWater);
                int snow = Math.Min(rain, snowRoom);
                patch.SnowWater += snow;
                balance.Runoff += rain - snow;
            }
            else
            {
                int retained = Math.Min(rain, patch.WaterCapacity - patch.Water);
                patch.Water += retained;
                balance.Runoff += rain - retained;
                int melted = Math.Min(patch.SnowWater, Math.Max(1, temperature / 100) * patch.LandCells * dt / 60);
                patch.SnowWater -= melted;
                int retainedMelt = Math.Min(melted, patch.WaterCapacity - patch.Water);
                patch.Water += retainedMelt;
                balance.Runoff += melted - retainedMelt;
            }
            patch.SnowDepth = Math.Min(EnvironmentState.MaxSnowDepth, patch.LandCells == 0 ? 0 : patch.SnowWater * 100 / patch.LandCells);
            int exposed = Math.Max(1000, 10000 - patch.Shelter / 2);
            int evaporation = (int)Math.Min(patch.Water, (long)Math.Max(0, temperature / 500) * patch.LandCells * dt * exposed / (60L * 12 * 10000));
            patch.Water -= evaporation;
            balance.Evaporation += evaporation;
            foreach (VegetationKind kind in new[] { VegetationKind.Herbaceous, VegetationKind.Shrub, VegetationKind.Tree })
            {
                VegetationState vegetation = patch.Vegetation(kind);
                int seasonal = world.Date.Season == Season.Spring ? 10000 : world.Date.Season == Season.Summer ? 8000 :
                    world.Date.Season == Season.Autumn ? 4000 : kind == VegetationKind.Herbaceous ? 0 : 500;
                int interval = kind == VegetationKind.Herbaceous ? 240 : kind == VegetationKind.Shrub ? 480 : 1440;
                long denominator = 60L * interval * 10000 * 10000;
                long light = (long)world.Environment.Daylight * seasonal;
                vegetation.GrowthRemainder += (long)vegetation.Capacity * dt * light / 10000;
                // One factor of 10000 was divided above to avoid wide-map overflow.
                denominator /= 10000;
                int potential = (int)Math.Min(int.MaxValue, vegetation.GrowthRemainder / denominator);
                vegetation.GrowthRemainder %= denominator;
                int grown = Math.Min(potential, Math.Max(0, vegetation.Capacity - vegetation.Biomass));
                grown = Math.Min(grown, patch.Water * 4);
                grown = Math.Min(grown, patch.SoilFertility / 10);
                if (temperature < -1000 || vegetation.SeedBank == 0 && vegetation.Biomass == 0)
                {
                    grown = 0;
                }
                vegetation.Biomass += grown;
                if (world.Date.Season != Season.Winter && grown > 0)
                {
                    if (kind == VegetationKind.Herbaceous) { patch.RootFood = Math.Min(vegetation.Capacity / 3, patch.RootFood + grown / 3); }
                    if (kind == VegetationKind.Shrub)
                    {
                        patch.BrowseFood = Math.Min(vegetation.Capacity / 3, patch.BrowseFood + grown / 3);
                        if (world.Date.Season == Season.Summer || world.Date.Season == Season.Autumn)
                        { patch.FruitFood = Math.Min(vegetation.Capacity / 5, patch.FruitFood + grown / 5); patch.SeedFood = Math.Min(vegetation.Capacity / 20, patch.SeedFood + grown / 20); }
                    }
                    if (kind == VegetationKind.Tree) { patch.TwigFood = Math.Min(vegetation.Capacity / 20, patch.TwigFood + grown / 20); }
                }
                int growthWater = (grown + 3) / 4;
                patch.Water -= growthWater;
                balance.WaterUsed += growthWater;
                patch.SoilFertility = Math.Max(0, patch.SoilFertility - grown / 20);
                balance.Photosynthesis += grown;
                if (grown > 0)
                {
                    vegetation.SeedBank = Math.Min(10000, vegetation.SeedBank + Math.Max(1, grown / 10));
                }
                int turnover = world.Date.Season == Season.Autumn ? (int)((long)vegetation.Biomass * dt / (28 * GameCalendar.TicksPerDay)) :
                    world.Date.Season == Season.Winter && kind == VegetationKind.Herbaceous ? (int)((long)vegetation.Biomass * dt / (28 * GameCalendar.TicksPerDay)) : 0;
                patch.RemoveBiomass(kind, turnover);
                patch.Litter += turnover;
            }
            long decomposed = Math.Min(patch.Litter, Math.Max(1, patch.LandCells * dt / (temperature < 0 ? 240 : 60)));
            patch.Litter -= decomposed;
            balance.Decomposed += decomposed;
            patch.SoilFertility = Math.Min(10000, patch.SoilFertility + (int)Math.Min(10000, decomposed / 2));
        }

        private static void UpdateCarcasses(WorldState world)
        {
            for (int i = world.Ecology.MutableCarcasses.Count - 1; i >= 0; i--)
            {
                CarcassState carcass = world.Ecology.MutableCarcasses[i];
                long elapsed = world.Tick - carcass.LastUpdateTick;
                if (elapsed < 60)
                {
                    continue;
                }
                carcass.LastUpdateTick = world.Tick;
                int temperature = world.Environment.Temperature + (carcass.Region < 0 ? 0 :
                    world.Ecology.Regions.MutableRegions[carcass.Region].TemperatureOffset);
                int rate = temperature < 0 ? 2 : temperature < 1000 ? 20 : 60;
                int decay = checked((int)(elapsed * rate / 60));
                carcass.Freshness = Math.Max(0, carcass.Freshness - decay);
                carcass.DecayProgress += decay;
                int units = Math.Min(carcass.HarvestCount, carcass.DecayProgress / 1000);
                carcass.DecayProgress %= 1000;
                for (int item = 0; item < (int)ItemKind.Count && units > 0; item++)
                {
                    int amount = Math.Min(carcass.Harvest[item], units);
                    carcass.Harvest[item] -= amount;
                    units -= amount;
                    world.Ecology.FindHabitat(carcass.HabitatId).Litter += (long)amount * EcologyBalance.BiomassPerItem((ItemKind)item);
                }
                if (carcass.HarvestCount == 0 && carcass.Inventory.TotalCount == 0 &&
                    world.Tick - carcass.CreatedTick >= GameCalendar.TicksPerDay)
                {
                    world.Ecology.MutableCarcasses.RemoveAt(i);
                }
            }
        }
    }
}
