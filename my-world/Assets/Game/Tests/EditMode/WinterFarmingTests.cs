using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyAiWorld.App;
using MyAiWorld.App.Observability;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class WinterFarmingTests
    {
        private static WorldState World()
        {
            var world = WorldFactory.Create(new WorldConfigData(42, 32, 32, 0, GameCalendar.WinterStartTick, 2, new[] { 1 }));
            world.Environment.Daylight = 10000; world.Environment.Temperature = 1000; world.Environment.SnowDepth = 0;
            world.Environment.Weather = WeatherKind.Clear;
            return world;
        }
        private static FarmPlotState Plot(WorldState world, CropType crop = CropType.Spinach)
        {
            var p = world.Resources.FarmPlots[0]; p.BedId = 0; p.Crop = crop; p.Moisture = 1800;
            foreach (var bed in world.Survival.Facilities) { bed.MutablePlotIds.Remove(p.Id); }
            return p;
        }
        private static void Grow(WorldState world, int ticks)
        { for (int i = 0; i < ticks; i++) { AgricultureSystem.Update(world); world.Tick++; } }
        private static void Balance(WorldState world)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++) { Assert.That(world.Materials.Error(world, (ItemKind)i), Is.Zero, ((ItemKind)i).ToString()); }
            Assert.That(world.Market.CurrencyError(world), Is.Zero);
            Assert.That(world.Ecology.Balance.BiomassError(world.Ecology), Is.Zero);
            Assert.That(world.Ecology.Balance.WaterError(world.Ecology), Is.Zero);
        }
        [Test]
        public void WinterSeeds_AreFiniteAndOnlyColdHardyVegetablesExist()
        {
            Assert.That(CropDefinition.All.Select(c => c.Type), Is.EquivalentTo(new[] { CropType.Spinach, CropType.Kale, CropType.LambsLettuce }));
            Assert.That(CropDefinition.All.All(c => c.SeedYield == 0 && c.ColdTolerance < 0), Is.True);
            var world = World(); var plot = Plot(world, CropType.None);
            foreach (var c in CropDefinition.All) { world.Resources.Stockpile.TryRemove(c.Seed, world.Resources.Stockpile.Count(c.Seed)); }
            Assert.That(AgricultureSystem.ChooseCrop(world, plot), Is.Null);
            Assert.That(AgricultureSystem.Status(world, plot), Is.EqualTo("no_seeds"));
        }

        [Test]
        public void AutoPolicy_PrefersPlantableCropOverBalancingVarietyCounts()
        {
            var world = World();
            var plot = Plot(world, CropType.None);
            world.Environment.Temperature = 250;
            world.Resources.FarmPlots.First(p => p != plot).Crop = CropType.LambsLettuce;

            var crop = AgricultureSystem.ChooseCrop(world, plot);
            Assert.That(crop.Type, Is.EqualTo(CropType.LambsLettuce));
            Assert.That(AgricultureSystem.CanPlant(world, plot, crop), Is.True);
            Assert.That(AgricultureSystem.NeedsWork(world, plot), Is.True);

            // Cold beds still need a potential crop so workers can start heating.
            world.Environment.Temperature = -1500;
            Assert.That(AgricultureSystem.ChooseCrop(world, plot), Is.Not.Null);
        }

        [Test]
        public void AutoPolicy_WorkerSkipsSeedsAlreadyReservedByAnotherWorker()
        {
            var world = World();
            var plot = Plot(world, CropType.None);
            var farmer = Worker(world, plot);
            var other = Worker(world, plot);
            Assert.That(world.Resources.TryReserve(new ResourceReservation(other.Id,
                ReservationKind.Stockpile, 0, ItemKind.SpinachSeed,
                world.Resources.Stockpile.Count(ItemKind.SpinachSeed), world.Resources.StockpilePosition)), Is.True);

            Assert.That(AgricultureSystem.ChooseCrop(world, plot, farmer).Type, Is.EqualTo(CropType.Kale));
            farmer.Inventory.TryAdd(ItemKind.SpinachSeed, 1);
            Assert.That(AgricultureSystem.ChooseCrop(world, plot, farmer).Type, Is.EqualTo(CropType.Spinach));
        }

        [TestCase(-1)] [TestCase(-1500)]
        public void FrozenSoil_RejectsSowingAndWatering(int temperature)
        {
            var w = World(); var p = Plot(w, CropType.None); w.Environment.Temperature = temperature;
            Assert.That(AgricultureSystem.CanPlant(w, p, CropDefinition.Spinach), Is.False);
            p.Crop = CropType.Spinach;
            Assert.That(AgricultureSystem.CanWater(w, p), Is.False);
            Grow(w, 10); Assert.That(p.GrowthTicks, Is.Zero);
        }
        [Test]
        public void Growth_RequiresLightWaterAndWarmth_AndSnowReducesIt()
        {
            var w = World(); var p = Plot(w);
            Grow(w, 20); Assert.That(p.GrowthTicks, Is.EqualTo(20));
            w.Environment.Daylight = 0; Grow(w, 20); Assert.That(p.GrowthTicks, Is.EqualTo(20));
            w.Environment.Daylight = 10000; p.Moisture = 0; Grow(w, 20); Assert.That(p.GrowthTicks, Is.EqualTo(20));
            p.Moisture = 1800; w.Environment.SnowDepth = 5000; Grow(w, 20); Assert.That(p.GrowthTicks, Is.EqualTo(30));
        }
        [TestCase(CropType.Spinach, 2)] [TestCase(CropType.Kale, 3)] [TestCase(CropType.LambsLettuce, 2)]
        public void RepeatedHarvest_HasFiniteCutsAndRequiresRegrowth(CropType crop, int cuts)
        {
            var w = World(); var p = Plot(w, crop); int fertility = p.Fertility;
            for (int i = 0; i < cuts; i++)
            {
                p.GrowthTicks = p.GrowthRequired; AgricultureSystem.Harvest(p);
                Assert.That(p.Stage, Is.Not.EqualTo(CropStage.Mature));
            }
            Assert.That(p.Crop, Is.EqualTo(CropType.None)); Assert.That(p.TotalHarvests, Is.EqualTo(cuts));
            Assert.That(p.Fertility, Is.LessThan(fertility));
        }
        [Test]
        public void MatureCrop_StillDiesFromProlongedSevereFrost()
        {
            var w = World(); var p = Plot(w); p.GrowthTicks = p.GrowthRequired; w.Environment.Temperature = -2500;
            Grow(w, 1800); Assert.That(p.Crop, Is.EqualTo(CropType.None)); Assert.That(p.LostCrop, Is.True); Assert.That(w.Survival.CropsLost, Is.EqualTo(1));
        }
        [Test]
        public void Beds_AreBoundedNonOverlappingConnectedAndUseRealConstructionMaterials()
        {
            var w = World(); var ids = new HashSet<int>();
            foreach (var bed in w.Survival.Facilities.Where(b => b.Kind == FacilityKind.GrowingBed))
            {
                Assert.That(bed.PlotIds.Count, Is.InRange(1, 4)); Assert.That(bed.Complete, Is.False);
                foreach (int id in bed.PlotIds) { Assert.That(ids.Add(id), Is.True); Assert.That(w.Resources.FindPlot(id).BedId, Is.EqualTo(bed.Id)); }
                Assert.That(ProductionRecipe.ConstructionCost(bed.Kind).Length, Is.EqualTo(6));
            }
        }
        [Test]
        public void Heating_BurnsFiniteWood_CannotWarmOtherPlots_AndCoolsAfterFuelRunsOut()
        {
            var w = World(); var bed = w.Survival.Facilities.First(b => b.Kind == FacilityKind.GrowingBed);
            bed.Progress = SurvivalRules.ConstructionTicks; bed.Temperature = -1500; bed.Inventory.TryAdd(ItemKind.Wood, 1);
            w.Environment.Temperature = -1500; w.Environment.Daylight = 0; w.Materials.Initialize(w);
            for (int i = 0; i < 300; i++) { GrowingBedSystem.Update(w); }
            Assert.That(bed.Temperature, Is.GreaterThan(300)); Assert.That(w.Materials.Consumed(ItemKind.Wood), Is.EqualTo(1));
            var outside = w.Resources.FarmPlots.First(p => p.BedId == 0);
            Assert.That(GrowingBedSystem.SoilTemperature(w, outside), Is.EqualTo(-1500));
            for (int i = 0; i < 1800; i++) { GrowingBedSystem.Update(w); }
            Assert.That(bed.Temperature, Is.LessThan(0)); Assert.That(bed.HeatBudget, Is.Zero); Balance(w);
        }
        [Test]
        public void PolicyChanges_PreserveCurrentCrop_ThenStopRegrowth_AndRejectInvalidTargets()
        {
            var w = World(); var p = w.Resources.FarmPlots[0]; p.Crop = CropType.Spinach; p.GrowthTicks = 123;
            var context = new CommandContext(w.Tick, new List<SimEvent>());
            Assert.That(new SetFarmPlotPolicyCommand(p.Id, FarmCropPolicy.Kale).Apply(w, context), Is.True);
            Assert.That(p.Crop, Is.EqualTo(CropType.Spinach)); Assert.That(p.GrowthTicks, Is.EqualTo(123));
            Assert.That(w.Campaign.Assisted, Is.True); AgricultureSystem.Harvest(p); Assert.That(p.Crop, Is.EqualTo(CropType.None));
            ulong hash = StateHasher.Compute(w);
            Assert.That(new SetFarmPlotPolicyCommand(-1, FarmCropPolicy.Fallow).Apply(w, context), Is.False);
            Assert.That(new SetGrowingBedHeatPolicyCommand(p.BedId, (GrowingBedHeatPolicy)99).Apply(w, context), Is.False);
            Assert.That(StateHasher.Compute(w), Is.EqualTo(hash));
        }
        [Test]
        public void SavedPolicies_RoundTripBothCommandTypes()
        {
            var a = (SetFarmPlotPolicyCommand)SavedCommand.Capture(new SetFarmPlotPolicyCommand(7, FarmCropPolicy.Fallow)).Restore();
            var b = (SetGrowingBedHeatPolicyCommand)SavedCommand.Capture(new SetGrowingBedHeatPolicyCommand(8, GrowingBedHeatPolicy.Priority)).Restore();
            Assert.That(a.PlotId, Is.EqualTo(7)); Assert.That(a.Policy, Is.EqualTo(FarmCropPolicy.Fallow));
            Assert.That(b.BedId, Is.EqualTo(8)); Assert.That(b.Policy, Is.EqualTo(GrowingBedHeatPolicy.Priority));
        }
        [Test]
        public void SameSeedAndFarmCommands_AreDeterministicAtEveryTick()
        {
            var first = World(); var second = World(); var engine = new SimulationEngine();
            int id = first.Resources.FarmPlots[0].Id; int bedId = first.Resources.FarmPlots[0].BedId;
            for (int tick = 0; tick < 360; tick++)
            {
                var commands = new List<IWorldCommand>();
                if (tick == 0) { commands.Add(new SetFarmPlotPolicyCommand(id, FarmCropPolicy.Kale)); }
                if (tick == 30) { commands.Add(new SetGrowingBedHeatPolicyCommand(bedId, GrowingBedHeatPolicy.Priority)); }
                if (tick == 90) { commands.Add(new SetFarmPlotPolicyCommand(id, FarmCropPolicy.Fallow)); }
                if (tick == 180) { commands.Add(new SetGrowingBedHeatPolicyCommand(bedId, GrowingBedHeatPolicy.Off)); }
                Assert.That(engine.Tick(first, commands).StateHash, Is.EqualTo(engine.Tick(second, commands).StateHash), "minute " + tick);
            }
        }
        [Test]
        public void GrowingSnapshot_RestoresTemperatureFuelRegrowthAndPoliciesDeterministically()
        {
            var w = World(); var p = w.Resources.FarmPlots[0]; var bed = w.Survival.FindFacility(p.BedId);
            bed.Progress = SurvivalRules.ConstructionTicks; bed.Temperature = 800; bed.FuelTicks = 50; bed.HeatBudget = 1000; bed.SnowDepth = 100;
            p.Crop = CropType.Kale; p.HarvestCount = 1; p.TotalHarvests = 1; p.GrowthRemainder = 456789; p.Policy = FarmCropPolicy.Kale; p.Moisture = 900;
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(w)); var engine = new SimulationEngine();
            Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(w)));
            for (int i = 0; i < 300; i++) { engine.Tick(w, null); engine.Tick(restored, null); Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(w))); }
        }
        private static NpcState Worker(WorldState world, FarmPlotState plot)
        {
            var npc = WorldFactory.SpawnNpc(world, plot.Position, true, new List<SimEvent>());
            npc.Position = plot.Position;
            npc.Needs.Hunger = npc.Needs.Thirst = npc.Needs.Fatigue = 10000;
            return npc;
        }

        [Test]
        public void BedConstruction_MissingInputsIsAtomic_CompletionConsumesOnce_AndSnowWorkReleasesClaims()
        {
            var w = World(); var p = w.Resources.FarmPlots.First(f => f.BedId != 0);
            var n = Worker(w, p); var bed = GrowingBedSystem.Bed(w, p);
            n.Position = bed.WorkCell;
            bed.ReservedBy = n.Id;
            w.Resources.TryReserve(new ResourceReservation(n.Id, ReservationKind.WorkCell, bed.Id, ItemKind.Food, 0, bed.WorkCell));
            bed.Inventory.TryAdd(ItemKind.Wood, 4); bed.Inventory.TryAdd(ItemKind.Stone, 2);
            w.Materials.Initialize(w);
            var build = new PlanStep(PlanStepType.Build, bed.WorkCell, bed.Id);
            ulong before = StateHasher.Compute(w);
            Assert.That(ProductionSystem.Apply(w, n, build), Is.False);
            Assert.That(StateHasher.Compute(w), Is.EqualTo(before));
            bed.Inventory.TryAdd(ItemKind.Fiber, 2); w.Materials.Initialize(w);
            Assert.That(ProductionSystem.Apply(w, n, build), Is.True);
            Assert.That(ProductionSystem.Apply(w, n, build), Is.False);
            Assert.That(w.Materials.Consumed(ItemKind.Wood), Is.EqualTo(4));
            Assert.That(w.Materials.Consumed(ItemKind.Stone), Is.EqualTo(2));
            Assert.That(w.Materials.Consumed(ItemKind.Fiber), Is.EqualTo(2));
            bed.SnowDepth = 4000;
            Assert.That(ProductionSystem.Apply(w, n, new PlanStep(PlanStepType.ClearBedSnow, bed.WorkCell, bed.Id)), Is.True);
            Assert.That(bed.SnowDepth, Is.Zero);
            ProductionSystem.Release(w, n); w.Resources.Release(n.Id);
            Assert.That(bed.ReservedBy.IsNone && w.Resources.Reservations.Count == 0, Is.True);
            Balance(w);
        }

        [Test]
        public void HeatingPolicyChangedDuringDelivery_PreservesCarriedWood()
        {
            var w = World(); var p = w.Resources.FarmPlots.First(f => f.BedId != 0); var n = Worker(w, p);
            var bed = GrowingBedSystem.Bed(w, p); bed.Progress = SurvivalRules.ConstructionTicks; bed.ReservedBy = n.Id;
            n.Position = bed.WorkCell; n.Inventory.TryAdd(ItemKind.Wood, 2); w.Materials.Initialize(w);
            bed.HeatPolicy = GrowingBedHeatPolicy.Off;
            Assert.That(ProductionSystem.Apply(w, n, new PlanStep(PlanStepType.Deliver, bed.WorkCell, bed.Id, ItemKind.Wood, 2)), Is.False);
            Assert.That(n.Inventory.Count(ItemKind.Wood), Is.EqualTo(2)); Assert.That(bed.Inventory.TotalCount, Is.Zero);
            Balance(w);
        }

        [Test]
        public void PendingPlant_RevalidatesNewPolicyWithoutSpendingSeeds_AndReleasesPlot()
        {
            var w = World(); var p = w.Resources.FarmPlots[0]; var n = Worker(w, p);
            var bed = GrowingBedSystem.Bed(w, p); bed.Progress = SurvivalRules.ConstructionTicks; bed.Temperature = 1000;
            n.Inventory.TryAdd(ItemKind.SpinachSeed, 1); w.Materials.Initialize(w);
            w.Resources.TryReserve(new ResourceReservation(n.Id, ReservationKind.FarmPlot, p.Id, ItemKind.Food, 0, p.Position));
            n.Plan.Start(NpcGoalType.Farm, new List<PlanStep> { new PlanStep(PlanStepType.Plant, p.Position, p.Id, ItemKind.SpinachSeed) });
            new SimulationEngine().Tick(w, new[] { new SetFarmPlotPolicyCommand(p.Id, FarmCropPolicy.Fallow) });
            Assert.That(n.Inventory.Count(ItemKind.SpinachSeed), Is.EqualTo(1));
            Assert.That(p.Crop, Is.EqualTo(CropType.None)); Assert.That(p.ReservedBy.IsNone, Is.True);
            Balance(w);
        }

        [Test]
        public void FarmDelivery_OnlyCountsRealUnconsumedCargo_AndCannotCountResaleTwice()
        {
            var w = World(); var n = Worker(w, w.Resources.FarmPlots[0]); n.Position = w.Resources.StockpilePosition;
            n.Inventory.TryAdd(ItemKind.Food, 3); n.Inventory.UndeliveredFarmFood = 3; w.Materials.Initialize(w);
            n.Inventory.TryRemove(ItemKind.Food, 1); w.Materials.Consume(ItemKind.Food, 1);
            bool Trade(TradeSide side) => w.Market.Execute(w, new TradeOrder(w.Market.NextOrderId, n.Id, side, ItemKind.Food, 2, w.Market.Price(ItemKind.Food)), new List<SimEvent>());
            Assert.That(Trade(TradeSide.Sell), Is.True); Assert.That(w.Survival.FarmFoodDelivered, Is.EqualTo(2));
            Assert.That(Trade(TradeSide.Buy), Is.True); Assert.That(Trade(TradeSide.Sell), Is.True);
            Assert.That(w.Survival.FarmFoodDelivered, Is.EqualTo(2)); Balance(w);
        }

        [Test]
        public void UnderConstructionSnapshot_ResumesEveryTick_WithStagedMaterialsAndClaims()
        {
            var w = World(); var p = w.Resources.FarmPlots.First(f => f.BedId != 0); var n = Worker(w, p);
            var bed = GrowingBedSystem.Bed(w, p); n.Position = bed.WorkCell; bed.Progress = 10;
            var cost = ProductionRecipe.ConstructionCost(bed.Kind);
            for (int i = 0; i < cost.Length; i += 2) { bed.Inventory.TryAdd((ItemKind)cost[i], cost[i + 1]); }
            bed.ReservedBy = n.Id;
            w.Resources.TryReserve(new ResourceReservation(n.Id, ReservationKind.WorkCell, bed.Id, ItemKind.Food, 0, bed.WorkCell));
            n.Plan.Start(NpcGoalType.Farm, new List<PlanStep> { new PlanStep(PlanStepType.Build, bed.WorkCell, bed.Id, durationTicks: SurvivalRules.ConstructionTicks - bed.Progress) });
            w.Materials.Initialize(w);
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(w)); var engine = new SimulationEngine();
            for (int i = 0; i < 300; i++) { Assert.That(engine.Tick(restored, null).StateHash, Is.EqualTo(engine.Tick(w, null).StateHash), "minute " + i); }
            Balance(w);
        }

        [Test]
        public void PendingPolicies_SaveLoadThenExecuteOnce_WithImmutableObservation()
        {
            string folder = Path.Combine(Path.GetTempPath(), "WinterFarming-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var session = new GameSession(WorldConfigData.CreateDefault()))
                {
                    session.SaveDirectory = folder;
                    var plot = session.ReadModel.FarmPlots.First(p => p.BedId != 0);
                    var bed = session.ReadModel.Survival.GrowingBeds.First(b => b.Id == plot.BedId);
                    session.EnqueueCommand(new SetFarmPlotPolicyCommand(plot.Id, FarmCropPolicy.Kale));
                    session.EnqueueCommand(new SetGrowingBedHeatPolicyCommand(bed.Id, GrowingBedHeatPolicy.Priority));
                    Assert.That(session.Save(), Is.True, session.SaveStatus);
                    ulong expected = session.Tick().StateHash;
                    Assert.That(plot.Policy, Is.EqualTo(FarmCropPolicy.Auto)); Assert.That(bed.HeatPolicy, Is.EqualTo(GrowingBedHeatPolicy.Normal));
                    Assert.That(session.Load(), Is.True, session.SaveStatus);
                    Assert.That(session.QueuedCommandCount, Is.EqualTo(2)); Assert.That(session.Tick().StateHash, Is.EqualTo(expected));
                    Assert.That(session.QueuedCommandCount, Is.Zero);
                }
            }
            finally { if (Directory.Exists(folder)) { Directory.Delete(folder, true); } }
        }

        [TestCase("duplicate_binding")] [TestCase("fuel_without_heat_time")] [TestCase("invalid_policy")]
        public void CorruptWinterState_IsRejected(string corruption)
        {
            var w = World(); var p = w.Resources.FarmPlots.First(f => f.BedId != 0); var bed = GrowingBedSystem.Bed(w, p);
            if (corruption == "duplicate_binding") { bed.MutablePlotIds.Add(p.Id); }
            if (corruption == "fuel_without_heat_time") { bed.HeatBudget = 1; bed.FuelTicks = 0; }
            if (corruption == "invalid_policy") { p.Policy = (FarmCropPolicy)55; }
            Assert.Throws<InvalidDataException>(() => WorldSnapshot.Restore(WorldSnapshot.Capture(w)));
        }
        [TestCase(20261020)] [TestCase(42)] [TestCase(1337)] [TestCase(8675309)]
        [Category("LongRunning")]
        public void FourteenDays_AutonomousWinterFarming_HarvestsAndRecutsWithConservation(int seed)
        {
            var c = WorldConfigData.CreateDefault();
            var w = WorldFactory.Create(new WorldConfigData(seed, c.Width, c.Height, c.NpcCount, c.StartTick, c.TicksPerSecondAt1x, c.SpeedMultipliers, 14));
            var engine = new SimulationEngine(); int firstDay = 0; long producedFood = 0;
            var recutPlots = new HashSet<int>();
            var previousCuts = new int[w.Resources.FarmPlots.Count];
            var previousHarvests = new int[w.Resources.FarmPlots.Count];
            for (int i = 0; i < 14 * GameCalendar.TicksPerDay; i++)
            {
                engine.Tick(w, null);
                for (int plotIndex = 0; plotIndex < w.Resources.FarmPlots.Count; plotIndex++)
                {
                    var plot = w.Resources.FarmPlots[plotIndex];
                    // Count another cut of the same living plant, not two first
                    // harvests separated by crop death or a fresh sowing.
                    if (plot.TotalHarvests > previousHarvests[plotIndex] && previousCuts[plotIndex] > 0) { recutPlots.Add(plot.Id); }
                    previousCuts[plotIndex] = plot.HarvestCount;
                    previousHarvests[plotIndex] = plot.TotalHarvests;
                }
                if (firstDay == 0 && w.Survival.FarmFoodDelivered > 0) { firstDay = i / 1440 + 1; }
                if (i % 60 == 0) { Balance(w); }
                if (i % 1440 == 1439)
                {
                    Console.WriteLine("seed=" + seed + " day=" + (i / 1440 + 1) + " live=" + w.LivingCount + " harvested=" + w.Entities.Npcs.Sum(n => n.HarvestedFood) +
                        " crops=" + w.Resources.FarmPlots.Count(p => p.Crop != CropType.None) + " beds=" + string.Join(",", w.Survival.Facilities.Where(b => b.Kind == FacilityKind.GrowingBed).Select(b => b.Complete + "/" + b.Temperature + "/" + b.Inventory.Count(ItemKind.Wood))));
                }
            }
            Balance(w); producedFood = w.Entities.Npcs.Sum(n => (long)n.HarvestedFood); int recuts = recutPlots.Count;
            Console.WriteLine("RESULT seed=" + seed + " firstDeliveryDay=" + firstDay + " delivered=" + w.Survival.FarmFoodDelivered + " harvested=" + producedFood + " recutPlots=" + recuts + " consumedFood=" + w.Materials.Consumed(ItemKind.Food) + " live=" + w.LivingCount + " animals=" + w.Ecology.Animals.Count + " births=" + w.Ecology.Births + " deaths=" + w.Ecology.TotalDeaths);
            Assert.That(firstDay, Is.InRange(1, seed == 20261020 ? 7 : 14)); Assert.That(producedFood, Is.GreaterThan(0)); if (seed == 20261020) { Assert.That(recuts, Is.GreaterThan(0), "Default challenge must reach repeat harvest by day fourteen."); }
            Assert.That(producedFood, Is.LessThan(w.Materials.Consumed(ItemKind.Food)));
        }
    }
}
