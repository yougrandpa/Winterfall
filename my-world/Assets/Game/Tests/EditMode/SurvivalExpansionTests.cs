using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class SurvivalExpansionTests
    {
        private static WorldState Empty(int count = 1)
        {
            // Warm spring noon keeps focused mechanics tests independent of the
            // cold-weather survival priorities that now run in every tick.
            long start = GameCalendar.StartTick + 12L * GameCalendar.TicksPerDay + 6 * GameCalendar.MinutesPerHour;
            var world = new WorldState(new WorldConfigData(42, 32, 32, count, start, 2, new[] { 1 }), new WorldMap(32, 32), new DeterministicRng(42));
            for (int i = 0; i < count; i++)
            {
                var npc = new NpcState(world.AllocateEntityId(), new NpcIdentity("Test" + i, "Builder", 25), new Personality(), new GridPos(16 + i, 16), (ulong)(42 + i));
                world.Entities.AddNpc(npc); world.Market.IssuedCurrency += npc.Coins;
            }
            EnvironmentSystem.Update(world);
            world.Campaign.Initialize(world);
            world.Materials.Initialize(world); return world;
        }
        private static bool AdvanceRescue(WorldState world, NpcState npc, PlanStep step, GridPathfinder pathfinder, List<SimEvent> events, out bool complete)
        {
            complete = false;
            return HealthSystem.PrepareRescue(world, npc, step, pathfinder, out bool ready) &&
                (!ready || HealthSystem.CommitRescue(world, npc, step, events, out complete));
        }
        private static FacilityState Facility(WorldState world, FacilityKind kind, bool complete = true)
        {
            var f = new FacilityState(world.Survival.NextId++, kind, new GridPos(16, 17), new GridPos(16, 16));
            world.Map.SetTile(f.Position, TileType.Rock); f.Progress = complete ? SurvivalRules.ConstructionTicks : 0;
            f.ReservedBy = world.Entities.Npcs[0].Id; world.Survival.MutableFacilities.Add(f); return f;
        }
        private static void Conserved(WorldState world)
        {
            foreach (ItemKind item in Enum.GetValues(typeof(ItemKind))) { if (item != ItemKind.Count) { Assert.That(world.Materials.Error(world, item), Is.Zero, item.ToString()); } }
            Assert.That(world.Market.CurrencyError(world), Is.Zero);
        }
        [Test]
        public void Purification_MissingFuel_RejectsWithoutPartialConsumption()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0]; var f = Facility(world, FacilityKind.Purifier);
            f.Inventory.TryAdd(ItemKind.RawWater, 6); world.Materials.Initialize(world);
            var step = new PlanStep(PlanStepType.Craft, f.WorkCell, f.Id, ItemKind.Water);
            Assert.That(ProductionSystem.Apply(world, npc, step), Is.False); Assert.That(f.Inventory.Count(ItemKind.RawWater), Is.EqualTo(6)); Conserved(world);
            f.Inventory.TryAdd(ItemKind.Wood, 1); world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Apply(world, npc, step), Is.True);
            Assert.That(f.Inventory.Water, Is.EqualTo(6)); Assert.That(f.Inventory.Count(ItemKind.RawWater), Is.Zero); Conserved(world);
            Assert.That(ProductionSystem.Apply(world, npc, step), Is.False); Conserved(world);
        }
        [Test]
        public void Construction_ConsumesDeliveredMaterialsOnce_AndWellStartsEmpty()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0]; var f = Facility(world, FacilityKind.Well, false);
            f.Inventory.TryAdd(ItemKind.Wood, 4); f.Inventory.TryAdd(ItemKind.Stone, 4); world.Materials.Initialize(world);
            var step = new PlanStep(PlanStepType.Build, f.WorkCell, f.Id);
            Assert.That(ProductionSystem.Apply(world, npc, step), Is.True); Assert.That(f.Complete, Is.True);
            Assert.That(world.Resources.FindNode(f.WellNodeId).Amount, Is.Zero); Conserved(world);
            Assert.That(ProductionSystem.Apply(world, npc, step), Is.False);
            for (int i = 0; i < 20; i++) { world.Resources.Update(world.Materials); }
            Assert.That(world.Resources.FindNode(f.WellNodeId).Amount, Is.EqualTo(1)); Conserved(world);
        }
        [Test]
        public void Equipment_TransferWearAndBreak_AreAuditable()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0]; npc.Inventory.TryAdd(ItemKind.Bow, 1); world.Materials.Initialize(world);
            Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Equip, npc.Position, item: ItemKind.Bow)), Is.True);
            for (int i = 0; i < SurvivalRules.ToolDurability; i++) { Conserved(world); ProductionSystem.Wear(world, npc, ItemKind.Bow); }
            Assert.That(npc.HasHuntingTool, Is.False); Conserved(world);
        }
        [Test]
        public void RawWater_SeededDiseaseAndTreatment_ConsumeRealSupplies()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0]; bool sick = false;
            npc.Inventory.TryAdd(ItemKind.RawWater, 10); world.Materials.Initialize(world);
            for (int i = 0; i < 10; i++) { Assert.That(ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.DrinkRaw, npc.Position)), Is.True); sick |= npc.Survival.DiseaseTicks > 0; }
            Assert.That(sick, Is.True); Conserved(world);
            npc.Inventory.TryAdd(ItemKind.Medicine, 1); world.Materials.Produce(ItemKind.Medicine, 1);
            ProductionSystem.Apply(world, npc, new PlanStep(PlanStepType.Treat, npc.Position)); Assert.That(npc.Survival.DiseaseTicks, Is.Zero); Conserved(world);
        }
        [Test]
        public void Crops_DroughtWinterRotationAndRest_HaveDifferentOutcomes()
        {
            var world = Empty(0); var plot = new FarmPlotState(1, new GridPos(3, 3)) { Crop = CropType.Wheat, Moisture = 1000, Fertility = 5000 };
            world.Resources.AddPlot(plot);
            Assert.That(AgricultureSystem.ChooseCrop(world, plot).Type, Is.EqualTo(CropType.Bean));
            AgricultureSystem.Update(world); Assert.That(plot.GrowthTicks, Is.EqualTo(1));
            world.Tick = 84 * 1440 + 1; AgricultureSystem.Update(world); Assert.That(plot.GrowthTicks, Is.EqualTo(1));
            plot.Moisture = 0;
            for (int i = 0; i < SurvivalRules.CropDeathDamage; i++) { AgricultureSystem.Update(world); world.Tick++; }
            Assert.That(plot.Crop, Is.EqualTo(CropType.None)); Assert.That(world.Survival.CropsLost, Is.EqualTo(1)); Assert.That(plot.Fertility, Is.GreaterThan(5000));
        }
        [Test]
        public void Farming_BoundedBatch_ReservesNoMoreThanFourAndReleasesOnInterrupt()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0];
            for (int i = 0; i < 6; i++) { world.Resources.AddPlot(new FarmPlotState(i + 1, npc.Position.Offset(i % 3, i / 3)) { Crop = CropType.Turnip, Moisture = 10 }); }
            npc.Inventory.TryAdd(ItemKind.RawWater, 4); world.Materials.Initialize(world);
            var planner = new SurvivalPlanner(new GridPathfinder());
            Assert.That(planner.TryBuild(world, npc, new NpcDecision(NpcGoalType.Farm, npc.Position, 80, npc.RngState, NpcDecisionTrace.Empty), out string failure), Is.True, failure);
            Assert.That(npc.Plan.Steps.Count(s => s.Type == PlanStepType.Water), Is.EqualTo(4));
            npc.Needs.Thirst = 100; new SimulationEngine().Tick(world, null);
            Assert.That(world.Resources.FarmPlots.All(p => p.ReservedBy.IsNone), Is.True); Conserved(world);
        }
        [Test]
        public void Hunting_RangeOcclusionAndFullBagDrop_PreserveSingleYield()
        {
            var world = Empty();
            world.Ecology.Initialize(world, world.Map.CollectReachable(world.Center));
            var npc = world.Entities.Npcs[0];
            npc.Action.SetIdle("Controlled hunt", long.MaxValue);
            npc.HasHuntingTool = true;
            npc.Inventory.TryAdd(ItemKind.Arrow, 4);
            var prey = world.Ecology.Spawn(world, AnimalSpecies.Rabbit, npc.Position.Offset(3, 0), 1500);
            prey.Health = 1;
            prey.ReservedBy = npc.Id;
            world.Materials.Initialize(world);
            world.Map.SetTile(npc.Position.Offset(1, 0), TileType.Rock);
            Assert.That(world.Ecology.Strike(world, npc, prey, new List<SimEvent>()), Is.False);
            world.Map.SetTile(npc.Position.Offset(1, 0), TileType.Grass);
            Assert.That(world.Ecology.Strike(world, npc, prey, new List<SimEvent>()), Is.True);
            var engine = new SimulationEngine();
            for (int i = 0; i < 12 && prey.Health > 0; i++) { engine.Tick(world, null); }
            Assert.That(prey.Health, Is.Zero);
            CarcassState carcass = world.Ecology.FindCarcass(prey.Id.Value);
            Assert.That(carcass, Is.Not.Null, "A kill creates one carcass, not ready-made food.");
            Assert.That(carcass.Id.Value, Is.EqualTo(prey.Id.Value));
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(npc.Inventory.Count(ItemKind.RawMeat), Is.Zero);
            Assert.That(world.Ecology.Strike(world, npc, prey, new List<SimEvent>()), Is.False);
            Conserved(world);
        }
        [Test]
        public void Rescue_ZeroHealthClaimsTransferAndRecovery_ResumeDeterministically()
        {
            var world = Empty(3); var victim = world.Entities.Npcs[0]; var rescuer = world.Entities.Npcs[1]; var other = world.Entities.Npcs[2];
            victim.Needs.Health = 0; victim.Position = victim.Home.Offset(4, 0);
            rescuer.Inventory.TryAdd(ItemKind.Medicine, 1); other.Inventory.TryAdd(ItemKind.Medicine, 1); world.Materials.Initialize(world);
            var engine = new SimulationEngine(); engine.Tick(world, null);
            Assert.That(victim.Survival.Life, Is.EqualTo(LifeState.Downed));
            var planner = new SurvivalPlanner(new GridPathfinder()); HealthSystem.Cancel(world, rescuer, "test"); HealthSystem.Cancel(world, other, "test");
            Assert.That(planner.TryBuild(world, rescuer, new NpcDecision(NpcGoalType.Rescue, victim.Position, 180, rescuer.RngState, NpcDecisionTrace.Empty), out string failure), Is.True, failure);
            Assert.That(planner.TryBuild(world, other, new NpcDecision(NpcGoalType.Rescue, victim.Position, 180, other.RngState, NpcDecisionTrace.Empty), out failure), Is.False);
            var copy = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            for (int i = 0; i < 30; i++) { Assert.That(engine.Tick(world, null).StateHash, Is.EqualTo(engine.Tick(copy, null).StateHash)); }
            Assert.That(victim.Survival.Life, Is.EqualTo(LifeState.Recovering)); Assert.That(victim.Position, Is.EqualTo(victim.Home));
            Assert.That(rescuer.Survival.Rescues, Is.EqualTo(1)); Assert.That(victim.Survival.Rescuer.IsNone, Is.True); Conserved(world);
        }
        [Test]
        public void Rescue_InterruptedTransport_IsClaimedByAnotherRescuerWithoutTeleport()
        {
            var world = Empty(3); var victim = world.Entities.Npcs[0]; var first = world.Entities.Npcs[1]; var second = world.Entities.Npcs[2];
            victim.Position = victim.Home.Offset(4, 0); victim.Needs.Health = 0;
            HealthSystem.Update(world, victim, new List<SimEvent>());
            first.Position = victim.Position; first.Inventory.TryAdd(ItemKind.Medicine, 1); world.Materials.Initialize(world);
            var planner = new SurvivalPlanner(new GridPathfinder());
            Assert.That(planner.TryBuild(world, first, new NpcDecision(NpcGoalType.Rescue, victim.Position, 180, first.RngState, NpcDecisionTrace.Empty), out string failure), Is.True);
            Assert.That(AdvanceRescue(world, first, first.Plan.CurrentStep, new GridPathfinder(), new List<SimEvent>(), out bool done), Is.True);
            Assert.That(done, Is.False); Assert.That(victim.Survival.Life, Is.EqualTo(LifeState.Recovering));
            HealthSystem.Cancel(world, first, "critical_need_interrupt"); second.Position = new GridPos(5, 5);
            Assert.That(planner.TryBuild(world, second, new NpcDecision(NpcGoalType.Rescue, victim.Position, 180, second.RngState, NpcDecisionTrace.Empty), out failure), Is.True, failure);
            GridPos previous = second.Position;
            AdvanceRescue(world, second, second.Plan.CurrentStep, new GridPathfinder(), new List<SimEvent>(), out done);
            Assert.That(previous.ManhattanDistance(second.Position), Is.EqualTo(1));
            for (int i = 0; i < 40 && !done; i++) { world.Tick++; AdvanceRescue(world, second, second.Plan.CurrentStep, new GridPathfinder(), new List<SimEvent>(), out done); }
            Assert.That(done, Is.True); Assert.That(victim.Position, Is.EqualTo(victim.Home)); Conserved(world);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void Recovery_UsesDeliveredFoodAndWater_WithoutLeavingBed(bool fullBag)
        {
            var world = Empty(2); var victim = world.Entities.Npcs[0]; var helper = world.Entities.Npcs[1];
            victim.Survival.Life = LifeState.Recovering; victim.Needs.Health = 1500; victim.Needs.Hunger = 1000; victim.Needs.Thirst = 1000;
            if (fullBag) { victim.Inventory.TryAdd(ItemKind.Seed, victim.Inventory.Capacity); }
            helper.Inventory.TryAdd(ItemKind.Water, 1); helper.Inventory.TryAdd(ItemKind.Food, 1); world.Materials.Initialize(world);
            Assert.That(HealthSystem.NeedsRescue(victim), Is.True);
            var planner = new SurvivalPlanner(new GridPathfinder());
            Assert.That(planner.TryBuild(world, helper, new NpcDecision(NpcGoalType.Rescue, victim.Position, 180, helper.RngState, NpcDecisionTrace.Empty), out string failure), Is.True, failure);
            AdvanceRescue(world, helper, helper.Plan.CurrentStep, new GridPathfinder(), new List<SimEvent>(), out bool done);
            Assert.That(done, Is.True); HealthSystem.Update(world, victim, new List<SimEvent>());
            Assert.That(victim.Needs.Food, Is.GreaterThan(3000)); Assert.That(victim.Needs.Water, Is.GreaterThan(3000));
            Assert.That(victim.Position, Is.EqualTo(victim.Home)); Conserved(world);
        }

        [Test]
        public void Death_TimeoutLeavesAuditableBelongingsAndAllDeadWorldKeepsTicking()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0]; npc.HasHuntingTool = true; npc.Inventory.TryAdd(ItemKind.Food, 3); world.Materials.Initialize(world);
            npc.Needs.Health = 0; var engine = new SimulationEngine(); engine.Tick(world, null);
            for (int i = 0; i <= SurvivalRules.RescueWindow; i++) { engine.Tick(world, null); }
            Assert.That(npc.Survival.Life, Is.EqualTo(LifeState.Dead)); Assert.That(npc.Inventory.TotalCount, Is.Zero); Assert.That(npc.Coins, Is.Zero);
            Assert.That(world.LivingCount, Is.Zero); Assert.That(world.Survival.Ground.Count, Is.EqualTo(1)); Conserved(world);
            var restored = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
            Assert.That(engine.Tick(restored, null).StateHash, Is.EqualTo(engine.Tick(world, null).StateHash));
        }
        [Test]
        public void RawWaterDelivery_DoesNotCreateUnsupportedMarketOrders()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0];
            world.Resources.AddNode(new ResourceNodeState(1, npc.Position, ItemKind.RawWater, 30, 12)); world.Materials.Initialize(world);
            var planner = new SurvivalPlanner(new GridPathfinder());
            Assert.That(planner.TryBuild(world, npc, new NpcDecision(NpcGoalType.GatherWater, npc.Position, 30, npc.RngState, NpcDecisionTrace.Empty), out string failure), Is.True, failure);
            Assert.That(npc.Plan.Steps.Any(s => s.Type == PlanStepType.Sell), Is.False);
            var engine = new SimulationEngine();
            for (int i = 0; i < 40 && npc.Plan.IsActive; i++) { engine.Tick(world, null); }
            Assert.That(world.Resources.Stockpile.Count(ItemKind.RawWater), Is.GreaterThan(0));
            Assert.That(world.Market.Ledger.Count, Is.Zero); Conserved(world);
        }
        [Test]
        public void Irrigation_ConsumesCisternRawWater_AndLeavesDrinkingReserveUntouched()
        {
            var world = Empty(); var cistern = Facility(world, FacilityKind.Cistern); cistern.ReservedBy = EntityId.None;
            var irrigation = new FacilityState(world.Survival.NextId++, FacilityKind.Irrigation, new GridPos(20, 17), new GridPos(20, 16)) { Progress = SurvivalRules.ConstructionTicks };
            world.Survival.MutableFacilities.Add(irrigation); world.Map.SetTile(irrigation.Position, TileType.Rock);
            var plot = new FarmPlotState(1, new GridPos(21, 18)) { Crop = CropType.Bean, Moisture = 0 }; world.Resources.AddPlot(plot);
            cistern.Inventory.TryAdd(ItemKind.RawWater, 1); world.Resources.Stockpile.TryAdd(ItemKind.Water, 8); world.Materials.Initialize(world);
            WaterSystem.Update(world); Assert.That(plot.Moisture, Is.EqualTo(900)); Assert.That(world.Resources.Stockpile.Water, Is.EqualTo(8)); Conserved(world);
            plot.Moisture = 0; WaterSystem.Update(world); Assert.That(plot.Moisture, Is.Zero); Conserved(world);
        }
        [Test]
        public void ConstructionInterrupt_ReleasesFacilityAndWorkCellTogether()
        {
            var world = Empty(); var npc = world.Entities.Npcs[0]; var f = Facility(world, FacilityKind.Purifier, false);
            f.Inventory.TryAdd(ItemKind.Wood, 2); f.Inventory.TryAdd(ItemKind.Stone, 4); world.Materials.Initialize(world);
            world.Resources.TryReserve(new ResourceReservation(npc.Id, ReservationKind.WorkCell, f.Id, ItemKind.Food, 0, f.WorkCell));
            npc.Plan.Start(NpcGoalType.Produce, new List<PlanStep> { new PlanStep(PlanStepType.Build, f.WorkCell, f.Id, durationTicks: 30) });
            new SimulationEngine().Tick(world, null); npc.Needs.Thirst = 100; new SimulationEngine().Tick(world, null);
            Assert.That(f.ReservedBy.IsNone, Is.True); Assert.That(world.Resources.Reservations.Count, Is.Zero);
            Assert.That(f.Inventory.Count(ItemKind.Stone), Is.EqualTo(4)); Conserved(world);
        }

        [Test]
        public void DefaultWorld_FullYear_ConservesAllItemsAndMaintainsProduction()
        {
            // The winter challenge is a bounded 14-day settlement. Long-term observation
            // is the supported mode for year-round ecology and material accounting.
            var world = WorldFactory.Create(WorldConfigData.CreateObservationDefault()); var engine = new SimulationEngine();
            long startTick = world.Tick;
            Directory.CreateDirectory(Path.Combine(UnityEngine.Application.dataPath, "../Logs/TestResults"));
            int completedDays = 0;
            for (int i = 0; i < GameCalendar.DaysPerYear * 1440; i++)
            {
                engine.Tick(world, null); Conserved(world);
                foreach (AnimalSpecies species in Enum.GetValues(typeof(AnimalSpecies))) { Assert.That(world.Ecology.Count(species), Is.LessThanOrEqualTo(EcologyState.SpeciesCapacity)); }
                if ((i + 1) % 1440 == 0)
                {
                    completedDays++;
                    if (completedDays <= GameCalendar.DefaultChallengeDays) { Assert.That(world.LivingCount, Is.GreaterThan(0), "Day " + completedDays); }
                    if (completedDays % 28 == 0)
                    {
                        var copy = WorldSnapshot.Restore(WorldSnapshot.Capture(world));
                        Assert.That(StateHasher.Compute(copy), Is.EqualTo(StateHasher.Compute(world)));
                        File.WriteAllText(Path.Combine(UnityEngine.Application.dataPath, "../Logs/TestResults/Survival-Season-" + completedDays + ".json"),
                            "{\"day\":" + completedDays + ",\"living\":" + world.LivingCount + ",\"water\":" + world.Resources.Stockpile.Water + ",\"food\":" + world.Resources.Stockpile.Food + ",\"hash\":\"" + StateHasher.ToHex(StateHasher.Compute(world)) + "\"}");
                    }
                }
            }
            Assert.That(world.Tick, Is.EqualTo(startTick + GameCalendar.DaysPerYear * 1440L));
            Assert.That(world.Campaign.Phase, Is.EqualTo(CampaignPhase.Observation));
            Assert.That(world.Survival.Facilities.Count(f => f.Complete), Is.EqualTo(5));
            Assert.That(world.Entities.Npcs.Sum(n => n.Survival.WaterProduced), Is.GreaterThan(1000));
            Assert.That(world.Entities.Npcs.Sum(n => n.HarvestedFood), Is.GreaterThan(0));
        }
    }
}
