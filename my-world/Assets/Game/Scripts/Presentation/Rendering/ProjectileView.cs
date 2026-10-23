using System.Collections.Generic;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.Rendering
{
    // Plays only recorded sweeps. Never aims at a target or extrapolates a collision.
    public sealed class ProjectileView : MonoBehaviour
    {
        private sealed class Sweep
        {
            public CombatTraceSegment Trace;
            public float Start;
            public float End;
            public bool Sampled;
            public bool DeferredCompletion;
            public float FirstSampleClock;
        }

        private readonly Queue<Sweep> _sweeps = new Queue<Sweep>();
        private PastoralArtConfig _art;
        private SpriteRenderer _arrow;
        private SpriteRenderer _impact;
        private ProjectileSnapshot _snapshot;
        private float _impactUntil;
        private Vector3 _impactPosition;
        private bool _effectOnly;
        private bool _hasTracePosition;
        private bool _terminal;
        public long Id { get; private set; }
        public int PendingTraceCount => _sweeps.Count;
        public int PlayedTraceCount { get; private set; }
        public bool IsEmbedded => _snapshot != null && _snapshot.Phase == ProjectilePhase.Embedded;
        public string SpriteKey { get; private set; }
        public string ImpactKey { get; private set; }
        public Sprite CurrentSprite => _arrow.sprite;
        public bool ArrowVisible => _arrow.enabled;
        public bool ImpactVisible => _impact.enabled;

        public void Initialize(long id, PastoralArtConfig art)
        {
            Id = id; _art = art;
            var shaft = new GameObject("Arrow", typeof(SpriteRenderer));
            shaft.transform.SetParent(transform, false); _arrow = shaft.GetComponent<SpriteRenderer>();
            var impact = new GameObject("Impact", typeof(SpriteRenderer));
            impact.transform.SetParent(transform, false); _impact = impact.GetComponent<SpriteRenderer>();
            _impact.enabled = false; _arrow.enabled = false;
            SpriteKey = "projectile_arrow"; _arrow.sprite = _art.Get(SpriteKey);
        }

        public void SetAuthoritative(ProjectileSnapshot snapshot, bool instant = false)
        {
            _snapshot = snapshot;
            if (snapshot != null && (instant || _sweeps.Count == 0 && !_hasTracePosition))
            {
                transform.position = Position(snapshot.X, snapshot.Y);
                Face(snapshot.VelocityX, snapshot.VelocityY);
                _arrow.enabled = true;
            }
        }

        public void Enqueue(CombatTraceSegment trace, float start, float end)
        {
            _sweeps.Enqueue(new Sweep { Trace = trace, Start = start, End = end });
        }

        public void ReceiveRelease(CombatVisualEvent combatEvent)
        {
            if (_hasTracePosition || _snapshot != null) { return; }
            transform.position = Position(combatEvent.X, combatEvent.Y); _arrow.enabled = true;
        }

        public void InitializeEffect(CombatVisualEvent combatEvent, PastoralArtConfig art, float clock)
        {
            Initialize(0, art); _effectOnly = true;
            transform.position = Position(combatEvent.X, combatEvent.Y);
            ShowImpact(combatEvent.Damage > 0, transform.position, clock);
        }

        // True means presentation has finished and the owner may remove this view.
        public bool Present(float clock)
        {
            while (_sweeps.Count > 0)
            {
                Sweep sweep = _sweeps.Peek();
                if (clock < sweep.Start) { break; }
                CombatTraceSegment trace = sweep.Trace;
                float alpha = sweep.End <= sweep.Start ? 1f : Mathf.Clamp01((clock - sweep.Start) / (sweep.End - sweep.Start));
                if (!sweep.Sampled)
                {
                    sweep.Sampled = true; sweep.FirstSampleClock = clock;
                    // Even a zero-time contact or a swept segment shorter than one render
                    // frame must display a real shaft before its terminal effect.
                    sweep.DeferredCompletion = alpha >= 1f;
                }
                bool deferred = sweep.DeferredCompletion && clock <= sweep.FirstSampleClock;
                if (deferred) { alpha = 0.5f; }
                transform.position = Vector3.Lerp(Position(trace.FromX, trace.FromY), Position(trace.ToX, trace.ToY), alpha);
                Face(trace.ToX - trace.FromX, trace.ToY - trace.FromY);
                _hasTracePosition = true; _arrow.enabled = true;
                if (clock < sweep.End || deferred) { break; }
                _sweeps.Dequeue(); PlayedTraceCount++;
                if (trace.State != CombatEventKind.Flying)
                {
                    _terminal = true;
                    ShowImpact(!trace.HitId.IsNone, transform.position, clock);
                }
            }

            if (_sweeps.Count == 0 && _snapshot != null)
            {
                // Attachment follows rendered victims in the owner after this sample.
                transform.position = Position(_snapshot.X, _snapshot.Y);
                Face(_snapshot.VelocityX, _snapshot.VelocityY);
                _arrow.enabled = true;
            }
            else if (_terminal && _sweeps.Count == 0) { _arrow.enabled = false; }
            _arrow.sortingOrder = CreatureMotion.SortOrder(transform.position.y) + 3;
            _impact.enabled = clock < _impactUntil;
            if (_impact.enabled)
            {
                _impact.transform.position = _impactPosition;
                _impact.sortingOrder = CreatureMotion.SortOrder(_impactPosition.y) + 4;
                _impact.color = new Color(1f, 1f, 1f, Mathf.Clamp01((_impactUntil - clock) / 0.25f));
            }
            return _sweeps.Count == 0 && _snapshot == null && !_impact.enabled;
        }

        public void Attach(Vector3 position)
        {
            if (!_effectOnly && IsEmbedded && _sweeps.Count == 0) { transform.position = position; }
        }

        private void Face(int x, int y)
        {
            if (x == 0 && y == 0) { return; }
            _arrow.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(y, x) * Mathf.Rad2Deg);
        }

        private void ShowImpact(bool hit, Vector3 position, float clock)
        {
            ImpactKey = hit ? "projectile_hit" : "projectile_miss";
            _impact.sprite = _art.Get(ImpactKey); _impactPosition = position; _impactUntil = clock + 0.25f;
        }

        public static Vector3 Position(int x, int y) => new Vector3((float)x / CombatState.UnitsPerCell, (float)y / CombatState.UnitsPerCell, 0f);
    }
}
