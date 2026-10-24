using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Navigation;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class EcologyEconomyTests
    {
        private static WorldState Create(int seed = 42, int npcCount = 20)
        {
            return WorldFactory.Create(new WorldConfigData(seed, 64, 64, npcCount,
                GameCalendar.StartTick, 2, new[] { 1, 2, 4, 8 }));
        }

        private static NpcState AddAtMarket(WorldState world)
        {
            return WorldFactory.SpawnNpc(world, world.Center, true, new List<SimEvent>());
        }

        private static bool Trade(WorldState world, NpcState npc, TradeSide side, ItemKind item, int amount, int price = 0)
        {
            return world.Market.Execute(world, new TradeOrder(world.Market.NextOrderId, npc.Id, side,
                item, amount, price == 0 ? world.Market.Price(item) : price), new List<SimEvent>());
        }

        [Test]
        public void Trade_BuySellAndDuplicate_ReconcileBothLegsAndLedger()
        {
            WorldState world = Create(npcCount: 0);
            NpcState npc = AddAtMarket(world);
            int price = world.Market.Price(ItemKind.Food);
            var order = new TradeOrder(world.Market.NextOrderId, npc.Id, TradeSide.Buy, ItemKind.Food, 2, price);
            var engine = new SimulationEngine();
            long coins = npc.Coins;
            Assert.That(world.Market.Ledger.Count, Is.Zero);
            TickResult result = engine.Tick(world, new IWorldCommand[] { new TradeCommand(order) });
            Assert.That(result.AppliedCommands, Is.EqualTo(1));
            Assert.That(npc.Inventory.Food, Is.EqualTo(2));
            Assert.That(npc.Coins, Is.EqualTo(coins - 2 * price));
            Assert.That(world.Market.Treasury, Is.EqualTo(2000 + 2 * price));
            Assert.That(world.Market.Ledger[0].Filled, Is.True);
            Assert.That(world.Market.Execute(world, order, result.Events), Is.False);
            Assert.That(world.Market.Ledger.Count, Is.EqualTo(1), "Duplicate IDs cannot append or settle twice.");
            Assert.That(Trade(world, npc, TradeSide.Sell, ItemKind.Food, 2), Is.True);
            Assert.That(npc.Coins, Is.EqualTo(coins));
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(world.Market.CurrencyError(world), Is.Zero);
            Assert.That(world.Materials.Error(world, ItemKind.Food), Is.Zero);
            ReconcileLedger(world);
        }

        [TestCase("invalid_quantity")]
        [TestCase("invalid_item")]
        [TestCase("invalid_side")]
        [TestCase("price_changed")]
        [TestCase("insufficient_coins")]
        [TestCase("bag_full")]
        [TestCase("market_stock_short")]
        [TestCase("seller_stock_short")]
        [TestCase("treasury_short")]
        [TestCase("market_full")]
        [TestCase("not_at_market")]
        [TestCase("large_quantity")]
        [TestCase("currency_overflow")]
        public void Trade_RejectedOrder_HasNoPartialGoodsOrCurrencyMutation(string scenario)
        {
            WorldState world = Create(npcCount: 0);
            NpcState npc = AddAtMarket(world);
            TradeSide side = TradeSide.Buy;
            ItemKind item = ItemKind.Food;
            int quantity = 1;
            int price = world.Market.Price(item);
            switch (scenario)
            {
                case "invalid_quantity": quantity = -1; break;
                case "invalid_item": item = (ItemKind)99; break;
                case "invalid_side": side = (TradeSide)99; break;
                case "price_changed": price++; break;
                case "insufficient_coins": npc.Coins = 0; break;
                case "bag_full": npc.Inventory.TryAdd(ItemKind.Seed, npc.Inventory.Capacity); break;
                case "market_stock_short": world.Market.Inventory.TryRemove(item, 30); break;
                case "seller_stock_short": side = TradeSide.Sell; break;
                case "treasury_short": side = TradeSide.Sell; npc.Inventory.TryAdd(item, 1); world.Market.Treasury = 0; break;
                case "market_full":
                    side = TradeSide.Sell; npc.Inventory.TryAdd(item, 1);
                    world.Market.Inventory.TryAdd(ItemKind.Seed, world.Market.Inventory.Capacity - world.Market.Inventory.TotalCount);
                    break;
                case "not_at_market": npc.Position = npc.Position.Offset(1, 0); break;
                case "large_quantity": quantity = int.MaxValue; price = int.MaxValue; break;
                case "currency_overflow": world.Market.Treasury = long.MaxValue; break;
            }
            string bag = npc.Inventory.Summary;
            string market = world.Market.Inventory.Summary;
            long wallet = npc.Coins;
            long treasury = world.Market.Treasury;
            Assert.That(Trade(world, npc, side, item, quantity, price), Is.False);
            Assert.That(npc.Inventory.Summary, Is.EqualTo(bag));
            Assert.That(world.Market.Inventory.Summary, Is.EqualTo(market));
            Assert.That(npc.Coins, Is.EqualTo(wallet));
            Assert.That(world.Market.Treasury, Is.EqualTo(treasury));
            TradeRecord record = world.Market.Ledger[0];
            Assert.That(record.Reason, Is.Not.EqualTo("filled"));
            Assert.That(record.Value, Is.Zero);
            Assert.That(record.WalletAfter, Is.EqualTo(record.WalletBefore));
            Assert.That(record.MarketAfter, Is.EqualTo(record.MarketBefore));
        }

        [Test]
        public void Trade_ContendingBuyers_CannotDoubleSpendLastUnit()
        {
            WorldState world = Create(npcCount: 0);
            NpcState first = AddAtMarket(world);
            NpcState second = AddAtMarket(world);
            world.Market.Inventory.TryRemove(ItemKind.Food, 29);
            int price = world.Market.Price(ItemKind.Food);
            long id = world.Market.NextOrderId;
            TickResult result = new SimulationEngine().Tick(world, new IWorldCommand[]
            {
                new TradeCommand(new TradeOrder(id, first.Id, TradeSide.Buy, ItemKind.Food, 1, price)),
                new TradeCommand(new TradeOrder(id + 1, second.Id, TradeSide.Buy, ItemKind.Food, 1, price))
            });
            Assert.That(result.AppliedCommands, Is.EqualTo(1));
            Assert.That(first.Inventory.Food, Is.EqualTo(1));
            Assert.That(second.Inventory.Food, Is.Zero);
            Assert.That(second.Coins, Is.EqualTo(100));
            Assert.That(world.Market.Ledger[1].Reason, Is.EqualTo("market_stock_short"));
        }

        [Test]
        public void Market_PricesRespondToScarcity_AndRemainBounded()
        {
            WorldState world = Create(npcCount: 0);
            world.Market.UpdatePrices();
            int initial = world.Market.Price(ItemKind.Food);
            world.Market.Inventory.TryRemove(ItemKind.Food, 30);
            world.Market.UpdatePrices();
            Assert.That(world.Market.Price(ItemKind.Food), Is.GreaterThan(initial));
            world.Market.Inventory.TryAdd(ItemKind.Food, 500);
            world.Market.UpdatePrices();
            Assert.That(world.Market.Price(ItemKind.Food), Is.LessThan(initial));
            Assert.That(world.Market.Price(ItemKind.Food), Is.InRange(1, 12));
        }

        [Test]
        public void Hunt_KillProducesOneDrop_AndReleasesReservation()
        {
            WorldState world = Create(npcCount: 0);
            NpcState npc = AddAtMarket(world);
            npc.HasHuntingTool = true;
            npc.Inventory.TryAdd(ItemKind.Arrow, 1);
            AnimalState prey = null;
            foreach (AnimalState candidate in world.Ecology.Animals)
            {
                if (candidate.Species == AnimalSpecies.Boar) { prey = candidate; break; }
            }
            prey.AgeTicks = Math.Max(prey.AgeTicks, prey.Definition.MaturityTicks);
            prey.BodyMass = prey.Definition.AdultMass;
            prey.Position = npc.Position;
            prey.Health = 1;
            prey.ReservedBy = npc.Id;
            // Keep the hunt itself under test; without warmth the thermal priority
            // legitimately interrupts any work plan at this cold spring hour.
            world.Environment.Temperature = 2500;
            world.Environment.Wind = 0;
            npc.Plan.Start(NpcGoalType.Hunt, new List<PlanStep> { new PlanStep(PlanStepType.Hunt, prey.Position, prey.Id.Value, durationTicks: 120) });
            world.Materials.Initialize(world);
            var engine = new SimulationEngine();
            int skill = npc.HuntingSkill;
            for (int tick = 0; tick < 24 && npc.Plan.IsActive; tick++)
            {
                engine.Tick(world, null);
            }
            Assert.That(npc.HuntKills, Is.EqualTo(1));
            Assert.That(npc.Inventory.Count(ItemKind.RawMeat), Is.GreaterThan(0), "A kill yields a carcass, not ready food.");
            Assert.That(npc.Inventory.Count(ItemKind.Hide), Is.GreaterThan(0));
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(prey.ReservedBy.IsNone, Is.True);
            Assert.That(npc.HuntingSkill, Is.GreaterThan(skill));
            Assert.That(npc.Needs.Energy, Is.LessThan(8400));
            foreach (ItemKind item in new[] { ItemKind.Food, ItemKind.RawMeat, ItemKind.Hide, ItemKind.Arrow })
            {
                Assert.That(world.Materials.Error(world, item), Is.Zero, item.ToString());
            }
            Assert.That(world.Ecology.Strike(world, npc, prey, new List<SimEvent>()), Is.False);
            Assert.That(npc.HuntKills, Is.EqualTo(1));
        }

        [TestCase("tool")]
        [TestCase("energy")]
        [TestCase("critical_need")]
        public void Hunt_InvalidatedPlan_ReleasesPreyWithoutDrop(string scenario)
        {
            WorldState world = Create(npcCount: 0);
            NpcState npc = AddAtMarket(world);
            npc.HasHuntingTool = true;
            npc.Inventory.TryAdd(ItemKind.Arrow, 4);
            AnimalState prey = world.Ecology.Animals[0];
            prey.Position = npc.Position;
            prey.ReservedBy = npc.Id;
            world.Environment.Temperature = 2500;
            world.Environment.Wind = 0;
            npc.Plan.Start(NpcGoalType.Hunt, new List<PlanStep> { new PlanStep(PlanStepType.Hunt, prey.Position, prey.Id.Value) });
            switch (scenario)
            {
                case "tool": npc.HasHuntingTool = false; break;
                case "capacity": npc.Inventory.TryAdd(ItemKind.Seed, npc.Inventory.Capacity); break;
                case "energy": npc.Needs.Fatigue = 1500; break;
                case "critical_need": npc.Needs.Thirst = 100; break;
            }
            TickResult result = new SimulationEngine().Tick(world, null);
            Assert.That(prey.ReservedBy.IsNone, Is.True);
            Assert.That(npc.Inventory.Food, Is.Zero);
            Assert.That(npc.HuntKills, Is.Zero);
            if (scenario == "critical_need")
            {
                Assert.That(result.Events.Exists(evt => evt.Kind == SimEventKind.PlanInterrupted), Is.True);
                Assert.That(npc.Plan.Goal, Is.EqualTo(NpcGoalType.Drink));
            }
            else
            {
                Assert.That(npc.Plan.IsFailed, Is.True);
            }
        }

        [Test]
        public void Hunt_TwoPlanners_ReserveDifferentPrey()
        {
            WorldState world = Create(npcCount: 0);
            NpcState first = AddAtMarket(world);
            NpcState second = AddAtMarket(world);
            first.HasHuntingTool = second.HasHuntingTool = true;
            first.Inventory.TryAdd(ItemKind.Arrow, 4);
            second.Inventory.TryAdd(ItemKind.Arrow, 4);
            // The planner only considers perceived prey; place one reachable animal
            // beside each hunter so this test isolates reservation exclusivity.
            AnimalState firstPrey = world.Ecology.Animals[0];
            AnimalState secondPrey = world.Ecology.Animals[1];
            firstPrey.Position = first.Position.Offset(1, 0);
            secondPrey.Position = second.Position.Offset(-1, 0);
            firstPrey.HabitatId = secondPrey.HabitatId = world.Ecology.HabitatAt(world.Center).Id;
            var planner = new SurvivalPlanner(new GridPathfinder());
            string failure;
            foreach (NpcState npc in world.Entities.Npcs)
            {
                Assert.That(planner.TryBuild(world, npc, new NpcDecision(NpcGoalType.Hunt,
                    npc.Position, 120, npc.RngState, NpcDecisionTrace.Empty), out failure), Is.True, failure);
            }
            Assert.That(first.Plan.Steps[0].ResourceId, Is.Not.EqualTo(second.Plan.Steps[0].ResourceId));
        }

        [Test]
        public void Animals_FleeBreedAndAge_StayWalkableAndBounded()
        {
            WorldState world = Create(npcCount: 0);
            NpcState hunter = AddAtMarket(world);
            hunter.HasHuntingTool = true;
            AnimalState prey = world.Ecology.Animals[0];
            prey.Position = world.Center;
            world.Tick = prey.NextDecisionTick;
            prey.NextMoveTick = world.Tick;
            world.Ecology.Update(world, new List<SimEvent>());
            Assert.That(prey.Fleeing, Is.True);
            Assert.That(prey.Position.ManhattanDistance(hunter.Position), Is.GreaterThan(0));
            prey.AgeTicks = prey.MaxAgeTicks - 1;
            world.Tick = prey.LastNeedsTick + 60;
            world.Ecology.Update(world, new List<SimEvent>());
            Assert.That(prey.Health, Is.Zero);
            Assert.That(world.Ecology.NaturalDeaths, Is.EqualTo(1));
            for (int tick = 0; tick < 7200; tick++)
            {
                world.Ecology.Update(world, new List<SimEvent>());
                world.Tick++;
                Assert.That(world.Ecology.Count(AnimalSpecies.Rabbit), Is.InRange(0, EcologyState.SpeciesCapacity));
                Assert.That(world.Ecology.Count(AnimalSpecies.Boar), Is.InRange(0, EcologyState.SpeciesCapacity));
            }
            Assert.That(world.Ecology.Births, Is.GreaterThan(0));
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Region >= 0) { continue; }
                Assert.That(world.Map.IsWalkable(animal.Position), Is.True);
            }
        }

        [TestCase("animal_position")]
        [TestCase("animal_health")]
        [TestCase("animal_age")]
        [TestCase("animal_rng")]
        [TestCase("animal_reservation")]
        [TestCase("animal_flee")]
        [TestCase("birth_progress")]
        [TestCase("ecology_rng")]
        [TestCase("wallet")]
        [TestCase("skill")]
        [TestCase("tool")]
        [TestCase("production")]
        [TestCase("treasury")]
        [TestCase("issued")]
        [TestCase("market_stock")]
        [TestCase("price")]
        [TestCase("flow")]
        [TestCase("ledger")]
        public void Hash_IncludesEcologyEconomyAndAuditState(string change)
        {
            WorldState world = Create();
            NpcState npc = world.Entities.Npcs[0];
            AnimalState animal = world.Ecology.Animals[0];
            ulong before = StateHasher.Compute(world);
            switch (change)
            {
                case "animal_position": animal.Position = animal.Position.Offset(1, 0); break;
                case "animal_health": animal.Health--; break;
                case "animal_age": animal.AgeTicks++; break;
                case "animal_rng": animal.RngState++; break;
                case "animal_reservation": animal.ReservedBy = npc.Id; break;
                case "animal_flee": animal.Fleeing = !animal.Fleeing; break;
                case "birth_progress": world.Ecology.BirthProgress++; break;
                case "ecology_rng": world.Ecology.Rng.NextUInt(); break;
                case "wallet": npc.Coins++; break;
                case "skill": npc.HuntingSkill++; break;
                case "tool": npc.HasHuntingTool = !npc.HasHuntingTool; break;
                case "production": npc.HarvestedFood++; break;
                case "treasury": world.Market.Treasury++; break;
                case "issued": world.Market.IssuedCurrency++; break;
                case "market_stock": world.Market.Inventory.TryRemove(ItemKind.Food, 1); break;
                case "price": world.Market.Inventory.TryRemove(ItemKind.Food, 30); world.Market.UpdatePrices(); world.Market.Inventory.TryAdd(ItemKind.Food, 30); break;
                case "flow": world.Materials.Produce(ItemKind.Food, 1); break;
                case "ledger": Trade(world, npc, TradeSide.Buy, ItemKind.Food, 1, 99); break;
            }
            Assert.That(StateHasher.Compute(world), Is.Not.EqualTo(before), change);
        }

        [Test]
        public void Hash_TradePlanQuote_AffectsReplayHash()
        {
            WorldState world = Create();
            NpcState npc = world.Entities.Npcs[0];
            npc.Plan.Start(NpcGoalType.Eat, new List<PlanStep>
            {
                new PlanStep(PlanStepType.Buy, world.Center, unitPrice: 5)
            });
            ulong before = StateHasher.Compute(world);
            npc.Plan.Start(NpcGoalType.Eat, new List<PlanStep>
            {
                new PlanStep(PlanStepType.Buy, world.Center, unitPrice: 6)
            });
            Assert.That(StateHasher.Compute(world), Is.Not.EqualTo(before));
        }

        [Test]
        public void Trade_StalePlanQuote_RecordsReasonAndPreservesWallet()
        {
            WorldState world = Create(npcCount: 0);
            NpcState npc = AddAtMarket(world);
            npc.Plan.Start(NpcGoalType.Eat, new List<PlanStep>
            {
                new PlanStep(PlanStepType.Buy, world.Center, unitPrice: world.Market.Price(ItemKind.Food) + 1)
            });
            var engine = new SimulationEngine();
            engine.Tick(world, null);
            engine.Tick(world, null);
            Assert.That(npc.Plan.FailureReason, Is.EqualTo("price_changed"));
            Assert.That(npc.LastDecision.ReasonCode, Is.EqualTo("price_changed"));
            Assert.That(world.Market.Ledger[0].Reason, Is.EqualTo("price_changed"));
            Assert.That(npc.Coins, Is.EqualTo(100));
            Assert.That(npc.Inventory.Food, Is.Zero);
        }

        [Test]
        public void Replay_SameSeedAndSpawnTradeCommands_MatchEveryTick()
        {
            WorldState first = Create();
            WorldState second = Create();
            var engine = new SimulationEngine();
            for (int tick = 0; tick < 1800; tick++)
            {
                var commands = new List<IWorldCommand>();
                if (tick == 0) { commands.Add(new SpawnNpcCommand()); }
                if (tick == 100 || tick == 600)
                {
                    commands.Add(new TradeCommand(new TradeOrder(first.Market.NextOrderId,
                        first.Entities.Npcs[0].Id, TradeSide.Buy, ItemKind.Food, 1, first.Market.Price(ItemKind.Food))));
                }
                Assert.That(engine.Tick(first, commands).StateHash, Is.EqualTo(engine.Tick(second, commands).StateHash), "Tick " + tick);
            }
        }

        // Survival v5 includes production, equipment, crops, rescue and ground inventories.
        // All earlier conservation and specialization
        // assertions remain active before accepting these new baselines.
        [TestCase(20261020, "ec8b3c600e0fe272")]
        [TestCase(42, "76dfcfb3cc7119a4")]
        [TestCase(1, "e9b36ab66ecc80eb")]
        [TestCase(12345, "19997160b1426731")]
        public void DefaultWorld_FourteenDays_ConservesResourcesAndCurrencyWithDistinctStrategies(int seed, string expectedHash)
        {
            WorldState world = Create(seed);
            var engine = new SimulationEngine();
            int minHealth = 10000;
            int minPopulation = 48;
            int maxPopulation = 0;
            int previousLedgerCount = 0;
            var daily = new List<string>();
            var eaten = new HashSet<EntityId>();
            var drunk = new HashSet<EntityId>();
            var slept = new HashSet<EntityId>();
            for (int tick = 0; tick < 14 * GameCalendar.TicksPerDay; tick++)
            {
                TickResult result = engine.Tick(world, null);
                foreach (SimEvent evt in result.Events)
                {
                    if (evt.Kind == SimEventKind.NpcActionCompleted)
                    {
                        if (evt.Message.EndsWith("completed Eat.")) { eaten.Add(evt.Source); }
                        if (evt.Message.EndsWith("completed Drink.")) { drunk.Add(evt.Source); }
                        // WarmUp uses the sleep action at the shelter and is the winter
                        // equivalent of the Sleep plan step.
                        if (evt.Message.EndsWith("completed Sleep.") || evt.Message.EndsWith("completed WarmUp.")) { slept.Add(evt.Source); }
                    }
                }
                Assert.That(world.Market.CurrencyError(world), Is.Zero, "Currency tick " + tick);
                for (int item = 0; item < (int)ItemKind.Count; item++)
                {
                    Assert.That(world.Materials.Error(world, (ItemKind)item), Is.Zero, "Material " + item + " tick " + tick);
                    if (item < SurvivalRules.MarketItemCount) { Assert.That(world.Market.Price((ItemKind)item), Is.InRange(1, 12)); }
                    else { Assert.That(world.Market.Price((ItemKind)item), Is.Zero); }
                }
                int rabbits = world.Ecology.Count(AnimalSpecies.Rabbit);
                int boars = world.Ecology.Count(AnimalSpecies.Boar);
                Assert.That(rabbits, Is.InRange(0, EcologyState.SpeciesCapacity));
                Assert.That(boars, Is.InRange(0, EcologyState.SpeciesCapacity));
                minPopulation = Math.Min(minPopulation, rabbits + boars);
                maxPopulation = Math.Max(maxPopulation, rabbits + boars);
                AssertInventory(world.Resources.Stockpile);
                AssertInventory(world.Market.Inventory);
                Assert.That(world.Market.Treasury, Is.GreaterThanOrEqualTo(0));
                foreach (NpcState npc in world.Entities.Npcs)
                {
                    minHealth = Math.Min(minHealth, npc.Needs.Health);
                    Assert.That(npc.Coins, Is.GreaterThanOrEqualTo(0));
                    // Winter-tuned survival now permits deprivation, downing and death;
                    // the invariant is no negative state, not guaranteed comfort.
                    Assert.That(npc.Needs.Health, Is.GreaterThanOrEqualTo(0));
                    Assert.That(npc.Needs.Food, Is.GreaterThanOrEqualTo(0));
                    Assert.That(npc.Needs.Water, Is.GreaterThanOrEqualTo(0));
                    Assert.That(npc.Needs.Energy, Is.GreaterThanOrEqualTo(0));
                    Assert.That(world.Map.IsWalkable(npc.Position), Is.True);
                    AssertInventory(npc.Inventory);
                }
                if ((tick + 1) % GameCalendar.TicksPerDay == 0)
                {
                    int buys = 0, sales = 0;
                    for (int trade = previousLedgerCount; trade < world.Market.Ledger.Count; trade++)
                    {
                        TradeRecord record = world.Market.Ledger[trade];
                        if (record.Filled && record.Order.Side == TradeSide.Buy) { buys++; }
                        if (record.Filled && record.Order.Side == TradeSide.Sell) { sales++; }
                    }
                    previousLedgerCount = world.Market.Ledger.Count;
                    daily.Add("{\"day\":" + (tick + 1) / 1440 + ",\"buys\":" + buys + ",\"sales\":" + sales +
                        ",\"rabbits\":" + rabbits + ",\"boars\":" + boars + ",\"treasury\":" + world.Market.Treasury + "}");
                }
                foreach (AnimalState animal in world.Ecology.Animals)
                {
                    if (animal.Region >= 0) { continue; }
                    Assert.That(world.Map.IsWalkable(animal.Position), Is.True);
                    if (!animal.ReservedBy.IsNone)
                    {
                        NpcState owner;
                        Assert.That(world.Entities.TryGetNpc(animal.ReservedBy, out owner), Is.True);
                        Assert.That(owner.Plan.IsActive, Is.True);
                        Assert.That(owner.Plan.CurrentStep.Type, Is.EqualTo(PlanStepType.Hunt));
                        Assert.That(owner.Plan.CurrentStep.ResourceId, Is.EqualTo(animal.Id.Value));
                    }
                }
            }
            int farmerHarvest = 0, gathererHarvest = 0, farmerGather = 0, gathererGather = 0, kills = 0;
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Role == "Farmer") { farmerHarvest += npc.HarvestedFood; farmerGather += npc.GatheredFood; }
                if (npc.Role == "Gatherer") { gathererHarvest += npc.HarvestedFood; gathererGather += npc.GatheredFood; }
                if (npc.Role == "Hunter") { kills += npc.HuntKills; }
            }
            Assert.That(farmerHarvest, Is.GreaterThan(gathererHarvest), "Farmers should specialize in crops.");
            Assert.That(gathererGather, Is.GreaterThan(farmerGather), "Gatherers should specialize in renewable food.");
            Assert.That(kills, Is.GreaterThan(0));
            int majority = Math.Max(1, world.Entities.Npcs.Count / 2);
            Assert.That(eaten.Count, Is.GreaterThanOrEqualTo(majority));
            Assert.That(drunk.Count, Is.GreaterThanOrEqualTo(majority));
            Assert.That(slept.Count, Is.GreaterThanOrEqualTo(majority));
            Assert.That(world.Ecology.Births, Is.GreaterThan(0));
            Assert.That(world.Market.FilledCount, Is.GreaterThan(100));
            Assert.That(world.Market.Bought(ItemKind.Food), Is.GreaterThan(0));
            Assert.That(world.Market.Sold(ItemKind.Food), Is.GreaterThan(0));
            Assert.That(world.Social.Greetings, Is.GreaterThan(0));
            Assert.That(world.Social.Gifts, Is.GreaterThan(0));
            Assert.That(world.Social.Timeline.Count, Is.LessThanOrEqualTo(SocialState.TimelineLimit));
            foreach (NpcState npc in world.Entities.Npcs) { Assert.That(npc.Social.Memories.Count, Is.LessThanOrEqualTo(NpcSocialState.MemoryLimit)); }
            ReconcileLedger(world);
            Assert.That(StateHasher.ToHex(StateHasher.Compute(world)), Is.EqualTo(expectedHash));
            string report = "{\"seed\":" + seed + ",\"ticks\":20160,\"hash\":\"" + StateHasher.ToHex(StateHasher.Compute(world)) +
                "\",\"greetings\":" + world.Social.Greetings + ",\"gifts\":" + world.Social.Gifts + ",\"hashVersion\":5,\"minHealth\":" + minHealth + ",\"minPopulation\":" + minPopulation + ",\"maxPopulation\":" + maxPopulation +
                ",\"births\":" + world.Ecology.Births + ",\"hunts\":" + kills + ",\"farmerHarvest\":" + farmerHarvest +
                ",\"gathererHarvest\":" + gathererHarvest + ",\"farmerGather\":" + farmerGather + ",\"gathererGather\":" + gathererGather +
                ",\"filled\":" + world.Market.FilledCount + ",\"rejected\":" + world.Market.RejectedCount +
                ",\"treasury\":" + world.Market.Treasury + ",\"currencyError\":0,\"materialErrors\":[0,0,0],\"days\":[" + string.Join(",", daily) + "]}";
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "TestResults"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Survival-FourteenDays-" + seed + ".json"), report);
            TestContext.WriteLine(report);
        }

        private static void AssertInventory(InventoryState inventory)
        {
            Assert.That(inventory.TotalCount, Is.InRange(0, inventory.Capacity));
            Assert.That(inventory.Food, Is.GreaterThanOrEqualTo(0));
            Assert.That(inventory.Water, Is.GreaterThanOrEqualTo(0));
            Assert.That(inventory.Seeds, Is.GreaterThanOrEqualTo(0));
        }

        private static void ReconcileLedger(WorldState world)
        {
            // Wallets can also change through death drops and ground pickups, which
            // are outside the market ledger. Verify every record's own conservation
            // equations and the global currency audit instead of replaying one
            // opening balance across external transfers.
            long sequence = 1;
            foreach (TradeRecord record in world.Market.Ledger)
            {
                Assert.That(record.Order.Id, Is.EqualTo(sequence++));
                Assert.That(record.WalletAfter + record.TreasuryAfter,
                    Is.EqualTo(record.WalletBefore + record.TreasuryBefore));
                Assert.That(record.BagAfter + record.MarketAfter, Is.EqualTo(record.BagBefore + record.MarketBefore));
                if (record.Filled)
                {
                    long money = record.Order.Side == TradeSide.Buy ? record.Value : -record.Value;
                    Assert.That(record.WalletAfter, Is.EqualTo(record.WalletBefore - money));
                    Assert.That(record.TreasuryAfter, Is.EqualTo(record.TreasuryBefore + money));
                    Assert.That(record.MarketAfter, Is.EqualTo(record.MarketBefore +
                        (record.Order.Side == TradeSide.Buy ? -record.Order.Quantity : record.Order.Quantity)));
                }
            }
            Assert.That(world.Market.CurrencyError(world), Is.Zero);
            AssertInventory(world.Market.Inventory);
        }
    }
}
