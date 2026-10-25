using System;
using System.IO;
using MyAiWorld.Sim.Ai;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    internal static class SurvivalSnapshot
    {
        internal static byte[] Capture(WorldState world)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream)) { Write(w, world); return stream.ToArray(); }
        }
        internal static void Write(BinaryWriter w, WorldState world)
        {
            var state = world.Survival;
            w.Write(state.NextId); w.Write(state.RefillWater); w.Write(state.CropsLost);
            w.Write(state.Facilities.Count);
            foreach (var f in state.Facilities)
            {
                w.Write(f.Id); w.Write((int)f.Kind); WorldSnapshot.WritePos(w, f.Position); WorldSnapshot.WritePos(w, f.WorkCell);
                w.Write(f.PendingRecipeId); w.Write(f.Progress); w.Write(f.Condition); w.Write(f.ReservedBy.Value); w.Write(f.WellNodeId); WorldSnapshot.WriteInventory(w, f.Inventory);
            }
            w.Write(state.Ground.Count);
            foreach (var g in state.Ground)
            {
                w.Write(g.Id); WorldSnapshot.WritePos(w, g.Position); w.Write(g.Coins); w.Write(g.ReservedBy.Value);
                WorldSnapshot.WriteInventory(w, g.Inventory); foreach (int durability in g.Tools) { w.Write(durability); }
            }
            w.Write(state.Shelters.Count);
            foreach (ShelterState shelter in state.Shelters)
            {
                w.Write(shelter.Id); WorldSnapshot.WritePos(w, shelter.Position); WorldSnapshot.WritePos(w, shelter.Entrance);
                w.Write(shelter.Capacity); WorldSnapshot.WriteInventory(w, shelter.Inventory);
                w.Write(shelter.Temperature); w.Write(shelter.FuelTicks); w.Write(shelter.HeatBudget);
            }
            foreach (var npc in world.Entities.Npcs)
            {
                var n = npc.Survival;
                w.Write(n.Stamina); w.Write(n.RecoveringStamina); w.Write((int)n.HuntMotion); w.Write(n.NextHuntMoveTick); w.Write(n.LastStaminaTick);
                w.Write((int)n.Life); w.Write(n.RescueDeadline); w.Write(n.Rescuer.Value); w.Write(n.DiseaseTicks); w.Write(n.WorkRetryTick);
                w.Write(n.HuntDropId); w.Write(n.Rescues); w.Write(n.Crafted); w.Write(n.Built); w.Write(n.WaterProduced);
                w.Write(n.ShelterId); w.Write(n.BodyTemperature); w.Write(n.Wetness); w.Write(n.ColdTicks); w.Write(n.CoatDurability);
                w.Write((int)n.DeathCause);
                foreach (int durability in n.Tools) { w.Write(durability); }
            }
            foreach (var p in world.Resources.FarmPlots) { w.Write((int)p.PreviousCrop); w.Write(p.Damage); }
        }
        internal static void Read(BinaryReader r, WorldState world)
        {
            var state = world.Survival;
            state.NextId = r.ReadInt32(); state.RefillWater = r.ReadBoolean(); state.CropsLost = r.ReadInt32();
            int count = WorldSnapshot.ReadCount(r, 1000);
            for (int i = 0; i < count; i++)
            {
                var f = new FacilityState(r.ReadInt32(), (FacilityKind)r.ReadInt32(), WorldSnapshot.ReadPos(r), WorldSnapshot.ReadPos(r));
                f.PendingRecipeId = r.ReadInt32();
                WorldSnapshot.Require(f.PendingRecipeId == 0 || ProductionRecipe.ById(f.PendingRecipeId) != null && ProductionRecipe.ById(f.PendingRecipeId).Facility == f.Kind);
                f.Progress = r.ReadInt32(); f.Condition = r.ReadInt32(); f.ReservedBy = new EntityId(r.ReadInt32()); f.WellNodeId = r.ReadInt32();
                WorldSnapshot.ReadInventory(r, f.Inventory);
                WorldSnapshot.Require(f.Id > 0 && state.FindFacility(f.Id) == null && Enum.IsDefined(typeof(FacilityKind), f.Kind) &&
                    f.Progress >= 0 && f.Progress <= SurvivalRules.ConstructionTicks && f.Condition >= 0 && f.Condition <= 100 &&
                    world.Map.IsInside(f.Position) && !world.Map.IsWalkable(f.Position) && world.Map.IsWalkable(f.WorkCell) && f.Position.ManhattanDistance(f.WorkCell) == 1 && Owner(world, f.ReservedBy));
                WorldSnapshot.Require(f.WellNodeId == 0 || world.Resources.FindNode(f.WellNodeId) != null);
                state.MutableFacilities.Add(f);
            }
            count = WorldSnapshot.ReadCount(r, 100000);
            for (int i = 0; i < count; i++)
            {
                var g = new GroundStockState(r.ReadInt32(), WorldSnapshot.ReadPos(r)); g.Coins = r.ReadInt64(); g.ReservedBy = new EntityId(r.ReadInt32());
                WorldSnapshot.ReadInventory(r, g.Inventory);
                for (int j = 0; j < 4; j++) { g.Tools[j] = ReadDurability(r); }
                WorldSnapshot.Require(g.Id > 0 && state.FindFacility(g.Id) == null && state.FindGround(g.Id) == null && g.Coins >= 0 && world.Map.IsWalkable(g.Position) && Owner(world, g.ReservedBy));
                state.MutableGround.Add(g);
            }
            count = WorldSnapshot.ReadCount(r, 10000);
            var entrances = new System.Collections.Generic.HashSet<GridPos>();
            for (int i = 0; i < count; i++)
            {
                var shelter = new ShelterState(r.ReadInt32(), WorldSnapshot.ReadPos(r), WorldSnapshot.ReadPos(r), r.ReadInt32());
                WorldSnapshot.ReadInventory(r, shelter.Inventory);
                shelter.Temperature = r.ReadInt32(); shelter.FuelTicks = r.ReadInt32(); shelter.HeatBudget = r.ReadInt32();
                WorldSnapshot.Require(state.FindShelter(shelter.Id) == null && state.FindFacility(shelter.Id) == null &&
                    state.FindGround(shelter.Id) == null && shelter.Capacity <= 10000 && entrances.Add(shelter.Entrance) &&
                    world.Map.IsInside(shelter.Position) && world.Map.GetTile(shelter.Position) == TileType.House && world.Map.IsWalkable(shelter.Entrance) &&
                    shelter.Temperature >= EnvironmentState.MinTemperature && shelter.Temperature <= EnvironmentState.MaxTemperature &&
                    shelter.FuelTicks >= 0 && shelter.FuelTicks <= SurvivalRules.FuelTicksPerWood &&
                    shelter.HeatBudget >= 0 && shelter.HeatBudget <= shelter.FuelTicks * SurvivalRules.HeatPerTick);
                state.MutableShelters.Add(shelter);
            }
            foreach (var npc in world.Entities.Npcs)
            {
                var n = npc.Survival;
                n.Stamina = r.ReadInt32(); n.RecoveringStamina = r.ReadBoolean(); n.HuntMotion = (HuntMotion)r.ReadInt32(); n.NextHuntMoveTick = r.ReadInt64(); n.LastStaminaTick = r.ReadInt64();
                WorldSnapshot.Require(n.Stamina >= 0 && n.Stamina <= 10000 && Enum.IsDefined(typeof(HuntMotion), n.HuntMotion) && n.NextHuntMoveTick >= 0 && n.LastStaminaTick >= -1 && n.LastStaminaTick <= world.Tick);
                n.Life = (LifeState)r.ReadInt32(); n.RescueDeadline = r.ReadInt64(); n.Rescuer = new EntityId(r.ReadInt32()); n.DiseaseTicks = r.ReadInt32(); n.WorkRetryTick = r.ReadInt64();
                n.HuntDropId = r.ReadInt32(); n.Rescues = r.ReadInt32(); n.Crafted = r.ReadInt32(); n.Built = r.ReadInt32(); n.WaterProduced = r.ReadInt32();
                n.ShelterId = r.ReadInt32(); n.BodyTemperature = r.ReadInt32(); n.Wetness = r.ReadInt32();
                n.ColdTicks = r.ReadInt32(); n.CoatDurability = ReadDurability(r);
                n.DeathCause = (NpcDeathCause)r.ReadInt32();
                WorldSnapshot.Require(Enum.IsDefined(typeof(NpcDeathCause), n.DeathCause) && n.ShelterId >= 0 && (n.ShelterId == 0 || state.FindShelter(n.ShelterId) != null &&
                    npc.Home == state.FindShelter(n.ShelterId).Entrance) && n.BodyTemperature >= 2000 && n.BodyTemperature <= 3700 &&
                    n.Wetness >= 0 && n.Wetness <= 10000 && n.ColdTicks >= 0 && n.ColdTicks <= SurvivalRules.MaxColdTicks &&
                    (n.Life != LifeState.Dead || n.CoatDurability == 0));
                for (int j = 0; j < 4; j++) { n.Tools[j] = ReadDurability(r); }
                CarcassState carcass = world.Ecology.FindCarcass(n.HuntDropId);
                WorldSnapshot.Require(Enum.IsDefined(typeof(LifeState), n.Life) && n.DiseaseTicks >= 0 && n.DiseaseTicks <= SurvivalRules.DiseaseDuration && Owner(world, n.Rescuer) &&
                    (n.HuntDropId == 0 || carcass != null && carcass.Region == -1 && carcass.ReservedBy == npc.Id &&
                        npc.Plan.CurrentStep?.Type == PlanStepType.Hunt && npc.Plan.CurrentStep.ResourceId == n.HuntDropId) &&
                    n.Rescues >= 0 && n.Crafted >= 0 && n.Built >= 0 && n.WaterProduced >= 0);
            }
            foreach (var p in world.Resources.FarmPlots)
            { p.PreviousCrop = (CropType)r.ReadInt32(); p.Damage = r.ReadInt32(); WorldSnapshot.Require(Enum.IsDefined(typeof(CropType), p.PreviousCrop) && p.Damage >= 0 && p.Damage < SurvivalRules.CropDeathDamage); }
            int max = 0;
            foreach (var f in state.Facilities) { max = Math.Max(max, f.Id); }
            foreach (var g in state.Ground) { max = Math.Max(max, g.Id); }
            foreach (ShelterState shelter in state.Shelters)
            {
                max = Math.Max(max, shelter.Id);
                int residents = 0;
                foreach (var npc in world.Entities.Npcs)
                {
                    if (npc.Survival.Life != LifeState.Dead && npc.Survival.ShelterId == shelter.Id) { residents++; }
                }
                WorldSnapshot.Require(residents <= shelter.Capacity);
            }
            WorldSnapshot.Require(state.NextId > max && state.CropsLost >= 0);
        }
        private static int ReadDurability(BinaryReader r) { int value = r.ReadInt32(); WorldSnapshot.Require(value >= 0 && value <= SurvivalRules.ToolDurability); return value; }
        private static bool Owner(WorldState world, EntityId id) => id.IsNone || world.Entities.TryGetNpc(id, out var _);
    }
}
