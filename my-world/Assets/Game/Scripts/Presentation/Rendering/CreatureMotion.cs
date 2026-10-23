using MyAiWorld.Sim.Core;
using UnityEngine;

namespace MyAiWorld.Presentation.Rendering
{
    // Presentation-only interpolation, shared by people and wildlife.
    public sealed class CreatureMotion
    {
        private Vector3 _from;
        private Vector3 _to;
        private long _tick = long.MinValue;
        public GridPos Cell { get; private set; } = GridPos.Invalid;
        public int Direction { get; private set; }
        public bool Moving { get; private set; }
        public Vector3 Position { get; private set; }

        public void SetCell(GridPos cell, bool instant, long tick)
        {
            // Alpha restarts on every simulation tick, even when the cell stays
            // unchanged. Advance both endpoints or the last move will replay.
            if (!cell.IsValid || tick == _tick && !instant) { return; }
            Vector3 target = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0f);
            Vector3 delta = target - _to;
            if (!instant && delta.sqrMagnitude > 0f)
            {
                Direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? (delta.x > 0 ? 1 : 3) : delta.y > 0 ? 2 : 0;
            }
            _tick = tick;
            Cell = cell;
            _from = instant ? target : _to;
            _to = target;
            if (instant) { Position = target; Moving = false; }
        }

        public Vector3 Sample(float alpha)
        {
            Position = Vector3.Lerp(_from, _to, Mathf.Clamp01(alpha));
            Moving = (_to - Position).sqrMagnitude > 0.00001f;
            return Position;
        }

        public void Face(int x, int y)
        {
            Direction = FacingDirection(x, y, Direction);
        }

        public static int FacingDirection(int x, int y, int fallback = 0)
        {
            if (x == 0 && y == 0) { return fallback; }
            return Mathf.Abs((float)x) > Mathf.Abs((float)y) ? (x > 0 ? 1 : 3) : y > 0 ? 2 : 0;
        }

        public static int SortOrder(float footY) => Mathf.RoundToInt(-footY * 32f);
    }
}
