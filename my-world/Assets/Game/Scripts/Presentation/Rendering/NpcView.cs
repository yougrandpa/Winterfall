using MyAiWorld.App.Query;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.Rendering
{
    public sealed class NpcView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private SpriteRenderer _effect;
        public bool Visible => _renderer.enabled;
        private PastoralArtConfig _art;
        private readonly CreatureMotion _motion = new CreatureMotion();
        private int _role;
        private int _action;
        private int _health = 10000;
        private float _flashUntil;
        private SpriteRenderer _selectionRing;
        private SpriteRenderer _selectionPointer;
        public int SortingOrder => _renderer.sortingOrder;
        public Rect HitBounds => new Rect((Vector2)transform.position + new Vector2(-0.35f, -0.1f), new Vector2(0.7f, 1.2f));
        public bool SelectionVisible => _selectionRing != null && _selectionRing.enabled;
        private float _bornAt;
        public EntityId Id { get; private set; }
        public GridPos Cell => _motion.Cell;
        public int FrameIndex { get; private set; }
        public Sprite CurrentSprite => _renderer.sprite;

        public static int RoleIndex(string role)
        {
            switch (role)
            {
                case "Farmer": return 0; case "Gatherer": return 1; case "Hunter": return 2;
                case "Trader": return 3; default: return 4;
            }
        }

        public void Initialize(EntityId id, Sprite sprite, Transform parent)
        {
            Id = id;
            transform.SetParent(parent, false);
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            var collider = gameObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.7f, 1.2f);
            collider.offset = new Vector2(0f, 0.5f);
        }

        public void Configure(PastoralArtConfig art, string role, float clock, int health = 10000)
        {
            _art = art; _role = RoleIndex(role); _bornAt = clock; _health = health;
            _effect = CreateSelectionSprite("WorkEffect", "work_effect_0_0");
            _selectionRing = CreateSelectionSprite("SelectionRing", "selection_ring");
            _selectionPointer = CreateSelectionSprite("SelectionPointer", "selection_pointer");
        }
        public void SetCell(GridPos cell, bool instant, long tick)
        {
            _motion.SetCell(cell, instant, tick);
            if (instant) { transform.position = _motion.Position; }
        }
        public void ApplyAlpha(float alpha)
        {
            transform.position = _motion.Sample(alpha);
            _renderer.sortingOrder = CreatureMotion.SortOrder(transform.position.y);
            if (_selectionRing != null) { _selectionRing.sortingOrder = -7999; }
            if (_selectionPointer != null) { _selectionPointer.sortingOrder = 100; }
        }
        public void Present(NpcSnapshot snapshot, long tick, float alpha, float clock)
        {
            _renderer.enabled = snapshot.Life != LifeState.Dead;
            transform.localRotation = Quaternion.Euler(0, 0, snapshot.Life == LifeState.Downed ? 75 : 0);
            if (snapshot.Health < _health) { _flashUntil = clock + 0.25f; }
            _health = snapshot.Health;
            SetCell(snapshot.Cell, false, tick); ApplyAlpha(alpha);
            // The simulation can finish a move before its final interpolated
            // segment is displayed. Keep walking until the feet actually arrive.
            _action = (int)(_motion.Moving ? NpcActionType.Move :
                snapshot.ActionType == NpcActionType.Move ? NpcActionType.Idle : snapshot.ActionType);
            FrameIndex = Mathf.FloorToInt((clock + Id.Value * 0.137f) * (_action == 1 ? 8f : 3f)) % 4;
            _renderer.sprite = _art.Get("npc_" + _role + "_" + _motion.Direction + "_" + _action + "_" + FrameIndex);
            Color tint = clock < _flashUntil ? new Color(1f, 0.6f, 0.5f) : Color.white;
            tint.a = Mathf.Clamp01((clock - _bornAt) / 0.25f);
            _renderer.color = tint;
            bool working = !_motion.Moving && (snapshot.ActionType == NpcActionType.Work || snapshot.ActionType == NpcActionType.Gather);
            _effect.enabled = working && snapshot.Life == LifeState.Active;
            if (_effect.enabled)
            {
                int effect = snapshot.ActionLabel.Contains("Hunt") ? 1 : snapshot.ActionLabel.Contains("Rescue") ? 3 : snapshot.ActionLabel.Contains("Water") || snapshot.ActionLabel.Contains("Gather") ? 0 : snapshot.ActionLabel.Contains("Plant") || snapshot.ActionLabel.Contains("Harvest") ? 4 : 2;
                _effect.sprite = _art.Get("work_effect_" + effect + "_" + FrameIndex); _effect.sortingOrder = _renderer.sortingOrder + 1;
            }
            if (snapshot.Life == LifeState.Dead) { SetSelected(false); }
        }
        private SpriteRenderer CreateSelectionSprite(string label, string key)
        {
            var child = new GameObject(label, typeof(SpriteRenderer));
            child.transform.SetParent(transform, false);
            var renderer = child.GetComponent<SpriteRenderer>();
            renderer.sprite = _art.Get(key);
            renderer.enabled = false;
            return renderer;
        }

        public void SetSelected(bool selected)
        {
            if (_selectionRing != null) { _selectionRing.enabled = selected; }
            if (_selectionPointer != null) { _selectionPointer.enabled = selected; }
        }
    }
}
