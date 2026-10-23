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
        private float _combatUntil;
        private int _eventStage = -1;
        private int _eventDirection;
        public int CombatStage { get; private set; } = -1;
        public int FacingDirection { get; private set; }
        public string SpriteKey { get; private set; }
        private SpriteRenderer _selectionRing;
        private SpriteRenderer _selectionPointer;
        private SpriteRenderer _silhouette;
        private SpriteRenderer _alert;
        private int _look;
        public int SortingOrder => _renderer.sortingOrder;
        public Rect HitBounds => new Rect((Vector2)transform.position + new Vector2(-0.35f, -0.1f), new Vector2(0.7f, 1.2f));
        public bool SelectionVisible => _selectionRing != null && _selectionRing.enabled;
        public bool SilhouetteVisible => _silhouette != null && _silhouette.enabled;
        public bool AlertVisible => _alert != null && _alert.enabled;
        public int Look => _look;
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

        // Twenty starting villagers cycle role then look, so every (role, look)
        // pair is unique. Look derives only from the entity id and never drifts.
        public static int LookIndex(EntityId id)
        {
            if (id.Value <= 0) { return 0; }
            return ((id.Value - 1) / 5) % 4;
        }

        public void Initialize(EntityId id, Sprite sprite, Transform parent, int look = 0)
        {
            Id = id;
            _look = look;
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
            _silhouette = CreateSelectionSprite("OcclusionSilhouette", "npc_0_0_0_0_0");
            _silhouette.sprite = _renderer.sprite;
            _silhouette.sortingOrder = 200;
            _silhouette.color = new Color(1f, 0.86f, 0.6f, 0.55f);
            _alert = CreateSelectionSprite("DangerAlert", "alert_low");
            _alert.transform.localPosition = new Vector3(0.36f, 1.28f, 0f);
            _alert.transform.localScale = Vector3.one * 0.5f;
            _alert.sortingOrder = 220;
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
            if (_selectionPointer != null) { _selectionPointer.sortingOrder = 210; }
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
            AttackSnapshot attack = snapshot.Attack;
            CombatStage = -1;
            if (attack != null && attack.Phase != CombatAttackPhase.Cooldown)
            {
                _motion.Face(attack.FacingX, attack.FacingY);
                CombatStage = attack.Kind == CombatAttackKind.Bow ?
                    (attack.Phase == CombatAttackPhase.Preparing ? 0 : 1) :
                    (attack.Phase == CombatAttackPhase.Preparing ? 2 : 3);
            }
            FacingDirection = _motion.Direction;
            if (clock < _combatUntil)
            {
                CombatStage = _eventStage; FacingDirection = _eventDirection;
            }
            if (CombatStage >= 0 && snapshot.Life == LifeState.Active)
            {
                // Combat is stationary and has a dedicated atlas; no old action-index insertion.
                ApplyAlpha(1f);
            }
            else { CombatStage = -1; }
            FrameIndex = Mathf.FloorToInt((clock + Id.Value * 0.137f) * (CombatStage >= 0 ? 12f : _action == 1 ? 8f : 3f)) % 4;
            SpriteKey = CombatStage >= 0 ? "npc_attack_" + _role + "_" + _look + "_" + FacingDirection + "_" + CombatStage + "_" + FrameIndex :
                "npc_" + _role + "_" + _look + "_" + FacingDirection + "_" + _action + "_" + FrameIndex;
            // A missing generated key stays visible to diagnostics (null), not a fake fallback attack.
            _renderer.sprite = _art.Get(SpriteKey);
            Color tint = clock < _flashUntil ? new Color(1f, 0.6f, 0.5f) : Color.white;
            tint.a = Mathf.Clamp01((clock - _bornAt) / 0.25f);
            _renderer.color = tint;
            UpdateSilhouette(tint);
            UpdateAlert(snapshot, clock);
            bool working = CombatStage < 0 && !_motion.Moving && (snapshot.ActionType == NpcActionType.Work || snapshot.ActionType == NpcActionType.Gather);
            _effect.enabled = working && snapshot.Life == LifeState.Active;
            if (_effect.enabled)
            {
                int effect = snapshot.ActionLabel.Contains("Hunt") ? 1 : snapshot.ActionLabel.Contains("Rescue") ? 3 : snapshot.ActionLabel.Contains("Water") || snapshot.ActionLabel.Contains("Gather") ? 0 : snapshot.ActionLabel.Contains("Plant") || snapshot.ActionLabel.Contains("Harvest") ? 4 : 2;
                _effect.sprite = _art.Get("work_effect_" + effect + "_" + FrameIndex); _effect.sortingOrder = _renderer.sortingOrder + 1;
            }
            if (snapshot.Life == LifeState.Dead) { SetSelected(false); }
        }
        public void ReceiveCombatEvent(CombatVisualEvent combatEvent, CombatAttackKind kind, int facingX, int facingY, float clock)
        {
            if (combatEvent.State == CombatEventKind.Contact && combatEvent.HitId == Id && combatEvent.Damage > 0)
            {
                _flashUntil = clock + 0.25f; _eventStage = 4; _eventDirection = _motion.Direction;
            }
            else if (combatEvent.Shooter == Id && (combatEvent.State == CombatEventKind.Started ||
                combatEvent.State == CombatEventKind.Released || combatEvent.State == CombatEventKind.Contact && combatEvent.ProjectileId == 0))
            {
                if (clock < _combatUntil && _eventStage == 4) { return; }
                bool preparing = combatEvent.State == CombatEventKind.Started;
                _eventStage = kind == CombatAttackKind.Bow ? (preparing ? 0 : 1) : (preparing ? 2 : 3);
                _motion.Face(facingX, facingY);
                _eventDirection = _motion.Direction;
            }
            else { return; }
            _combatUntil = clock + 0.25f;
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

        public void SetSilhouetteVisible(bool visible)
        {
            if (_silhouette == null) { return; }
            _silhouette.enabled = visible && _renderer.enabled;
            if (_silhouette.enabled) { _silhouette.sprite = _renderer.sprite; }
        }

        private void UpdateSilhouette(Color tint)
        {
            if (_silhouette == null || !_silhouette.enabled) { return; }
            _silhouette.sprite = _renderer.sprite;
            _silhouette.sortingOrder = 200;
            _silhouette.color = new Color(1f, 0.86f, 0.6f, 0.55f * tint.a);
        }

        private void UpdateAlert(NpcSnapshot snapshot, float clock)
        {
            if (_alert == null) { return; }
            bool dead = snapshot.Life == LifeState.Dead;
            bool down = snapshot.Life == LifeState.Downed;
            bool low = !down && snapshot.Health < 2500;
            _alert.enabled = !dead && (down || low);
            if (!_alert.enabled) { return; }
            _alert.sprite = _art.Get(down ? "alert_down" : "alert_low");
            _alert.sortingOrder = 220;
            float pulse = 1f + Mathf.Sin(clock * 4.2f) * 0.08f;
            _alert.transform.localScale = Vector3.one * (0.5f * pulse);
            _alert.color = new Color(1f, 1f, 1f, 0.9f);
        }
    }
}
