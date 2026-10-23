using System.Collections.Generic;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Core;
using UnityEngine;

namespace MyAiWorld.Presentation.Atmosphere
{
    // Fades tree and shelter canopies that sit in front of villagers. Checks run at
    // roughly 11 Hz through a coarse spatial hash so hundreds of trees stay cheap.
    public sealed class CanopyFader : MonoBehaviour
    {
        private const float BucketSize = 4f;
        private const float CheckInterval = 0.09f;

        private sealed class Entry
        {
            public SpriteRenderer Renderer;
            public Rect Canopy;
            public Color BaseColor;
            public float Alpha = 1f;
            public float Target = 1f;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<long, List<Entry>> _buckets = new Dictionary<long, List<Entry>>();
        private AtmosphereProfile _profile;
        private float _lastCheck = float.NegativeInfinity;
        private bool _first = true;

        public int TreeCount => _entries.Count;
        public bool SelectedOccluded { get; private set; }
        public bool OcclusionEnabled => Enabled && _profile != null && _profile.canopyFade;
        public float SelectedAlpha => 0.35f;
        public bool Enabled { get; private set; } = true;

        public void Initialize(AtmosphereProfile profile)
        {
            _profile = profile;
            Enabled = profile == null || profile.canopyFade;
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (!enabled) { Restore(); }
        }

        public void Restore()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                entry.Target = 1f;
                entry.Alpha = 1f;
                if (entry.Renderer == null) { continue; }
                Color color = entry.BaseColor;
                color.a = entry.BaseColor.a;
                entry.Renderer.color = color;
            }
            SelectedOccluded = false;
        }

        public void Clear()
        {
            _entries.Clear();
            _buckets.Clear();
            SelectedOccluded = false;
            _first = true;
            _lastCheck = float.NegativeInfinity;
        }

        public void Register(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null) { return; }
            Vector3 position = renderer.transform.position;
            var canopy = new Rect(position.x - 0.7f, position.y + 0.15f, 1.4f, 1.55f);
            var entry = new Entry
            {
                Renderer = renderer,
                Canopy = canopy,
                BaseColor = renderer.color,
                Alpha = 1f,
                Target = 1f
            };
            _entries.Add(entry);
            long key = BucketKey(Mathf.FloorToInt(canopy.center.x / BucketSize), Mathf.FloorToInt(canopy.center.y / BucketSize));
            List<Entry> bucket;
            if (!_buckets.TryGetValue(key, out bucket))
            {
                bucket = new List<Entry>();
                _buckets.Add(key, bucket);
            }
            bucket.Add(entry);
        }

        public void Present(float animationTime, IReadOnlyList<NpcView> views, EntityId? selected)
        {
            if (_first || animationTime - _lastCheck >= CheckInterval || animationTime < _lastCheck)
            {
                Recompute(views, selected);
                _lastCheck = animationTime;
                _first = false;
            }
            Apply(Mathf.Max(Time.unscaledDeltaTime, 0f) * 6f);
        }

        private void Recompute(IReadOnlyList<NpcView> views, EntityId? selected)
        {
            bool enabled = OcclusionEnabled;
            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].Target = 1f;
            }
            SelectedOccluded = false;
            if (!enabled) { return; }

            for (int i = 0; i < views.Count; i++)
            {
                NpcView view = views[i];
                if (view == null || !view.isActiveAndEnabled) { continue; }
                bool isSelected = selected.HasValue && view.Id == selected.Value;
                float target = isSelected ? SelectedAlpha : _profile.canopyOpacity;
                var point = new Vector2(view.transform.position.x, view.transform.position.y + 0.7f);
                int bx = Mathf.FloorToInt(point.x / BucketSize);
                int by = Mathf.FloorToInt(point.y / BucketSize);
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    List<Entry> bucket;
                    if (!_buckets.TryGetValue(BucketKey(bx + ox, by + oy), out bucket)) { continue; }
                    for (int e = 0; e < bucket.Count; e++)
                    {
                        Entry entry = bucket[e];
                        if (!entry.Canopy.Contains(point)) { continue; }
                        if (target < entry.Target) { entry.Target = target; }
                        if (isSelected) { SelectedOccluded = true; }
                    }
                }
            }
        }

        private void Apply(float step)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry.Renderer == null) { continue; }
                entry.Alpha = Mathf.MoveTowards(entry.Alpha, entry.Target, step);
                Color color = entry.BaseColor;
                color.a = entry.BaseColor.a * entry.Alpha;
                entry.Renderer.color = color;
            }
        }

        // Test hook: resolves one focus point without throttling and snaps alpha.
        public int ApplyFocus(Vector3 point, bool selected)
        {
            bool enabled = OcclusionEnabled;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                entry.Target = 1f;
                entry.Alpha = 1f;
                if (entry.Renderer == null) { continue; }
                Color color = entry.BaseColor;
                color.a = entry.BaseColor.a;
                entry.Renderer.color = color;
            }
            SelectedOccluded = false;
            if (!enabled) { return 0; }

            var focus = new Vector2(point.x, point.y + 0.7f);
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (!entry.Canopy.Contains(focus)) { continue; }
                count++;
                entry.Target = selected ? SelectedAlpha : _profile.canopyOpacity;
                entry.Alpha = entry.Target;
                Color color = entry.BaseColor;
                color.a = entry.BaseColor.a * entry.Alpha;
                entry.Renderer.color = color;
            }
            if (selected && count > 0) { SelectedOccluded = true; }
            return count;
        }

        private static long BucketKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }
}
