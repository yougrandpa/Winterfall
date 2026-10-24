using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    // Explicit wire format. Never serialize engine objects, backing fields or CLR type names.
    public static partial class WorldSnapshot
    {
        public const int CurrentVersion = 5;

        public static byte[] Capture(WorldState world, int version = CurrentVersion)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                Require(version == CurrentVersion);
                w.Write(version);
                WorldConfigData c = world.Config;
                w.Write(c.Seed); w.Write(c.Width); w.Write(c.Height); w.Write(c.NpcCount);
                w.Write(c.StartTick); w.Write(c.ChallengeDays); w.Write(c.TicksPerSecondAt1x);
                w.Write(c.SpeedMultipliers.Length);
                foreach (int speed in c.SpeedMultipliers) { w.Write(speed); }
                w.Write(world.Tick); w.Write(world.NextEntityId); w.Write(world.Rng.State);
                foreach (TileType tile in world.Map.CopyTiles()) { w.Write((byte)tile); }
                WriteInventory(w, world.Resources.Stockpile);
                w.Write(world.Resources.Nodes.Count);
                foreach (ResourceNodeState n in world.Resources.Nodes)
                {
                    w.Write(n.Id); WritePos(w, n.Position); w.Write((int)n.Item); w.Write(n.Capacity);
                    w.Write(n.RegenerationTicks); w.Write(n.Amount); w.Write(n.RegenerationProgress); w.Write(n.ReservedBy.Value);
                }
                w.Write(world.Resources.FarmPlots.Count);
                foreach (FarmPlotState p in world.Resources.FarmPlots)
                {
                    w.Write(p.Id); WritePos(w, p.Position); w.Write((int)p.Crop); w.Write(p.GrowthTicks);
                    w.Write(p.Moisture); w.Write(p.Fertility); w.Write(p.ReservedBy.Value);
                }
                w.Write(world.Resources.Reservations.Count);
                foreach (ResourceReservation r in world.Resources.Reservations)
                {
                    w.Write(r.Owner.Value); w.Write((int)r.Kind); w.Write(r.ResourceId); w.Write((int)r.Item);
                    w.Write(r.Quantity); WritePos(w, r.Position);
                }
                world.Ecology.WriteSnapshot(w);
                world.Market.WriteSnapshot(w);
                world.Materials.WriteSnapshot(w);
                w.Write(world.Entities.Npcs.Count);
                foreach (NpcState n in world.Entities.Npcs) { WriteNpc(w, n); }
                WriteSocial(w, world);
                SurvivalSnapshot.Write(w, world);
                WriteEnvironment(w, world);
                world.Campaign.Write(w);
                CombatSnapshot.Write(w, world);
                HuntingSnapshot.Write(w, world);
                return stream.ToArray();
            }
        }

        public static WorldState Restore(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 64 * 1024 * 1024) { throw new InvalidDataException("Invalid snapshot size."); }
            using (var stream = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(stream))
            {
                int version = r.ReadInt32();
                if (version != CurrentVersion) { throw new InvalidDataException("Unsupported snapshot version: " + version); }
                int seed = r.ReadInt32(), width = r.ReadInt32(), height = r.ReadInt32(), npcs = r.ReadInt32();
                Require(width >= 16 && height >= 16 && (long)width * height <= 1048576 && npcs >= 0 && npcs <= 10000);
                long start = r.ReadInt64(); int challengeDays = r.ReadInt32(); int rate = r.ReadInt32();
                var speeds = new int[ReadCount(r, 32)];
                for (int i = 0; i < speeds.Length; i++) { speeds[i] = r.ReadInt32(); }
                Require(speeds.Length > 0);
                var config = new WorldConfigData(seed, width, height, npcs, start, rate, speeds, challengeDays);
                var world = new WorldState(config, new WorldMap(width, height), new DeterministicRng(1));
                world.Tick = r.ReadInt64(); world.NextEntityId = r.ReadInt32(); world.Rng.State = r.ReadUInt64();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte tile = r.ReadByte(); Require(tile <= (byte)TileType.Farmland);
                        world.Map.SetTile(new GridPos(x, y), (TileType)tile);
                    }
                }
                ReadInventory(r, world.Resources.Stockpile);
                int count = ReadCount(r);
                var nodeOwners = new List<EntityId>();
                for (int i = 0; i < count; i++)
                {
                    int id = r.ReadInt32(); GridPos pos = ReadPos(r); ItemKind item = (ItemKind)r.ReadInt32();
                    int cap = r.ReadInt32(), regen = r.ReadInt32();
                    var n = new ResourceNodeState(id, pos, item, cap, regen);
                    n.Amount = r.ReadInt32(); n.RegenerationProgress = r.ReadInt32(); nodeOwners.Add(new EntityId(r.ReadInt32()));
                    Require(id > 0 && world.Resources.FindNode(id) == null && world.Map.IsWalkable(pos) && item >= ItemKind.Food && item < ItemKind.Count && n.Amount >= 0 && n.Amount <= cap && n.RegenerationProgress >= 0 && n.RegenerationProgress < regen);
                    world.Resources.AddNode(n);
                }
                count = ReadCount(r);
                var plotOwners = new List<EntityId>();
                for (int i = 0; i < count; i++)
                {
                    var p = new FarmPlotState(r.ReadInt32(), ReadPos(r));
                    p.Crop = (CropType)r.ReadInt32(); p.GrowthTicks = r.ReadInt32(); p.Moisture = r.ReadInt32();
                    p.Fertility = r.ReadInt32(); plotOwners.Add(new EntityId(r.ReadInt32()));
                    Require(p.Id > 0 && world.Resources.FindPlot(p.Id) == null && world.Map.IsWalkable(p.Position) && p.Crop >= CropType.None && p.Crop <= CropType.Bean && p.GrowthTicks >= 0 && p.GrowthTicks <= p.GrowthRequired && p.Moisture >= 0 && p.Moisture <= 1800 && p.Fertility >= 0 && p.Fertility <= 10000);
                    world.Resources.AddPlot(p);
                }
                count = ReadCount(r);
                for (int i = 0; i < count; i++)
                {
                    var reservation = new ResourceReservation(new EntityId(r.ReadInt32()), (ReservationKind)r.ReadInt32(), r.ReadInt32(), (ItemKind)r.ReadInt32(), r.ReadInt32(), ReadPos(r));
                    Require(reservation.Kind >= ReservationKind.Node && reservation.Kind <= ReservationKind.WorkCell && reservation.Item >= ItemKind.Food && reservation.Item < ItemKind.Count && world.Resources.TryReserve(reservation));
                }
                for (int i = 0; i < nodeOwners.Count; i++) { Require(world.Resources.Nodes[i].ReservedBy == nodeOwners[i]); }
                for (int i = 0; i < plotOwners.Count; i++) { Require(world.Resources.FarmPlots[i].ReservedBy == plotOwners[i]); }
                world.Ecology.ReadSnapshot(r);
                world.Market.ReadSnapshot(r);
                world.Materials.ReadSnapshot(r);
                count = ReadCount(r, 10000);
                for (int i = 0; i < count; i++) { world.Entities.AddNpc(ReadNpc(r)); }
                ReadSocial(r, world);
                SurvivalSnapshot.Read(r, world);
                ReadEnvironment(r, world);
                world.Campaign.Read(r, world);
                CombatSnapshot.Read(r, world);
                HuntingSnapshot.Read(r, world);
                Require(stream.Position == stream.Length);
                Validate(world);
                return world;
            }
        }

        internal static void WriteEnvironment(BinaryWriter writer, WorldState world)
        {
            EnvironmentState state = world.Environment;
            writer.Write(state.Temperature); writer.Write(state.Wind); writer.Write(state.Daylight); writer.Write(state.SnowDepth);
            writer.Write((int)state.Weather); writer.Write(state.NextWeatherTick); writer.Write(state.RngState);
        }

        private static void ReadEnvironment(BinaryReader reader, WorldState world)
        {
            EnvironmentState state = world.Environment;
            state.Temperature = reader.ReadInt32(); state.Wind = reader.ReadInt32(); state.Daylight = reader.ReadInt32();
            state.SnowDepth = reader.ReadInt32(); state.Weather = (WeatherKind)reader.ReadInt32();
            state.NextWeatherTick = reader.ReadInt64(); state.RngState = reader.ReadUInt64();
            Require(state.Temperature >= EnvironmentState.MinTemperature && state.Temperature <= EnvironmentState.MaxTemperature &&
                state.Wind >= 0 && state.Wind <= 100 && state.Daylight >= 0 && state.Daylight <= 10000 &&
                state.SnowDepth >= 0 && state.SnowDepth <= EnvironmentState.MaxSnowDepth &&
                Enum.IsDefined(typeof(WeatherKind), state.Weather) && state.NextWeatherTick >= 0 && state.RngState != 0);
        }

        internal static void WritePos(BinaryWriter w, GridPos p) { w.Write(p.X); w.Write(p.Y); }
        internal static GridPos ReadPos(BinaryReader r) => new GridPos(r.ReadInt32(), r.ReadInt32());
        internal static void Require(bool valid)
        {
            if (!valid) { throw new InvalidDataException("Snapshot violates world invariants."); }
        }
        internal static int ReadCount(BinaryReader r, int max = 1000000)
        {
            int count = r.ReadInt32(); Require(count >= 0 && count <= max); return count;
        }
        internal static void WriteInventory(BinaryWriter w, InventoryState inventory)
        {
            w.Write(inventory.Capacity);
            for (int i = 0; i < (int)ItemKind.Count; i++) { w.Write(inventory.Count((ItemKind)i)); }
        }
        internal static void ReadInventory(BinaryReader r, InventoryState inventory)
        {
            Require(r.ReadInt32() == inventory.Capacity);
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                int amount = r.ReadInt32(); Require(amount >= 0);
                Require(amount == 0 || inventory.TryAdd((ItemKind)i, amount));
            }
        }
        private static void WriteNpc(BinaryWriter w, NpcState n)
        {
            w.Write(n.Id.Value); w.Write(n.Name); w.Write(n.Role); w.Write(n.Identity.Age); WritePos(w, n.Home);
            WritePos(w, n.Position); w.Write(n.RngState);
            w.Write(n.Personality.Diligence); w.Write(n.Personality.Caution); w.Write(n.Personality.Sociability); w.Write(n.Personality.Curiosity);
            w.Write(n.Needs.Hunger); w.Write(n.Needs.Thirst); w.Write(n.Needs.Fatigue); w.Write(n.Needs.Health); w.Write(n.Needs.Safety);
            WriteInventory(w, n.Inventory);
            w.Write(n.Coins); w.Write(n.HasHuntingTool); w.Write(n.HuntingSkill); w.Write(n.HuntKills);
            w.Write(n.HarvestedFood); w.Write(n.GatheredFood); w.Write(n.Purchases); w.Write(n.Sales);
            w.Write((int)n.Action.Type); w.Write(n.Action.Label); WritePos(w, n.Action.Target); w.Write(n.Action.EndTick);
            w.Write(n.Action.PathIndex); w.Write(n.Action.Path.Count);
            foreach (GridPos pos in n.Action.Path) { WritePos(w, pos); }
            w.Write((int)n.Plan.Goal); w.Write(n.Plan.StepIndex); w.Write(n.Plan.StepStarted); w.Write(n.Plan.FailedStepIndex);
            w.Write(n.Plan.FailureReason); w.Write(n.Plan.ReplanCount); w.Write(n.Plan.Steps.Count);
            foreach (PlanStep s in n.Plan.Steps)
            {
                w.Write((int)s.Type); WritePos(w, s.Target); w.Write(s.ResourceId); w.Write((int)s.Item);
                w.Write(s.Quantity); w.Write(s.DurationTicks); w.Write(s.UnitPrice);
            }
            w.Write(n.LastDecision.Goal); w.Write(n.LastDecision.ReasonCode); w.Write(n.LastDecision.ChosenScore);
            w.Write(n.LastDecision.Options.Count);
            foreach (NpcDecisionOption o in n.LastDecision.Options) { w.Write(o.Id); w.Write(o.Score); }
        }
        private static NpcState ReadNpc(BinaryReader r)
        {
            var id = new EntityId(r.ReadInt32());
            var identity = new NpcIdentity(r.ReadString(), r.ReadString(), r.ReadInt32());
            GridPos home = ReadPos(r), pos = ReadPos(r); ulong rng = r.ReadUInt64();
            var personality = new Personality { Diligence = r.ReadInt32(), Caution = r.ReadInt32(), Sociability = r.ReadInt32(), Curiosity = r.ReadInt32() };
            var n = new NpcState(id, identity, personality, home, rng) { Position = pos };
            n.Needs.Hunger = r.ReadInt32(); n.Needs.Thirst = r.ReadInt32(); n.Needs.Fatigue = r.ReadInt32(); n.Needs.Health = r.ReadInt32(); n.Needs.Safety = r.ReadInt32();
            ReadInventory(r, n.Inventory);
            n.Coins = r.ReadInt64(); n.HasHuntingTool = r.ReadBoolean(); n.HuntingSkill = r.ReadInt32(); n.HuntKills = r.ReadInt32();
            n.HarvestedFood = r.ReadInt32(); n.GatheredFood = r.ReadInt32(); n.Purchases = r.ReadInt32(); n.Sales = r.ReadInt32();
            n.Action.Type = (NpcActionType)r.ReadInt32(); n.Action.Label = r.ReadString(); n.Action.Target = ReadPos(r); n.Action.EndTick = r.ReadInt64();
            n.Action.PathIndex = r.ReadInt32(); int count = ReadCount(r);
            for (int i = 0; i < count; i++) { n.Action.Path.Add(ReadPos(r)); }
            var goal = (NpcGoalType)r.ReadInt32(); int index = r.ReadInt32(); bool started = r.ReadBoolean(); int failed = r.ReadInt32();
            string reason = r.ReadString(); int replans = r.ReadInt32(); count = ReadCount(r, 1000);
            var steps = new List<PlanStep>();
            for (int i = 0; i < count; i++)
            {
                steps.Add(new PlanStep((PlanStepType)r.ReadInt32(), ReadPos(r), r.ReadInt32(), (ItemKind)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            }
            n.Plan.Start(goal, steps); n.Plan.StepIndex = index; n.Plan.StepStarted = started; n.Plan.FailedStepIndex = failed;
            n.Plan.FailureReason = reason; n.Plan.ReplanCount = replans;
            n.LastDecision = new NpcDecisionTrace { Goal = r.ReadString(), ReasonCode = r.ReadString(), ChosenScore = r.ReadInt32() };
            count = ReadCount(r, 1000);
            for (int i = 0; i < count; i++) { n.LastDecision.Options.Add(new NpcDecisionOption(r.ReadString(), r.ReadInt32())); }
            return n;
        }
        private static void Validate(WorldState world)
        {
            Require(world.Tick >= world.Config.StartTick && world.Rng.State != 0);
            var ids = new HashSet<int>(); int largest = 0;
            foreach (NpcState n in world.Entities.Npcs)
            {
                Require(n.Id.Value > 0 && ids.Add(n.Id.Value) && world.Map.IsWalkable(n.Position) && world.Map.IsWalkable(n.Home) && n.RngState != 0 && n.Coins >= 0);
                largest = Math.Max(largest, n.Id.Value);
                foreach (int v in new[] { n.Needs.Food, n.Needs.Water, n.Needs.Energy, n.Needs.Health, n.Needs.Safety }) { Require(v >= 0 && v <= 10000); }
                Require(n.Action.PathIndex >= 0 && n.Action.PathIndex <= n.Action.Path.Count && n.Plan.StepIndex >= 0 && n.Plan.StepIndex <= n.Plan.Steps.Count);
                Require(Enum.IsDefined(typeof(NpcActionType), n.Action.Type) && Enum.IsDefined(typeof(NpcGoalType), n.Plan.Goal));
                foreach (PlanStep step in n.Plan.Steps) { Require(Enum.IsDefined(typeof(PlanStepType), step.Type)); }
            }
            foreach (AnimalState a in world.Ecology.Animals)
            {
                Require(a.Id.Value > 0 && ids.Add(a.Id.Value) && a.RngState != 0);
                largest = Math.Max(largest, a.Id.Value);
                NpcState owner;
                Require(a.ReservedBy.IsNone || world.Entities.TryGetNpc(a.ReservedBy, out owner));
            }
            foreach (ResourceReservation reservation in world.Resources.Reservations)
            {
                NpcState owner; Require(world.Entities.TryGetNpc(reservation.Owner, out owner));
            }
            world.Ecology.Validate(world);
            world.Campaign.Validate(world);
            CombatSnapshot.Validate(world);
            HuntingSnapshot.Validate(world);
            Require(world.NextEntityId > largest && world.Market.CurrencyError(world) == 0);
            for (int i = 0; i < (int)ItemKind.Count; i++) { Require(world.Materials.Error(world, (ItemKind)i) == 0); }
        }
    }
}

namespace MyAiWorld.Sim.World
{
    public sealed partial class MarketState
    {
        internal void WriteSnapshot(BinaryWriter w)
        {
            WorldSnapshot.WriteInventory(w, Inventory); w.Write(Treasury); w.Write(IssuedCurrency); w.Write(NextOrderId);
            w.Write(LedgerHash); w.Write(FilledCount); w.Write(RejectedCount); w.Write(Volume);
            for (int i = 0; i < SurvivalRules.MarketItemCount; i++) { w.Write(_prices[i]); w.Write(_bought[i]); w.Write(_sold[i]); }
            w.Write(_ledger.Count);
            foreach (TradeRecord t in _ledger)
            {
                TradeOrder o = t.Order; w.Write(o.Id); w.Write(o.NpcId.Value); w.Write((int)o.Side); w.Write((int)o.Item); w.Write(o.Quantity); w.Write(o.UnitPrice);
                w.Write(t.Tick); w.Write(t.Reason); w.Write(t.WalletBefore); w.Write(t.TreasuryBefore); w.Write(t.BagBefore); w.Write(t.MarketBefore);
            }
        }
        internal void ReadSnapshot(BinaryReader r)
        {
            WorldSnapshot.ReadInventory(r, Inventory); Treasury = r.ReadInt64(); IssuedCurrency = r.ReadInt64(); NextOrderId = r.ReadInt64();
            LedgerHash = r.ReadUInt64(); FilledCount = r.ReadInt32(); RejectedCount = r.ReadInt32(); Volume = r.ReadInt64();
            WorldSnapshot.Require(Treasury >= 0 && IssuedCurrency >= 0 && FilledCount >= 0 && RejectedCount >= 0 && Volume >= 0);
            for (int i = 0; i < SurvivalRules.MarketItemCount; i++)
            {
                _prices[i] = r.ReadInt32(); _bought[i] = r.ReadInt64(); _sold[i] = r.ReadInt64();
                WorldSnapshot.Require(_prices[i] >= 1 && _prices[i] <= 12 && _bought[i] >= 0 && _sold[i] >= 0);
            }
            int count = WorldSnapshot.ReadCount(r);
            WorldSnapshot.Require(count == FilledCount + RejectedCount && NextOrderId == count + 1L);
            for (int i = 0; i < count; i++)
            {
                var order = new TradeOrder(r.ReadInt64(), new EntityId(r.ReadInt32()), (TradeSide)r.ReadInt32(), (ItemKind)r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                _ledger.Add(new TradeRecord(order, r.ReadInt64(), r.ReadString(), r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32()));
                WorldSnapshot.Require(order.Id == i + 1L);
            }
        }
    }
    public sealed partial class MaterialBalance
    {
        internal void WriteSnapshot(BinaryWriter w)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++) { w.Write(_initial[i]); w.Write(_produced[i]); w.Write(_consumed[i]); }
        }
        internal void ReadSnapshot(BinaryReader r)
        {
            for (int i = 0; i < (int)ItemKind.Count; i++)
            {
                _initial[i] = r.ReadInt64(); _produced[i] = r.ReadInt64(); _consumed[i] = r.ReadInt64();
                WorldSnapshot.Require(_initial[i] >= 0 && _produced[i] >= 0 && _consumed[i] >= 0);
            }
        }
    }
}
