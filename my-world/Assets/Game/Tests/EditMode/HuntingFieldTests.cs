using System.Collections.Generic;
using System.Linq;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Navigation;
using NUnit.Framework;
using static MyAiWorld.Tests.EditMode.HuntingGameplayTests;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class HuntingFieldTests
    {
        [Test]
        public void InterruptedLastMemberAtVillage_RecordsTheTaskResultOnlyOnce()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            npc.Position = npc.Home;
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            npc.Plan.Fail("interrupted");

            HuntingCoordinator.Update(world, new List<SimEvent>());
            HuntingCoordinator.Update(world, new List<SimEvent>());

            Assert.That(world.Hunting.Tasks, Is.Empty);
            Assert.That(world.Hunting.RecentResults, Has.Count.EqualTo(1));
            Assert.That(world.Hunting.RecentResults[0].Id, Is.EqualTo(task.Id));
            Assert.That(world.Hunting.RecentResults[0].Success, Is.False);
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(world)));
        }

        [TestCase(101, ItemKind.WinterCoat, ItemKind.Fur, ItemKind.Hide)]
        [TestCase(102, ItemKind.Arrow, ItemKind.Feather, ItemKind.Fiber)]
        public void PartiallyStagedAlternativeRecipe_KeepsItsIdWhileAnotherIngredientIsUnavailable(
            int id, ItemKind output, ItemKind alternative, ItemKind original)
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var facility = new FacilityState(world.Survival.NextId++, FacilityKind.Workbench, world.Center.Offset(0, 1), world.Center)
            { Progress = SurvivalRules.ConstructionTicks, PendingRecipeId = id };
            world.Map.SetTile(facility.Position, TileType.Rock);
            world.Survival.MutableFacilities.Add(facility);
            facility.Inventory.TryAdd(alternative, 1);
            world.Resources.Stockpile.TryAdd(original, 2);
            if (output == ItemKind.Arrow) { world.Resources.Stockpile.TryAdd(ItemKind.Wood, 1); }
            world.Materials.Initialize(world);

            Assert.That(ProductionRecipe.Select(world, npc, output).Id, Is.EqualTo(id));
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(ProductionRecipe.Select(restored, restored.Entities.Npcs[0], output).Id, Is.EqualTo(id));
        }

        [Test]
        public void RecoveryAfterLeavingHunt_ClearsTheRecoveryMovementAndDisplayState()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            npc.Survival.Stamina = 5900;
            npc.Survival.RecoveringStamina = true;
            npc.Survival.HuntMotion = HuntMotion.Recover;
            HuntingLocomotion.Refresh(world, npc);
            Assert.That(npc.Survival.RecoveringStamina, Is.False);
            Assert.That(npc.Survival.HuntMotion, Is.EqualTo(HuntMotion.Walk));
        }

        [Test]
        public void CancelDuringPreparation_ReleasesUnusedFoodWaterAndCoatsImmediately()
        {
            WorldState world = Arena(1);
            world.Environment.Temperature = -500;
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            Assert.That(world.Resources.Reservations.Any(r => r.Kind == ReservationKind.HuntSupply), Is.True);
            HuntingCoordinator.Retreat(world, task, "停止追猎", new List<SimEvent>());
            Assert.That(world.Resources.Reservations.Any(r => r.Kind == ReservationKind.HuntSupply), Is.False);
            Conserved(world);
        }

        [Test]
        public void IncomingArrow_DoesNotCountAsANearMissBeforeItPassesTheAnimal()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0]; var prey = Prey(world, AnimalSpecies.Rabbit);
            prey.Position = new GridPos(10, 5);
            AnimalEncounterSystem.NearMiss(world, new GridPos(6, 5), new GridPos(9, 5), npc.Id);
            Assert.That(prey.Encounter.ThreatId.IsNone, Is.True);
            AnimalEncounterSystem.NearMiss(world, new GridPos(9, 4), new GridPos(12, 4), npc.Id);
            Assert.That(prey.Encounter.ThreatId, Is.EqualTo(npc.Id));
            Assert.That(prey.Fleeing, Is.True);
        }

        [Test]
        public void MovingTeammates_FollowSingleFileWithoutBlockingTheWholeFutureRoute()
        {
            WorldState world = Arena(2);
            var task = Request(world, Prey(world)); task.Stage = HuntStage.Returning;
            for (int x = 16; x <= 24; x++)
            { world.Map.SetTile(new GridPos(x, 15), TileType.Rock); world.Map.SetTile(new GridPos(x, 17), TileType.Rock); }
            var leader = world.Entities.Npcs[0]; var follower = world.Entities.Npcs[1];
            leader.Position = new GridPos(20, 16); follower.Position = new GridPos(21, 16);
            Assert.That(HuntingNavigation.Move(world, leader, world.Resources.StockpilePosition, 1), Is.True);
            Assert.That(HuntingNavigation.Move(world, follower, world.Resources.StockpilePosition, 1), Is.True);
            Assert.That(leader.Position, Is.EqualTo(new GridPos(19, 16)));
            Assert.That(follower.Position, Is.EqualTo(new GridPos(20, 16)));
        }

        [Test]
        public void UnseenMarkedPrey_VisitsTheLastKnownCellRatherThanStoppingAtBowRange()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0]; var prey = Prey(world, AnimalSpecies.Rabbit);
            prey.Position = new GridPos(14, 5);
            world.Map.SetTile(new GridPos(10, 5), TileType.Forest);
            var task = Request(world, prey); task.Stage = HuntStage.Approaching;
            task.LastSeenTick = -1;
            for (int i = 0; i < 12 && npc.Position.X < 10; i++)
            { HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>()); world.Tick++; }
            Assert.That(npc.Position.X, Is.GreaterThanOrEqualTo(10));
            Assert.That(task.Stage, Is.Not.EqualTo(HuntStage.Retreating));
        }

        [Test]
        public void LastReleasedArrow_IsResolvedBeforeAmmoExhaustionCanCancelTheTeam()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0]; var prey = Prey(world, AnimalSpecies.Rabbit);
            var task = Request(world, prey); task.Stage = HuntStage.Engaging;
            npc.Position = prey.Position.Offset(-3, 0);
            npc.Inventory.TryRemove(ItemKind.Arrow, 3); world.Materials.Consume(ItemKind.Arrow, 3);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.True);
            for (int i = 0; i < 3; i++) { CombatSystem.Update(world, new List<SimEvent>()); world.Tick++; }
            Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.Zero);
            Assert.That(world.Combat.Projectiles.Any(p => p.Shooter == npc.Id && p.Phase == ProjectilePhase.Flying), Is.True);
            Assert.That(HuntingSafety.Check(world, task), Is.Null);
            Conserved(world);
        }

        [TestCase(101, ItemKind.WinterCoat, ItemKind.Fur, 2)]
        [TestCase(102, ItemKind.Arrow, ItemKind.Feather, 1)]
        public void AlternativeRecipes_UseLockedRealIngredientsAndPreserveTheBatchAcrossSave(int id, ItemKind output, ItemKind ingredient, int quantity)
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var facility = new FacilityState(world.Survival.NextId++, FacilityKind.Workbench, world.Center.Offset(0, 1), world.Center)
            { Progress = SurvivalRules.ConstructionTicks, Condition = 100, ReservedBy = npc.Id, PendingRecipeId = id };
            world.Map.SetTile(facility.Position, TileType.Rock);
            world.Survival.MutableFacilities.Add(facility);
            npc.Position = facility.WorkCell;
            var recipe = ProductionRecipe.ById(id);
            for (int i = 0; i < recipe.Inputs.Length; i += 2) { facility.Inventory.TryAdd((ItemKind)recipe.Inputs[i], recipe.Inputs[i + 1]); }
            var step = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, output, durationTicks: recipe.Ticks, recipeId: id);
            npc.Plan.Start(NpcGoalType.Produce, new List<PlanStep> { step });
            world.Materials.Initialize(world);
            Assert.That(ProductionRecipe.Select(world, npc, output).Id, Is.EqualTo(id));
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(restored.Entities.Npcs[0].Plan.CurrentStep.RecipeId, Is.EqualTo(id));
            foreach (WorldState branch in new[] { world, restored })
            {
                var maker = branch.Entities.Npcs[0];
                Assert.That(ProductionSystem.Apply(branch, maker, maker.Plan.CurrentStep), Is.True);
                Assert.That(branch.Materials.Consumed(ingredient), Is.EqualTo(quantity));
                Assert.That(branch.Survival.Facilities[0].Inventory.Count(output), Is.EqualTo(recipe.Quantity));
                Assert.That(branch.Survival.Facilities[0].PendingRecipeId, Is.Zero);
                Conserved(branch);
            }
            Assert.That(StateHasher.Compute(world), Is.EqualTo(StateHasher.Compute(restored)));
        }

        [Test]
        public void CompletedResult_RecordsEachActualReturnAndRestoresImmutably()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0]; var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = HuntStage.Returning;
            npc.Position = world.Resources.StockpilePosition;
            npc.Inventory.TryAdd(ItemKind.Fur, 1); world.Materials.Initialize(world);
            HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>());
            var result = world.Hunting.RecentResults.Single();
            Assert.That(result.Success, Is.True);
            Assert.That(result.Items[(int)ItemKind.Fur], Is.EqualTo(1));
            Assert.That(result.ReturnCells[0], Is.EqualTo(world.Resources.StockpilePosition));
            Assert.That(result.ReturnTicks[0], Is.EqualTo(world.Tick));
            var snapshot = new MyAiWorld.App.Query.HuntingReadModel(world);
            npc.Position = npc.Home;
            Assert.That(snapshot.RecentResults[0].ReturnCells[0], Is.EqualTo(world.Resources.StockpilePosition));
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(world)));
        }

        [Test]
        public void WarmCoatedHunter_IsNotSentHomeOnlyBecauseItIsWinterNight()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            world.Environment.Temperature = -900; world.Environment.Daylight = 0;
            npc.Survival.CoatDurability = 120;
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit)); task.Stage = HuntStage.Approaching;
            Assert.That(HuntingSafety.NeedsRetreat(world, npc), Is.False);
            npc.Survival.BodyTemperature = 3599;
            Assert.That(HuntingSafety.NeedsRetreat(world, npc), Is.True);
        }

        [Test]
        public void ExhaustedHunter_SlowsRecoversAndResumesWithoutRetreating()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var prey = Prey(world, AnimalSpecies.Rabbit);
            prey.Position = new GridPos(20, 10);
            var task = Request(world, prey);
            task.Stage = HuntStage.Approaching;
            npc.Survival.Stamina = 0;
            GridPos start = npc.Position;
            for (int i = 0; i < 5; i++)
            {
                HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>());
                world.Tick++;
            }
            Assert.That(npc.Position.ManhattanDistance(start), Is.InRange(1, 2));
            Assert.That(npc.Survival.RecoveringStamina, Is.True);
            Assert.That(npc.Survival.Stamina, Is.GreaterThan(1000));
            Assert.That(task.Stage, Is.Not.EqualTo(HuntStage.Retreating));
            for (int i = 0; i < 20; i++) { HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>()); world.Tick++; }
            Assert.That(npc.Survival.RecoveringStamina, Is.False);
            Assert.That(npc.Needs.Energy, Is.EqualTo(10000));
        }

        [Test]
        public void RecoveringHunter_CannotDrawEvenWithPlentyOfSleepEnergy()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var prey = Prey(world, AnimalSpecies.Rabbit);
            prey.ReservedBy = npc.Id;
            npc.Survival.Stamina = 1000;
            HuntingLocomotion.Refresh(world, npc);
            Assert.That(CombatSystem.TryStartHunt(world, npc, prey, new List<SimEvent>()), Is.False);
            Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(4));
        }

        [Test]
        public void Preparation_ReservesWholeTeamAndLeavesRealRationsInBags()
        {
            WorldState world = Arena(2);
            foreach (var npc in world.Entities.Npcs) { npc.Position = world.Resources.StockpilePosition.Offset(npc.Id.Value - 1, 0); }
            int food = world.Resources.Stockpile.Food, water = world.Resources.Stockpile.Water;
            var task = Request(world, Prey(world));
            Assert.That(world.Resources.AvailableStock(ItemKind.Food), Is.EqualTo(food - 2));
            foreach (var member in task.Members) { Assert.That(HuntingPreparation.Prepare(world, world.Entities.Npcs.Single(n => n.Id == member.NpcId), member, out _), Is.True); }
            Assert.That(world.Entities.Npcs.All(n => n.Inventory.Food == 1 && n.Inventory.Water == 1), Is.True);
            Assert.That(world.Resources.Stockpile.Food, Is.EqualTo(food - 2));
            Assert.That(world.Resources.Stockpile.Water, Is.EqualTo(water - 2));
            Conserved(world);
        }

        [Test]
        public void SharedFood_SecondTaskCannotConsumeTheFirstTeamsReservedRations()
        {
            WorldState world = Arena(2);
            world.Resources.Stockpile.TryRemove(ItemKind.Food, world.Resources.Stockpile.Food - 1);
            AnimalState first = Prey(world, AnimalSpecies.Rabbit), second = Prey(world, AnimalSpecies.Rabbit);
            Request(world, first, world.Entities.Npcs[0].Id);
            ulong hash = StateHasher.Compute(world);
            Assert.That(HuntingCoordinator.TryRequest(world, second.Id, new[] { world.Entities.Npcs[1].Id }, true, new List<SimEvent>(), out string reason), Is.False);
            Assert.That(reason, Does.Contain("食物"));
            Assert.That(StateHasher.Compute(world), Is.EqualTo(hash));
        }

        [Test]
        public void FieldRations_AreConsumedBeforeHungerCanCancelTheHunt()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = HuntStage.Approaching;
            npc.Inventory.TryAdd(ItemKind.Food, 1); npc.Inventory.TryAdd(ItemKind.Water, 1);
            npc.Needs.Hunger = 1490; npc.Needs.Thirst = 1700;
            world.Materials.Initialize(world);
            HuntingCoordinator.Update(world, new List<SimEvent>());
            Assert.That(task.Stage, Is.Not.EqualTo(HuntStage.Retreating));
            Assert.That(npc.Needs.Food, Is.EqualTo(5990));
            Assert.That(npc.Needs.Water, Is.EqualTo(7200));
            Conserved(world);
        }

        [Test]
        public void WinterPreparation_EquipsARealCoatWithoutCreatingAnotherTool()
        {
            WorldState world = Arena(1);
            world.Environment.Temperature = -500;
            var npc = world.Entities.Npcs[0]; npc.Position = world.Resources.StockpilePosition;
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            int coats = world.Resources.Stockpile.Count(ItemKind.WinterCoat);
            Assert.That(HuntingPreparation.Prepare(world, npc, task.Members[0], out _), Is.True);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(120));
            Assert.That(world.Resources.Stockpile.Count(ItemKind.WinterCoat), Is.EqualTo(coats - 1));
            Conserved(world);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThreatRecognition_StealthReducesDetectionButNeverProtectsAdjacentHunters(bool sneaking)
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var prey = Prey(world, AnimalSpecies.Deer);
            npc.Position = prey.Position.Offset(-4, 0);
            npc.Survival.HuntMotion = sneaking ? HuntMotion.Sneak : HuntMotion.Walk;
            AnimalEncounterSystem.Update(world, prey, new List<SimEvent>());
            Assert.That(prey.Encounter.ThreatId.IsNone, Is.EqualTo(sneaking));
            npc.Position = prey.Position.Offset(-1, 0);
            world.Tick++;
            AnimalEncounterSystem.Update(world, prey, new List<SimEvent>());
            Assert.That(prey.Encounter.ThreatId, Is.EqualTo(npc.Id));
        }

        [Test]
        public void ArrowDisturbance_RevealsImpactPositionWithoutReadingHiddenShooter()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0]; npc.Position = new GridPos(25, 25);
            var prey = Prey(world, AnimalSpecies.Rabbit);
            GridPos sound = prey.Position.Offset(1, 0);
            AnimalEncounterSystem.Disturb(world, sound, npc.Id, 1);
            Assert.That(prey.Fleeing, Is.True);
            Assert.That(prey.Encounter.LastThreatPosition, Is.EqualTo(sound));
            Assert.That(prey.Encounter.LastThreatPosition, Is.Not.EqualTo(npc.Position));
        }

        [TestCase(AnimalSpecies.Rabbit, ItemKind.Fur)]
        [TestCase(AnimalSpecies.Fox, ItemKind.Fur)]
        [TestCase(AnimalSpecies.Wolf, ItemKind.Fur)]
        [TestCase(AnimalSpecies.Boar, ItemKind.Hide)]
        [TestCase(AnimalSpecies.Deer, ItemKind.Hide)]
        [TestCase(AnimalSpecies.Raven, ItemKind.Feather)]
        public void SpeciesLoot_PreservesBiomassAndMaterializesOnlyAtHarvest(AnimalSpecies species, ItemKind item)
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0]; var prey = Prey(world, species);
            prey.Health = 0; prey.LastDamageSourceId = npc.Id; prey.DeathCause = AnimalDeathCause.Hunt;
            world.Ecology.ResolveDeaths(world, new List<SimEvent>());
            var body = world.Ecology.FindCarcass(prey.Id.Value);
            Assert.That(body.Remaining(item), Is.GreaterThan(0));
            int amount = body.Remaining(item);
            Assert.That(world.Materials.Produced(item), Is.Zero);
            npc.Position = body.Position;
            npc.Inventory.TryRemove(ItemKind.Arrow, 4); world.Materials.Consume(ItemKind.Arrow, 4);
            Assert.That(world.Ecology.HarvestCarcass(world, npc, body, new List<SimEvent>()), Is.True);
            Assert.That(npc.Inventory.Count(item) + body.Remaining(item), Is.EqualTo(amount));
            if (body.Remaining(item) > 0)
            {
                int meat = npc.Inventory.Count(ItemKind.RawMeat);
                npc.Inventory.TryRemove(ItemKind.RawMeat, meat); world.Resources.Stockpile.TryAdd(ItemKind.RawMeat, meat);
                world.Ecology.HarvestCarcass(world, npc, body, new List<SimEvent>());
                Assert.That(npc.Inventory.Count(item), Is.EqualTo(amount));
            }
            Conserved(world);
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(world)));
        }

        [Test]
        public void RecoveryAndReservedSupply_RoundTripAndContinueWithTheSameHash()
        {
            WorldState world = Arena(1);
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            task.Stage = HuntStage.Approaching;
            world.Entities.Npcs[0].Survival.Stamina = 100;
            var engine = new SimulationEngine(); engine.Tick(world, null);
            WorldState restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            for (int i = 0; i < 25; i++) { Assert.That(engine.Tick(world, null).StateHash, Is.EqualTo(engine.Tick(restored, null).StateHash)); Conserved(world); }
        }

        [Test]
        public void ReleasedSurvivalMember_KeepsReturnTrackingUntilActuallyBackInVillage()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            var task = Request(world, Prey(world, AnimalSpecies.Rabbit));
            npc.Position = new GridPos(12, 5); npc.Needs.Fatigue = 2000;
            HuntingCoordinator.Retreat(world, task, "过劳", new List<SimEvent>());
            Assert.That(HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>()), Is.False);
            Assert.That(world.Hunting.Tasks, Has.Count.EqualTo(1));
            Assert.That(world.Hunting.RecentResults, Is.Empty);
            npc.Needs.Fatigue = 8000;
            for (int i = 0; i < 20 && world.Hunting.Tasks.Count > 0; i++)
            { HuntingCoordinator.Update(world, new List<SimEvent>()); HuntingCoordinator.UpdateMember(world, npc, new List<SimEvent>()); world.Tick++; }
            Assert.That(npc.Position, Is.EqualTo(npc.Home));
            Assert.That(world.Hunting.Tasks, Is.Empty);
            Assert.That(world.Hunting.RecentResults.Single().Success, Is.False);
        }

        [Test]
        public void HuntingMovement_DoesNotSpendSleepEnergy()
        {
            WorldState world = Arena(1);
            var npc = world.Entities.Npcs[0];
            int energy = npc.Needs.Energy;
            Assert.That(HuntingNavigation.Move(world, npc, npc.Position.Offset(1, 0)), Is.True);
            Assert.That(npc.Needs.Energy, Is.EqualTo(energy));
        }

        [Test]
        public void MarkedDistantBoar_DoesNotChargeWithoutActualDisturbance()
        {
            WorldState world = Arena(2);
            AnimalState boar = Prey(world, AnimalSpecies.Boar);
            boar.Position = new GridPos(9, 5);
            Request(world, boar);
            AnimalEncounterSystem.Update(world, boar, new List<SimEvent>());
            Assert.That(world.Combat.GetAttack(boar.Id), Is.Null);
        }

        [Test]
        public void Gathering_RalliesAtVillageStockpile()
        {
            WorldState world = Arena(2);
            HuntTaskState task = Request(world, Prey(world));
            Assert.That(task.RallyCell, Is.EqualTo(world.Resources.StockpilePosition));
        }

        [Test]
        public void MissingFood_RejectsWholeTeamWithoutChangingWorld()
        {
            WorldState world = Arena(2);
            AnimalState prey = Prey(world);
            foreach (var npc in world.Entities.Npcs) { npc.Inventory.TryRemove(ItemKind.Food, npc.Inventory.Food); }
            world.Resources.Stockpile.TryRemove(ItemKind.Food, world.Resources.Stockpile.Food);
            world.Materials.Initialize(world);
            ulong before = StateHasher.Compute(world);
            Assert.That(HuntingCoordinator.TryRequest(world, prey.Id, null, true, new List<SimEvent>(), out string reason), Is.False);
            Assert.That(reason, Does.Contain("食物"));
            Assert.That(StateHasher.Compute(world), Is.EqualTo(before));
        }
    }
}
