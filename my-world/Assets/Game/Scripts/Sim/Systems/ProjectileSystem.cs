using System;
using System.Collections.Generic;
using System.Numerics;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class ProjectileSystem
    {
        // Exact fraction of one tick. Never round slab bounds or contact ordering.
        private readonly struct SweepTime : IComparable<SweepTime>
        {
            internal SweepTime(long numerator, long denominator)
            { Numerator = numerator; Denominator = denominator; }
            internal long Numerator { get; }
            internal long Denominator { get; }
            internal static SweepTime Zero => new SweepTime(0, 1);
            internal static SweepTime End => new SweepTime(1, 1);
            internal static SweepTime NoContact => new SweepTime(2, 1);
            public int CompareTo(SweepTime other) =>
                ((BigInteger)Numerator * other.Denominator).CompareTo((BigInteger)other.Numerator * Denominator);
            internal int Quantize() => (int)(((BigInteger)Numerator * CombatState.TimeUnitsPerTick + Denominator - 1) / Denominator);
            internal int Interpolate(int from, int to) =>
                from + (int)(((BigInteger)to - from) * Numerator / Denominator);
        }

        private readonly struct Contact
        {
            internal Contact(SweepTime time, EntityId entity, bool animal, bool boundary, GridPos terrain)
            { Time = time; Entity = entity; IsAnimal = animal; Boundary = boundary; Terrain = terrain; }
            internal SweepTime Time { get; }
            internal EntityId Entity { get; }
            internal bool IsAnimal { get; }
            internal bool Boundary { get; }
            internal GridPos Terrain { get; }
        }
        private sealed class Flight
        {
            internal ProjectileState Arrow;
            internal int FromX, FromY, ToX, ToY;
            internal Contact Contact;
        }

        public static void Update(WorldState world, List<SimEvent> events)
        {
            UpdateEmbedded(world);
            var flights = new List<Flight>();
            foreach (ProjectileState arrow in world.Combat.Projectiles)
            {
                if (arrow.Phase != ProjectilePhase.Flying || arrow.LastAdvancedTick >= world.Tick || arrow.ReleasedTick >= world.Tick) { continue; }
                var flight = new Flight
                {
                    Arrow = arrow, FromX = arrow.X, FromY = arrow.Y,
                    ToX = arrow.X + arrow.VelocityX, ToY = arrow.Y + arrow.VelocityY
                };
                flight.Contact = FindContact(world, flight);
                flights.Add(flight);
            }
            // Physical contacts are ordered across arrows as well as within each sweep.
            // A second arrow can stop at the same just-killed body, but cannot kill it twice.
            flights.Sort((a, b) =>
            {
                int order = a.Contact.Time.CompareTo(b.Contact.Time);
                if (order == 0) { order = a.Contact.Entity.Value.CompareTo(b.Contact.Entity.Value); }
                return order != 0 ? order : a.Arrow.Id.CompareTo(b.Arrow.Id);
            });
            foreach (Flight flight in flights)
            {
                ProjectileState arrow = flight.Arrow;
                Contact hit = flight.Contact;
                bool contact = hit.Time.CompareTo(SweepTime.End) <= 0;
                SweepTime stop = contact ? hit.Time : SweepTime.End;
                int time = stop.Quantize();
                arrow.X = stop.Interpolate(flight.FromX, flight.ToX);
                arrow.Y = stop.Interpolate(flight.FromY, flight.ToY);
                arrow.LastAdvancedTick = world.Tick;
                arrow.RemainingTicks--;
                UpdateLastWalkable(world, arrow, flight.FromX, flight.FromY, flight.ToX, flight.ToY, stop);
                UpdateShooterOverlap(world, arrow);
                CombatEventKind state = CombatEventKind.Flying;
                if (contact)
                {
                    if (hit.Boundary)
                    {
                        state = CombatEventKind.Lost;
                        RemoveLost(world, arrow, state);
                    }
                    else if (!hit.Entity.IsNone)
                    {
                        CombatSystem.ApplyDamage(world, arrow.Shooter, false, hit.Entity, hit.IsAnimal,
                            arrow.Damage, arrow.AttackId, arrow.Id, time, events);
                        if (arrow.Integrity > 0 && CombatSystem.TryPosition(world, hit.Entity, hit.IsAnimal, out GridPos target))
                        {
                            arrow.Phase = ProjectilePhase.Embedded;
                            arrow.EmbeddedTarget = hit.Entity;
                            arrow.EmbeddedTargetIsAnimal = hit.IsAnimal;
                            arrow.VelocityX = 0; arrow.VelocityY = 0;
                            state = CombatEventKind.Embedded;
                            // The victim finishes its movement at the tick boundary. Preserve the
                            // actual contact in the trace, then attach the authoritative arrow.
                            EmitEvent(world, arrow, state, hit.Entity, time);
                            AddTrace(world, flight, hit.Entity, state, time);
                            Follow(world, arrow, target);
                            continue;
                        }
                        state = StopOnGround(world, arrow, time);
                    }
                    else
                    {
                        world.Combat.Misses++;
                        // Rock/house impacts destroy the shaft; forest and ordinary landings
                        // retain release-time quality. No collision-time random roll.
                        TileType tile = world.Map.GetTile(hit.Terrain);
                        if (tile == TileType.Rock || tile == TileType.House) { arrow.Integrity = 0; }
                        state = StopOnGround(world, arrow, time);
                    }
                }
                else if (arrow.RemainingTicks <= 0)
                {
                    world.Combat.Misses++;
                    state = StopOnGround(world, arrow, time);
                }
                AddTrace(world, flight, hit.Entity, state, time);
            }
            // Dead/missing embedded victims become ground stock at their last known point.
            UpdateEmbedded(world);
        }

        public static void UpdateEmbedded(WorldState world)
        {
            foreach (ProjectileState arrow in world.Combat.MutableProjectiles.ToArray())
            {
                if (arrow.Phase != ProjectilePhase.Embedded) { continue; }
                if (CombatSystem.TryPosition(world, arrow.EmbeddedTarget, arrow.EmbeddedTargetIsAnimal, out GridPos target))
                {
                    Follow(world, arrow, target);
                }
                else
                {
                    // Dead local bodies may finish a valid movement. External/missing bodies
                    // must not drag an arrow to an invalid coordinate or across the map.
                    AnimalState animal = arrow.EmbeddedTargetIsAnimal ? world.Ecology.Find(arrow.EmbeddedTarget.Value) : null;
                    if (CombatSystem.HasLocalPosition(world, animal))
                    {
                        Follow(world, arrow, animal.Position);
                    }
                    else if (!arrow.EmbeddedTargetIsAnimal && world.Entities.TryGetNpc(arrow.EmbeddedTarget, out NpcState npc) &&
                        world.Map.IsInside(npc.Position))
                    {
                        Follow(world, arrow, npc.Position);
                    }
                    else
                    {
                        // This saved position is authority. Transient movement records are not
                        // saved and must not change a pending transfer after a save/restore.
                        if (world.Map.IsWalkable(arrow.LastWalkablePosition)) { Follow(world, arrow, arrow.LastWalkablePosition); }
                    }
                    // Removal is the ownership transfer: repeated updates cannot drop it twice.
                    StopOnGround(world, arrow, CombatState.TimeUnitsPerTick);
                }
            }
        }

        private static void Follow(WorldState world, ProjectileState arrow, GridPos target)
        {
            arrow.X = target.X * CombatState.UnitsPerCell + CombatState.UnitsPerCell / 2;
            arrow.Y = target.Y * CombatState.UnitsPerCell + CombatState.UnitsPerCell / 2;
            if (world.Map.IsWalkable(target)) { arrow.LastWalkablePosition = target; }
        }

        private static Contact FindContact(WorldState world, Flight flight)
        {
            Contact best = new Contact(SweepTime.NoContact, EntityId.None, false, false, GridPos.Invalid);
            int scale = CombatState.UnitsPerCell;
            long dx = (long)flight.ToX - flight.FromX, dy = (long)flight.ToY - flight.FromY;
            SweepTime enter = SweepTime.Zero, exit = SweepTime.End;
            if (!Slab(flight.FromX, dx, 0, (long)world.Map.Width * scale - 1, ref enter, ref exit) ||
                !Slab(flight.FromY, dy, 0, (long)world.Map.Height * scale - 1, ref enter, ref exit))
            {
                best = new Contact(SweepTime.Zero, EntityId.None, false, true, GridPos.Invalid);
            }
            else if (exit.CompareTo(SweepTime.End) < 0)
            {
                best = new Contact(exit, EntityId.None, false, true, GridPos.Invalid);
            }
            // Bounded velocities make this local swept-cell rectangle small even at maximum speed.
            // Closed tile boxes include a corner/edge even on the minimum side.
            int minX = Math.Max(0, FloorCell(Math.Min(flight.FromX, flight.ToX)) - 1);
            int maxX = Math.Min(world.Map.Width - 1, FloorCell(Math.Max(flight.FromX, flight.ToX)));
            int minY = Math.Max(0, FloorCell(Math.Min(flight.FromY, flight.ToY)) - 1);
            int maxY = Math.Min(world.Map.Height - 1, FloorCell(Math.Max(flight.FromY, flight.ToY)));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new GridPos(x, y);
                    TileType tile = world.Map.GetTile(cell);
                    if (tile != TileType.Forest && tile != TileType.Rock && tile != TileType.House) { continue; }
                    // A shooter inside a forest can shoot outward; only its launch cell's
                    // connected initial overlap is ignored, not later forest intersections.
                    if (flight.Arrow.IgnoreInitialShooterOverlap && Cell(flight.FromX, flight.FromY) == cell) { continue; }
                    if (SweepBox(flight.FromX, flight.FromY, dx, dy, (long)x * scale, (long)y * scale,
                        (long)(x + 1) * scale, (long)(y + 1) * scale, out SweepTime time))
                    {
                        var candidate = new Contact(time, EntityId.None, false, false, cell);
                        if (Earlier(candidate, best, world.Map.Width)) { best = candidate; }
                    }
                }
            }
            foreach (AnimalState animal in world.Ecology.Animals)
            {
                if (animal.Health <= 0 || !CombatSystem.HasLocalPosition(world, animal)) { continue; }
                int radius = animal.Species == AnimalSpecies.Vole ? 128 : animal.Species == AnimalSpecies.Rabbit ? 205 :
                    animal.Species == AnimalSpecies.Raven ? 160 : animal.Species == AnimalSpecies.Fox ? 256 : 350;
                ConsiderBody(world, flight, animal.Id, animal.Position, radius, true, ref best);
            }
            foreach (NpcState npc in world.Entities.Npcs)
            {
                if (npc.Survival.Life == LifeState.Dead || npc.Needs.Health <= 0 || !world.Map.IsInside(npc.Position)) { continue; }
                ConsiderBody(world, flight, npc.Id, npc.Position, 256, false, ref best);
            }
            return best;
        }

        private static void ConsiderBody(WorldState world, Flight flight, EntityId id, GridPos current,
            int radius, bool animal, ref Contact best)
        {
            GridPos start = world.Combat.StartPosition(id, current);
            // A newly returned resident has no local start; never sweep from (-1,-1).
            if (!world.Map.IsInside(start)) { start = current; }
            int scale = CombatState.UnitsPerCell;
            long x = flight.FromX - ((long)start.X * scale + scale / 2);
            long y = flight.FromY - ((long)start.Y * scale + scale / 2);
            long dx = (long)flight.ToX - flight.FromX - ((long)current.X - start.X) * scale;
            long dy = (long)flight.ToY - flight.FromY - ((long)current.Y - start.Y) * scale;
            // Ignore only the connected launch overlap. A separated shooter is ordinary geometry.
            if (id == flight.Arrow.Shooter && flight.Arrow.IgnoreInitialShooterOverlap &&
                Math.Abs(x) <= radius && Math.Abs(y) <= radius) { return; }
            if (SweepBox(x, y, dx, dy, -radius, -radius, radius, radius, out SweepTime time))
            {
                var candidate = new Contact(time, id, animal, false, GridPos.Invalid);
                if (Earlier(candidate, best, world.Map.Width)) { best = candidate; }
            }
        }

        private static bool Earlier(Contact a, Contact b, int width)
        {
            int order = a.Time.CompareTo(b.Time);
            if (order != 0) { return order < 0; }
            if (a.Entity.Value != b.Entity.Value) { return a.Entity.Value < b.Entity.Value; }
            if (a.Boundary != b.Boundary) { return a.Boundary; }
            return (long)a.Terrain.Y * width + a.Terrain.X < (long)b.Terrain.Y * width + b.Terrain.X;
        }

        // Continuous segment against an axis-aligned fixed-point body. Relative movement
        // accounts for both the projectile and the body's complete minute-long path.
        private static bool SweepBox(long x, long y, long dx, long dy, long minX, long minY, long maxX, long maxY, out SweepTime time)
        {
            SweepTime enter = SweepTime.Zero, exit = SweepTime.End;
            bool hit = Slab(x, dx, minX, maxX, ref enter, ref exit) && Slab(y, dy, minY, maxY, ref enter, ref exit);
            time = enter;
            return hit;
        }
        private static bool Slab(long start, long delta, long min, long max, ref SweepTime enter, ref SweepTime exit)
        {
            if (delta == 0) { return start >= min && start <= max; }
            long near = min - start, far = max - start;
            long divisor = delta;
            if (divisor < 0) { long swap = near; near = -far; far = -swap; divisor = -divisor; }
            var first = new SweepTime(near, divisor);
            var last = new SweepTime(far, divisor);
            if (first.CompareTo(exit) > 0 || last.CompareTo(enter) < 0) { return false; }
            if (first.CompareTo(enter) > 0) { enter = first; }
            if (last.CompareTo(exit) < 0) { exit = last; }
            return enter.CompareTo(exit) <= 0;
        }
        private static long FloorDivide(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        internal static int Interpolate(int from, int to, int time) => from + (int)(((long)to - from) * time / CombatState.TimeUnitsPerTick);
        private static int FloorCell(int value) => (int)FloorDivide(value, CombatState.UnitsPerCell);
        public static GridPos Cell(int x, int y) => new GridPos(FloorCell(x), FloorCell(y));

        private static void UpdateShooterOverlap(WorldState world, ProjectileState arrow)
        {
            if (!arrow.IgnoreInitialShooterOverlap) { return; }
            if (!world.Entities.TryGetNpc(arrow.Shooter, out NpcState shooter)) { arrow.IgnoreInitialShooterOverlap = false; return; }
            int x = shooter.Position.X * CombatState.UnitsPerCell + 512;
            int y = shooter.Position.Y * CombatState.UnitsPerCell + 512;
            arrow.IgnoreInitialShooterOverlap = Math.Abs(arrow.X - x) <= 256 && Math.Abs(arrow.Y - y) <= 256;
        }
        private static void UpdateLastWalkable(WorldState world, ProjectileState arrow, int fromX, int fromY, int toX, int toY, SweepTime until)
        {
            // Clip the original sweep by the exact contact, not its quantized trace.
            int scale = CombatState.UnitsPerCell;
            int endX = until.Interpolate(fromX, toX), endY = until.Interpolate(fromY, toY);
            int minX = Math.Max(0, FloorCell(Math.Min(fromX, endX)) - 1);
            int maxX = Math.Min(world.Map.Width - 1, FloorCell(Math.Max(fromX, endX)));
            int minY = Math.Max(0, FloorCell(Math.Min(fromY, endY)) - 1);
            int maxY = Math.Min(world.Map.Height - 1, FloorCell(Math.Max(fromY, endY)));
            SweepTime lastTime = new SweepTime(-1, 1);
            GridPos endpoint = Cell(endX, endY);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new GridPos(x, y);
                    if (!world.Map.IsWalkable(cell)) { continue; }
                    SweepTime enter = SweepTime.Zero, exit = until;
                    if (!Slab(fromX, (long)toX - fromX, (long)x * scale, (long)(x + 1) * scale, ref enter, ref exit) ||
                        !Slab(fromY, (long)toY - fromY, (long)y * scale, (long)(y + 1) * scale, ref enter, ref exit)) { continue; }
                    int order = exit.CompareTo(lastTime);
                    if (order > 0 || (order == 0 && cell == endpoint))
                    {
                        lastTime = exit;
                        arrow.LastWalkablePosition = cell;
                    }
                }
            }
        }
        private static CombatEventKind StopOnGround(WorldState world, ProjectileState arrow, int time)
        {
            if (arrow.Integrity <= 0)
            {
                RemoveLost(world, arrow, CombatEventKind.Broken, time);
                return CombatEventKind.Broken;
            }
            if (!world.Map.IsWalkable(arrow.LastWalkablePosition))
            {
                RemoveLost(world, arrow, CombatEventKind.Lost, time);
                return CombatEventKind.Lost;
            }
            GroundStockState stock = null;
            foreach (GroundStockState ground in world.Survival.Ground)
            {
                if (ground.Position == arrow.LastWalkablePosition && ground.ReservedBy.IsNone &&
                    ground.Inventory.TotalCount < ground.Inventory.Capacity) { stock = ground; break; }
            }
            stock = stock ?? world.Survival.Drop(arrow.LastWalkablePosition);
            if (!stock.Inventory.TryAdd(ItemKind.Arrow, 1)) { throw new InvalidOperationException("Arrow ground transfer failed."); }
            world.Combat.MutableProjectiles.Remove(arrow);
            EmitEvent(world, arrow, CombatEventKind.Grounded, arrow.EmbeddedTarget, time);
            return CombatEventKind.Grounded;
        }
        private static void RemoveLost(WorldState world, ProjectileState arrow, CombatEventKind kind, int time = CombatState.TimeUnitsPerTick)
        {
            if (!world.Combat.MutableProjectiles.Remove(arrow)) { return; }
            world.Materials.Consume(ItemKind.Arrow, 1);
            world.Combat.ArrowsLost++;
            EmitEvent(world, arrow, kind, arrow.EmbeddedTarget, time);
        }
        private static void EmitEvent(WorldState world, ProjectileState arrow, CombatEventKind kind, EntityId hit, int time)
        {
            world.Combat.AddEvent(new CombatVisualEvent(world.Tick, arrow.AttackId, arrow.Id, arrow.Shooter,
                hit, kind, arrow.X, arrow.Y, time, 0));
        }
        private static void AddTrace(WorldState world, Flight flight, EntityId hit, CombatEventKind state, int time)
        {
            world.Combat.AddTrace(new CombatTraceSegment(world.Tick, flight.Arrow.Id, flight.Arrow.Shooter,
                flight.FromX, flight.FromY, flight.Arrow.X, flight.Arrow.Y, hit, state, 0, time));
        }
    }
}
