using System;
using System.Linq;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class WinterProductionTests
    {
        private static WorldState Empty(params string[] roles)
        {
            if (roles.Length == 0) { roles = new[] { "Builder" }; }
            var config = new WorldConfigData(731, 32, 32, roles.Length, 97 * 1440 + 720, 2, new[] { 1 });
            var world = new WorldState(config, new WorldMap(32, 32), new DeterministicRng(731));
            foreach (string role in roles)
            {
                var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity(role, role, 25),
                    new Personality(), world.Center.Offset(world.Entities.Npcs.Count * 3, 0), (ulong)(731 + world.Entities.Npcs.Count));
                world.Entities.AddNpc(npc);
                world.Market.IssuedCurrency += npc.Coins;
            }
            world.Environment.Temperature = 2000;
            world.Environment.Wind = 0;
            world.Environment.Weather = WeatherKind.Clear;
            world.Environment.Daylight = 10000;
            world.Environment.NextWeatherTick = long.MaxValue;
            world.Survival.RefillWater = false;
            world.Campaign.Initialize(world);
            world.Materials.Initialize(world);
            return world;
        }

        private static FacilityState Facility(WorldState world, FacilityKind kind)
        {
            var facility = new FacilityState(world.Survival.NextId++, kind, world.Center.Offset(0, 1), world.Center)
            {
                Progress = SurvivalRules.ConstructionTicks
            };
            world.Map.SetTile(facility.Position, TileType.Rock);
            world.Survival.MutableFacilities.Add(facility);
            return facility;
        }

        private static ShelterState Shelter(WorldState world, NpcState npc)
        {
            var shelter = new ShelterState(world.Survival.NextId++, npc.Home.Offset(0, -1), npc.Home, 4);
            world.Map.SetTile(shelter.Position, TileType.House);
            world.Survival.MutableShelters.Add(shelter);
            npc.Survival.ShelterId = shelter.Id;
            return shelter;
        }

        private static void Inputs(InventoryState inventory, ProductionRecipe recipe)
        {
            for (int i = 0; i < recipe.Inputs.Length; i += 2)
            {
                Assert.That(inventory.TryAdd((ItemKind)recipe.Inputs[i], recipe.Inputs[i + 1]), Is.True);
            }
        }

        private static void Conserved(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString());
            }
            Assert.That(world.Market.CurrencyError(world), Is.Zero);
        }

        private static bool Plan(WorldState world, NpcState npc, NpcGoalType goal = NpcGoalType.Produce)
        {
            return new SurvivalPlanner(new GridPathfinder()).TryBuild(world, npc,
                new NpcDecision(goal, npc.Position, 30, npc.RngState, NpcDecisionTrace.Empty), out _);
        }

        // Run transfer effects explicitly; recipe work durations are asserted separately.
        // This keeps these tests independent of Factory supplies and policy preferences.
        private static void CompletePlan(WorldState world, NpcState npc)
        {
            foreach (PlanStep step in npc.Plan.Steps)
            {
                if (step.Type == PlanStepType.Move) { npc.Position = step.Target; }
                else if (step.Type == PlanStepType.TakeFromStock)
                {
                    Assert.That(world.Resources.Reservations.Any(r => r.Owner == npc.Id && r.Kind == ReservationKind.Stockpile &&
                        r.Item == step.Item && r.Quantity >= step.Quantity), Is.True);
                    Assert.That(world.Resources.Stockpile.TryRemove(step.Item, step.Quantity), Is.True);
                    Assert.That(npc.Inventory.TryAdd(step.Item, step.Quantity), Is.True);
                }
                else if (step.Type == PlanStepType.Deposit)
                {
                    Assert.That(npc.Inventory.TryRemove(step.Item, step.Quantity), Is.True);
                    Assert.That(world.Resources.Stockpile.TryAdd(step.Item, step.Quantity), Is.True);
                }
                else if (step.Type == PlanStepType.Gather)
                {
                    ResourceNodeState node = world.Resources.FindNode(step.ResourceId);
                    Assert.That(node.ReservedBy, Is.EqualTo(npc.Id));
                    Assert.That(node.Amount, Is.GreaterThanOrEqualTo(step.Quantity));
                    node.Amount -= step.Quantity;
                    Assert.That(npc.Inventory.TryAdd(step.Item, step.Quantity), Is.True);
                }
                else if (step.Type == PlanStepType.Buy || step.Type == PlanStepType.Sell)
                {
                    var order = new TradeOrder(world.Market.NextOrderId, npc.Id,
                        step.Type == PlanStepType.Buy ? TradeSide.Buy : TradeSide.Sell,
                        step.Item, step.Quantity, step.UnitPrice);
                    Assert.That(world.Market.Execute(world, order, new System.Collections.Generic.List<SimEvent>()), Is.True, step.Summary);
                }
                else { Assert.That(ProductionSystem.Apply(world, npc, step), Is.True, step.Summary); }
                Conserved(world);
            }
            npc.Plan.StepIndex = npc.Plan.Steps.Count;
            world.Resources.Release(npc.Id);
            ProductionSystem.Release(world, npc);
        }

        [Test]
        public void Coat_EquipmentTransferIsNotAFifthToolOrProduction()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            for (int i = 0; i < 4; i++) { npc.Survival.SetTool((ItemKind)((int)ItemKind.Bow + i), 20 + i); }
            npc.Inventory.TryAdd(ItemKind.WinterCoat, 1);
            world.Materials.Initialize(world);
            Assert.That((int)ItemKind.Bow, Is.EqualTo(11));
            Assert.That((int)ItemKind.Bucket, Is.EqualTo(14));
            Assert.That((int)ItemKind.Arrow, Is.EqualTo(15));
            Assert.That((int)ItemKind.Count, Is.EqualTo(21));
            Assert.That(SurvivalRules.IsTool(ItemKind.WinterCoat), Is.False);
            var equip = new PlanStep(PlanStepType.Equip, npc.Position, item: ItemKind.WinterCoat);
            Assert.That(ProductionSystem.Apply(world, npc, equip), Is.True);
            Assert.That(npc.Survival.Tools.Length, Is.EqualTo(4));
            Assert.That(npc.Survival.Tools, Is.EqualTo(new[] { 20, 21, 22, 23 }));
            Assert.That(npc.Survival.Durability(ItemKind.WinterCoat), Is.Zero);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(120));
            Assert.That(npc.Survival.EquipmentCount(ItemKind.WinterCoat), Is.EqualTo(1));
            Assert.That(npc.Inventory.Count(ItemKind.WinterCoat), Is.Zero);
            Assert.That(world.Materials.Produced(ItemKind.WinterCoat), Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.WinterCoat), Is.Zero);
            Assert.That(ProductionSystem.Apply(world, npc, equip), Is.False);
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Equip, npc.Position, item: ItemKind.Arrow)), Is.False);
            Conserved(world);
        }

        [Test]
        public void GroundCoat_IsPickedIntoInventoryBeforeEquipping()
        {
            WorldState world = Empty("Farmer");
            NpcState npc = world.Entities.Npcs[0];
            GroundStockState ground = world.Survival.Drop(npc.Position);
            ground.Inventory.TryAdd(ItemKind.WinterCoat, 1);
            ground.ReservedBy = npc.Id;
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Pickup, ground.Position, ground.Id)), Is.True);
            Assert.That(npc.Inventory.Count(ItemKind.WinterCoat), Is.EqualTo(1));
            Assert.That(npc.Survival.CoatDurability, Is.Zero);
            Assert.That(ground.Empty, Is.True);
            Assert.That(ProductionSystem.Needed(world, npc), Is.True);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Equip));
            CompletePlan(world, npc);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(120));
            Assert.That(world.Materials.Produced(ItemKind.WinterCoat), Is.Zero);
            Conserved(world);
        }

        [TestCase(ItemKind.Arrow, FacilityKind.Workbench, 6, 8)]
        [TestCase(ItemKind.Food, FacilityKind.Purifier, 3, 10)]
        [TestCase(ItemKind.WinterCoat, FacilityKind.Workbench, 1, 16)]
        public void Recipes_ConsumeRealInputsOnceAndProduceFiniteBatches(ItemKind output, FacilityKind kind, int quantity, int ticks)
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            ProductionRecipe recipe = ProductionRecipe.For(output);
            Assert.That(recipe.Facility, Is.EqualTo(kind));
            Assert.That(recipe.Quantity, Is.EqualTo(quantity));
            Assert.That(recipe.Ticks, Is.EqualTo(ticks));
            FacilityState facility = Facility(world, kind);
            facility.ReservedBy = npc.Id;
            Inputs(facility.Inventory, recipe);
            world.Materials.Initialize(world);
            var craft = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, output, durationTicks: recipe.Ticks);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.True);
            Assert.That(facility.Inventory.Count(output), Is.EqualTo(quantity));
            for (int i = 0; i < recipe.Inputs.Length; i += 2)
            {
                ItemKind input = (ItemKind)recipe.Inputs[i];
                Assert.That(facility.Inventory.Count(input), Is.Zero);
                Assert.That(world.Materials.Consumed(input), Is.EqualTo(recipe.Inputs[i + 1]));
            }
            Assert.That(world.Materials.Produced(output), Is.EqualTo(quantity));
            Assert.That(npc.Survival.Crafted, Is.EqualTo(1));
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            Conserved(world);
        }

        [TestCase(ItemKind.Arrow)]
        [TestCase(ItemKind.Food)]
        [TestCase(ItemKind.WinterCoat)]
        public void Recipes_EveryMissingIngredientRejectsAtomically(ItemKind output)
        {
            ProductionRecipe recipe = ProductionRecipe.For(output);
            for (int missing = 0; missing < recipe.Inputs.Length; missing += 2)
            {
                WorldState world = Empty();
                NpcState npc = world.Entities.Npcs[0];
                FacilityState facility = Facility(world, recipe.Facility);
                facility.ReservedBy = npc.Id;
                Inputs(facility.Inventory, recipe);
                facility.Inventory.TryRemove((ItemKind)recipe.Inputs[missing], 1);
                world.Materials.Initialize(world);
                int count = facility.Inventory.TotalCount;
                var craft = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, output);
                Assert.That(ProductionSystem.Check(world, npc, craft), Is.False);
                Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
                Assert.That(facility.Inventory.TotalCount, Is.EqualTo(count));
                Assert.That(world.Materials.Produced(output), Is.Zero);
                Assert.That(npc.Survival.Crafted, Is.Zero);
                Conserved(world);
            }
        }

        [Test]
        public void Arrows_OutputSpaceIsCheckedAfterInputsWithoutPartialConsumption()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            FacilityState facility = Facility(world, FacilityKind.Workbench);
            facility.ReservedBy = npc.Id;
            Inputs(facility.Inventory, ProductionRecipe.For(ItemKind.Arrow));
            facility.Inventory.TryAdd(ItemKind.Seed, facility.Inventory.Capacity - facility.Inventory.TotalCount);
            world.Materials.Initialize(world);
            var craft = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, ItemKind.Arrow);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            Assert.That(facility.Inventory.Count(ItemKind.Wood), Is.EqualTo(1));
            Assert.That(facility.Inventory.TotalCount, Is.EqualTo(120));
            Conserved(world);
            facility.Inventory.TryRemove(ItemKind.Seed, 3);
            npc.Inventory.TryAdd(ItemKind.Seed, 3);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.True);
            Assert.That(facility.Inventory.TotalCount, Is.EqualTo(120));
            Conserved(world);
        }

        [TestCase(ItemKind.Arrow)]
        [TestCase(ItemKind.Food)]
        [TestCase(ItemKind.WinterCoat)]
        public void Recipes_RequireCorrectCompleteUsableReservedFacility(ItemKind output)
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            ProductionRecipe recipe = ProductionRecipe.For(output);
            FacilityState facility = Facility(world, recipe.Facility);
            Inputs(facility.Inventory, recipe);
            world.Materials.Initialize(world);
            var craft = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, output);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            facility.ReservedBy = npc.Id;
            facility.Progress = 0;
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            facility.Progress = SurvivalRules.ConstructionTicks;
            facility.Condition = 0;
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            facility.Condition = 100;
            npc.Position = npc.Position.Offset(1, 0);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            npc.Position = facility.WorkCell;
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Wait, facility.WorkCell, facility.Id, output)), Is.False);
            FacilityState wrong = Facility(world, recipe.Facility == FacilityKind.Workbench ? FacilityKind.Purifier : FacilityKind.Workbench);
            wrong.ReservedBy = npc.Id;
            Inputs(wrong.Inventory, recipe);
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Craft, wrong.WorkCell, wrong.Id, output)), Is.False);
            Conserved(world);
        }

        [Test]
        public void Hunter_WithBowTakesActualArrowsWithoutDepositingThem()
        {
            WorldState world = Empty("Hunter");
            NpcState npc = world.Entities.Npcs[0];
            npc.Survival.SetTool(ItemKind.Bow, 120);
            world.Resources.Stockpile.TryAdd(ItemKind.Arrow, 2);
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Needed(world, npc), Is.True);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.TakeFromStock));
            Assert.That(npc.Plan.Steps.Last().Item, Is.EqualTo(ItemKind.Arrow));
            Assert.That(npc.Plan.Steps.Any(s => s.Type == PlanStepType.Deposit), Is.False);
            CompletePlan(world, npc);
            Assert.That(npc.Inventory.Count(ItemKind.Arrow), Is.EqualTo(2));
            Assert.That(world.Resources.Stockpile.Count(ItemKind.Arrow), Is.Zero);
            Assert.That(world.Materials.Produced(ItemKind.Arrow), Is.Zero);
            Conserved(world);
        }

        [TestCase("Hunter")]
        [TestCase("Builder")]
        public void MissingArrows_ProposesWorkbenchRecipeWithRealWorkTime(string workerRole)
        {
            WorldState world = Empty(workerRole, "Hunter");
            NpcState worker = world.Entities.Npcs[0];
            world.Entities.Npcs[1].Survival.SetTool(ItemKind.Bow, 120);
            if (workerRole == "Hunter") { worker.Survival.SetTool(ItemKind.Bow, 120); }
            FacilityState facility = Facility(world, FacilityKind.Workbench);
            Inputs(facility.Inventory, ProductionRecipe.For(ItemKind.Arrow));
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Needed(world, worker), Is.True);
            Assert.That(Plan(world, worker), Is.True);
            PlanStep craft = worker.Plan.Steps.Last();
            Assert.That(craft.Type, Is.EqualTo(PlanStepType.Craft));
            Assert.That(craft.Item, Is.EqualTo(ItemKind.Arrow));
            Assert.That(craft.DurationTicks, Is.EqualTo(8));
            CompletePlan(world, worker);
            Assert.That(facility.Inventory.Count(ItemKind.Arrow), Is.EqualTo(6));
            Conserved(world);
        }

        [TestCase("Hunter")]
        [TestCase("Farmer")]
        [TestCase("Trader")]
        [TestCase("Builder")]
        [TestCase("Gatherer")]
        public void EveryProfession_CanAcquireAndWearAnExistingCoat(string role)
        {
            WorldState world = Empty(role);
            NpcState npc = world.Entities.Npcs[0];
            world.Resources.Stockpile.TryAdd(ItemKind.WinterCoat, 1);
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Needed(world, npc), Is.True);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Item, Is.EqualTo(ItemKind.WinterCoat));
            CompletePlan(world, npc);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(120));
            Assert.That(world.Materials.Produced(ItemKind.WinterCoat), Is.Zero);
            Conserved(world);
        }

        [Test]
        public void Cooking_StagesPartialCarriedMeatAndFiniteStockThenCooksInsteadOfRecyclingInputs()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            FacilityState facility = Facility(world, FacilityKind.Purifier);
            facility.Inventory.TryAdd(ItemKind.Wood, 1);
            npc.Inventory.TryAdd(ItemKind.RawMeat, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.RawMeat, 2);
            world.Materials.Initialize(world);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Deliver));
            Assert.That(npc.Plan.Steps.Last().Item, Is.EqualTo(ItemKind.RawMeat));
            Assert.That(npc.Plan.Steps.Last().Quantity, Is.EqualTo(1));
            CompletePlan(world, npc);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.All(s => s.Type != PlanStepType.TakeFacility), Is.True);
            Assert.That(npc.Plan.Steps.Last().Quantity, Is.EqualTo(2));
            CompletePlan(world, npc);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Craft));
            Assert.That(npc.Plan.Steps.Last().DurationTicks, Is.EqualTo(10));
            CompletePlan(world, npc);
            Assert.That(facility.Inventory.Food, Is.EqualTo(3));
            Assert.That(world.Materials.Consumed(ItemKind.RawMeat), Is.EqualTo(3));
            Assert.That(world.Materials.Consumed(ItemKind.Wood), Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void DrinkingWater_IsProcessedBeforeCookingInTheSharedFurnace()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            FacilityState facility = Facility(world, FacilityKind.Purifier);
            Inputs(facility.Inventory, ProductionRecipe.For(ItemKind.Water));
            facility.Inventory.TryAdd(ItemKind.RawMeat, 3);
            world.Survival.RefillWater = true;
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.CookingNeeded(world), Is.True);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Craft));
            Assert.That(npc.Plan.Steps.Last().Item, Is.EqualTo(ItemKind.Water));
            CompletePlan(world, npc);
            Assert.That(facility.Inventory.Water, Is.EqualTo(6));
            Assert.That(facility.Inventory.Count(ItemKind.RawMeat), Is.EqualTo(3));
            Conserved(world);
        }

        [Test]
        public void PartialSource_StagesOnlyTheReservedFiniteYield()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            FacilityState facility = Facility(world, FacilityKind.Purifier);
            facility.Inventory.TryAdd(ItemKind.Wood, 1);
            var source = new ResourceNodeState(1, npc.Position.Offset(1, 0), ItemKind.RawWater, 2, 100);
            world.Resources.AddNode(source);
            world.Survival.RefillWater = true;
            world.Materials.Initialize(world);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Single(s => s.Type == PlanStepType.Gather).Quantity, Is.EqualTo(2));
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Deliver));
            Assert.That(npc.Plan.Steps.Last().Quantity, Is.EqualTo(2));
            CompletePlan(world, npc);
            Assert.That(source.Amount, Is.Zero);
            Assert.That(facility.Inventory.Count(ItemKind.RawWater), Is.EqualTo(2));
            Assert.That(facility.Inventory.Water, Is.Zero);
            Assert.That(world.Materials.Produced(ItemKind.RawWater), Is.Zero);
            Conserved(world);
        }

        [Test]
        public void ColdCoat_UsesCarriedFiberDespiteMissingHideAndNeverDrainsRawIngredients()
        {
            WorldState world = Empty("Farmer");
            NpcState npc = world.Entities.Npcs[0];
            world.Environment.Temperature = 0;
            FacilityState facility = Facility(world, FacilityKind.Workbench);
            facility.Inventory.TryAdd(ItemKind.Hide, 1);
            npc.Inventory.TryAdd(ItemKind.Fiber, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Hide, 1);
            world.Resources.Stockpile.TryAdd(ItemKind.Fiber, 1);
            world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Needed(world, npc), Is.True);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Deliver));
            Assert.That(npc.Plan.Steps.Last().Item, Is.EqualTo(ItemKind.Fiber));
            CompletePlan(world, npc);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Plan(world, npc), Is.True);
                Assert.That(npc.Plan.Steps.Any(s => s.Type == PlanStepType.TakeFacility &&
                    (s.Item == ItemKind.Hide || s.Item == ItemKind.RawMeat)), Is.False);
                CompletePlan(world, npc);
            }
            Assert.That(facility.Inventory.Count(ItemKind.WinterCoat), Is.EqualTo(1));
            Assert.That(world.Materials.Consumed(ItemKind.Hide), Is.EqualTo(2));
            Assert.That(world.Materials.Consumed(ItemKind.Fiber), Is.EqualTo(2));
            Assert.That(Plan(world, npc), Is.True);
            CompletePlan(world, npc);
            Assert.That(world.Resources.Stockpile.Count(ItemKind.WinterCoat), Is.EqualTo(1));
            Assert.That(Plan(world, npc), Is.True);
            CompletePlan(world, npc);
            Assert.That(npc.Survival.CoatDurability, Is.EqualTo(120));
            Assert.That(ProductionSystem.CoatShortage(world), Is.False);
            Conserved(world);
        }

        [Test]
        public void ShelterSupply_IsFirstAndCanDeliverTheOnlyRealWood()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            world.Environment.Temperature = -1500;
            ShelterState shelter = Shelter(world, npc);
            FacilityState facility = Facility(world, FacilityKind.Workbench);
            Inputs(facility.Inventory, ProductionRecipe.For(ItemKind.WinterCoat));
            npc.Inventory.TryAdd(ItemKind.Wood, 1);
            world.Materials.Initialize(world);
            Assert.That(Plan(world, npc), Is.True);
            PlanStep delivery = npc.Plan.Steps.Last();
            Assert.That(delivery.Type, Is.EqualTo(PlanStepType.DeliverFuel));
            Assert.That(delivery.Quantity, Is.EqualTo(1));
            CompletePlan(world, npc);
            Assert.That(shelter.Inventory.Count(ItemKind.Wood), Is.EqualTo(1));
            Assert.That(shelter.HeatBudget, Is.Zero);
            Assert.That(world.Materials.Consumed(ItemKind.Wood), Is.Zero);
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.HeatBudget, Is.EqualTo(SurvivalRules.HeatPerWood - SurvivalRules.HeatPerTick));
            Assert.That(world.Materials.Consumed(ItemKind.Wood), Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void ShelterSupply_SortsWoodAs240MinutesRatherThan180()
        {
            WorldState world = Empty("Builder", "Builder");
            NpcState npc = world.Entities.Npcs[0];
            world.Environment.Temperature = -1500;
            ShelterState first = Shelter(world, npc);
            first.Inventory.TryAdd(ItemKind.Wood, 1);
            ShelterState second = Shelter(world, world.Entities.Npcs[1]);
            second.FuelTicks = 200;
            second.HeatBudget = 200 * SurvivalRules.HeatPerTick;
            npc.Inventory.TryAdd(ItemKind.Wood, 1);
            world.Materials.Initialize(world);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().ResourceId, Is.EqualTo(second.Id));
            Conserved(world);
        }

        [Test]
        public void FacilityFuel_CannotAlsoHeatAnEmptyShelterOrMeltSnow()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            world.Environment.Temperature = -1500;
            world.Environment.SnowDepth = 6;
            ShelterState shelter = Shelter(world, npc);
            FacilityState facility = Facility(world, FacilityKind.Purifier);
            facility.ReservedBy = npc.Id;
            Inputs(facility.Inventory, ProductionRecipe.For(ItemKind.Food));
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.WorkCell, shelter.Id, ItemKind.RawWater, 0, shelter.Entrance));
            world.Materials.Initialize(world);
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.HeatBudget, Is.Zero);
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, ItemKind.Food)), Is.True);
            ThermalSystem.UpdateWorld(world);
            Assert.That(shelter.FuelTicks, Is.Zero);
            Assert.That(shelter.HeatBudget, Is.Zero);
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.MeltSnow, shelter.Entrance, shelter.Id, quantity: 6)), Is.False);
            Assert.That(world.Environment.SnowDepth, Is.EqualTo(6));
            Assert.That(world.Materials.Consumed(ItemKind.Wood), Is.EqualTo(1));
            Conserved(world);
        }

        [Test]
        public void ShelterBurn_CannotFuelCookingAndCraftingDoesNotDebitTheRoomAgain()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            world.Environment.Temperature = -1500;
            ShelterState shelter = Shelter(world, npc);
            shelter.Inventory.TryAdd(ItemKind.Wood, 1);
            FacilityState facility = Facility(world, FacilityKind.Purifier);
            facility.ReservedBy = npc.Id;
            facility.Inventory.TryAdd(ItemKind.RawMeat, 3);
            npc.Inventory.TryAdd(ItemKind.Wood, 1);
            world.Materials.Initialize(world);
            var craft = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, ItemKind.Food);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            ThermalSystem.UpdateWorld(world);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.False);
            int heat = shelter.HeatBudget;
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Deliver, facility.WorkCell, facility.Id, ItemKind.Wood)), Is.True);
            Assert.That(ProductionSystem.Apply(world, npc, craft), Is.True);
            Assert.That(shelter.HeatBudget, Is.EqualTo(heat));
            Assert.That(world.Materials.Consumed(ItemKind.Wood), Is.EqualTo(2));
            Conserved(world);
        }

        [Test]
        public void MeltSnow_CompetesWithTheLastHeatingMinuteAndRejectsFreeOrExcessHeat()
        {
            WorldState melted = Empty();
            WorldState heated = Empty();
            foreach (WorldState world in new[] { melted, heated })
            {
                world.Environment.Temperature = -1500;
                world.Environment.SnowDepth = 6;
                ShelterState shelter = Shelter(world, world.Entities.Npcs[0]);
                shelter.Inventory.TryAdd(ItemKind.Wood, 1);
                world.Materials.Initialize(world);
                for (int i = 0; i < SurvivalRules.FuelTicksPerWood - 1; i++) { ThermalSystem.UpdateWorld(world); world.Tick++; }
                shelter.Temperature = 1000;
                Assert.That(shelter.HeatBudget, Is.EqualTo(SurvivalRules.HeatPerTick));
            }
            NpcState npc = melted.Entities.Npcs[0];
            ShelterState room = melted.Survival.Shelters[0];
            var melt = new PlanStep(PlanStepType.MeltSnow, room.Entrance, room.Id, quantity: 6);
            Assert.That(ProductionSystem.Apply(melted, npc, melt), Is.False, "A work-cell reservation is required.");
            melted.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.WorkCell, room.Id, ItemKind.RawWater, 0, room.Entrance));
            Assert.That(ProductionSystem.Apply(melted, npc, new PlanStep(PlanStepType.MeltSnow, room.Entrance, room.Id, quantity: int.MaxValue)), Is.False);
            Assert.That(ProductionSystem.Apply(melted, npc, melt), Is.True);
            Assert.That(room.HeatBudget, Is.EqualTo(SurvivalRules.HeatPerTick - 6 * SurvivalRules.SnowMeltHeatPerWater));
            Assert.That(melted.Environment.SnowDepth, Is.Zero);
            ThermalSystem.UpdateWorld(melted);
            ThermalSystem.UpdateWorld(heated);
            Assert.That(heated.Survival.Shelters[0].Temperature - room.Temperature, Is.EqualTo(6 * SurvivalRules.SnowMeltHeatPerWater));
            Assert.That(room.FuelTicks, Is.Zero);
            Assert.That(room.HeatBudget, Is.Zero);
            melted.Environment.SnowDepth = 6;
            room.FuelTicks = 10;
            Assert.That(ProductionSystem.Apply(melted, npc, melt), Is.False, "Remaining duration is not actual heat.");
            Assert.That(npc.Inventory.Count(ItemKind.RawWater), Is.EqualTo(6));
            Assert.That(melted.Materials.Produced(ItemKind.RawWater), Is.EqualTo(6));
            Assert.That(melted.Materials.Consumed(ItemKind.Wood), Is.EqualTo(1));
            Conserved(melted);
            Conserved(heated);
        }

        [Test]
        public void MeltSnowPlanner_UsesSmallFiniteSnowAndRechecksHeatAtExecution()
        {
            WorldState world = Empty();
            NpcState npc = world.Entities.Npcs[0];
            ShelterState shelter = Shelter(world, npc);
            world.Environment.Temperature = -1500;
            world.Environment.SnowDepth = 2;
            shelter.Inventory.TryAdd(ItemKind.Wood, 1);
            world.Materials.Initialize(world);
            ThermalSystem.UpdateWorld(world);
            Assert.That(Plan(world, npc, NpcGoalType.GatherWater), Is.True);
            Assert.That(npc.Plan.Steps.Any(s => s.Type == PlanStepType.MeltSnow && s.Quantity == 2), Is.True);
            PlanStep melt = npc.Plan.Steps.Single(s => s.Type == PlanStepType.MeltSnow);
            for (int i = 1; i < SurvivalRules.FuelTicksPerWood; i++) { ThermalSystem.UpdateWorld(world); }
            Assert.That(ProductionSystem.Apply(world, npc, melt), Is.False);
            Assert.That(world.Environment.SnowDepth, Is.EqualTo(2));
            Assert.That(world.Materials.Produced(ItemKind.RawWater), Is.Zero);
            Conserved(world);
        }

        [TestCase(ItemKind.Arrow)]
        [TestCase(ItemKind.Food)]
        [TestCase(ItemKind.WinterCoat)]
        public void InterruptedCraft_KeepsStagedInputsAndFinishedOutputForNextWorker(ItemKind output)
        {
            WorldState world = Empty("Builder", "Gatherer");
            NpcState first = world.Entities.Npcs[0];
            NpcState next = world.Entities.Npcs[1];
            ProductionRecipe recipe = ProductionRecipe.For(output);
            FacilityState facility = Facility(world, recipe.Facility);
            Inputs(facility.Inventory, recipe);
            world.Materials.Initialize(world);
            facility.ReservedBy = first.Id;
            world.Resources.TryReserve(new ResourceReservation(first.Id, ReservationKind.WorkCell, facility.Id, output, 0, facility.WorkCell));
            var craft = new PlanStep(PlanStepType.Craft, facility.WorkCell, facility.Id, output, durationTicks: recipe.Ticks);
            first.Plan.Start(NpcGoalType.Produce, new System.Collections.Generic.List<PlanStep> { craft });
            HealthSystem.Cancel(world, first, "critical_need_interrupt");
            Assert.That(ProductionSystem.HasInputs(facility.Inventory, recipe.Inputs), Is.True);
            Assert.That(facility.ReservedBy.IsNone, Is.True);
            Assert.That(world.Resources.Reservations.Count, Is.Zero);
            facility.ReservedBy = first.Id;
            Assert.That(ProductionSystem.Apply(world, first, craft), Is.True);
            HealthSystem.Cancel(world, first, "critical_need_interrupt");
            Assert.That(facility.Inventory.Count(output), Is.EqualTo(recipe.Quantity));
            Assert.That(ProductionSystem.Needed(world, next), Is.True);
            Assert.That(Plan(world, next), Is.True);
            Assert.That(next.Plan.Steps.Any(s => s.Type == PlanStepType.TakeFacility && s.Item == output), Is.True);
            long sold = next.Plan.Steps.Where(s => s.Type == PlanStepType.Sell && s.Item == output).Sum(s => (long)s.Quantity);
            CompletePlan(world, next);
            Assert.That(facility.Inventory.Count(output), Is.Zero);
            Assert.That(world.Resources.Stockpile.Count(output), Is.EqualTo(recipe.Quantity - sold));
            Assert.That(world.Market.Inventory.Count(output), Is.EqualTo(sold));
            Assert.That(world.Materials.Produced(output), Is.EqualTo(recipe.Quantity));
            Conserved(world);
        }

        [Test]
        public void ColdWorldWithoutMaterials_DoesNotCreateSitesOrInventoryOnRepeatedProposals()
        {
            WorldState world = Empty("Hunter");
            NpcState npc = world.Entities.Npcs[0];
            npc.Survival.SetTool(ItemKind.Bow, 120);
            world.Environment.Temperature = -1500;
            world.Materials.Initialize(world);
            int nextId = world.Survival.NextId;
            for (int i = 0; i < 50; i++)
            {
                Assert.That(Plan(world, npc), Is.False);
                Assert.That(world.Survival.Facilities.Count, Is.Zero);
                Assert.That(world.Resources.Reservations.Count, Is.Zero);
                Assert.That(world.Resources.Stockpile.TotalCount, Is.Zero);
                Assert.That(npc.Inventory.TotalCount, Is.Zero);
                Assert.That(world.Survival.NextId, Is.EqualTo(nextId));
                Conserved(world);
            }
        }

        [Test]
        public void MissingHide_StagesOnlyOneBoundedFiberBatchWithoutFinishedGoodsLoop()
        {
            WorldState world = Empty("Farmer");
            NpcState npc = world.Entities.Npcs[0];
            world.Environment.Temperature = 0;
            FacilityState facility = Facility(world, FacilityKind.Workbench);
            world.Resources.Stockpile.TryAdd(ItemKind.Fiber, 10);
            world.Materials.Initialize(world);
            Assert.That(Plan(world, npc), Is.True);
            Assert.That(npc.Plan.Steps.Last().Type, Is.EqualTo(PlanStepType.Deliver));
            Assert.That(npc.Plan.Steps.Last().Quantity, Is.EqualTo(2));
            CompletePlan(world, npc);
            for (int i = 0; i < 20; i++)
            {
                Assert.That(Plan(world, npc), Is.False);
                Assert.That(facility.Inventory.Count(ItemKind.Fiber), Is.EqualTo(2));
                Assert.That(world.Resources.Stockpile.Count(ItemKind.Fiber), Is.EqualTo(8));
                Assert.That(world.Materials.Produced(ItemKind.WinterCoat), Is.Zero);
                Conserved(world);
            }
        }
    }
}
