using MyAiWorld.App.Query;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.Rendering
{
    public sealed class AnimalView : MonoBehaviour
    {
        private readonly CreatureMotion _motion = new CreatureMotion();
        private SpriteRenderer _renderer;
        private PastoralArtConfig _art;
        private AnimalSnapshot _snapshot;
        private int _health;
        private float _bornAt;
        private float _flashUntil;
        private float _hurtFrom;
        private float _deathAt;
        private float _attackUntil;
        private float _attackFrom;
        private long _tick;
        private int _eventDirection;
        public bool IsDespawning { get; private set; }
        public bool IsAttacking { get; private set; }
        public int FacingDirection { get; private set; }
        public string SpriteKey { get; private set; }
        public EntityId Id => _snapshot.Id;
        public GridPos Cell => _motion.Cell;
        public bool IsDying { get; private set; }
        private SpriteRenderer _selection;
        public Rect HitBounds => new Rect((Vector2)transform.position + new Vector2(-0.45f, -0.15f), new Vector2(0.9f, 0.9f));
        public int SortingOrder => _renderer.sortingOrder;
        public void SetSelected(bool selected)
        {
            if (_selection == null && !selected) { return; }
            if (_selection == null)
            {
                var go = new GameObject("HuntTarget"); go.transform.SetParent(transform, false);
                _selection = go.AddComponent<SpriteRenderer>(); _selection.sprite = _art.Get("selection_ring");
                _selection.sortingOrder = -7998; _selection.color = new Color(1f, 0.6f, 0.22f);
            }
            _selection.enabled = selected;
        }
        public int FrameIndex { get; private set; }
        public Sprite CurrentSprite => _renderer.sprite;
        public float Opacity => _renderer.color.a;
        public float PlaybackRate => _snapshot.Fleeing ? 14f : 7f;

        public void Initialize(AnimalSnapshot snapshot, PastoralArtConfig art, long tick, float clock, bool initial)
        {
            _tick = tick; _snapshot = snapshot; _art = art; _health = snapshot.Health;
            _bornAt = initial ? clock - 1 : clock;
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _motion.SetCell(snapshot.Cell, true, tick);
            transform.position = _motion.Position;
        }
        public void Receive(AnimalSnapshot snapshot, long tick, float clock)
        {
            if (snapshot.Health < _health) { _hurtFrom = clock; _flashUntil = clock + 0.25f; }
            _tick = tick; _health = snapshot.Health; _snapshot = snapshot;
            _motion.SetCell(snapshot.Cell, false, tick);
        }
        public void BeginDeath(float clock)
        {
            if (IsDying) { return; }
            IsDying = true; _deathAt = clock;
            if (_selection != null) { _selection.enabled = false; }
        }
        public void BeginDespawn(float clock)
        {
            if (IsDespawning) { return; }
            IsDespawning = true; _deathAt = clock;
            // Migration is removal, never a hurt/death animation at an invalid cell.
            _renderer.enabled = false;
        }

        public void ReceiveCombatEvent(CombatVisualEvent combatEvent, int facingX, int facingY, float clock)
        {
            if (combatEvent.State == CombatEventKind.Contact && combatEvent.HitId == Id && combatEvent.Damage > 0)
            {
                _hurtFrom = clock; _flashUntil = clock + 0.25f;
            }
            if (combatEvent.Shooter == Id && combatEvent.ProjectileId == 0 &&
                (combatEvent.State == CombatEventKind.Started || combatEvent.State == CombatEventKind.Released || combatEvent.State == CombatEventKind.Contact))
            {
                _attackFrom = clock; _attackUntil = clock + 0.25f;
                _motion.Face(facingX, facingY);
                _eventDirection = _motion.Direction;
            }
        }

        public bool Present(float alpha, float clock, AttackSnapshot attack = null)
        {
            if (IsDespawning) { return true; }
            IsAttacking = !IsDying && (attack != null && attack.Phase != CombatAttackPhase.Cooldown || clock < _attackUntil);
            if (attack != null && IsAttacking) { _motion.Face(attack.FacingX, attack.FacingY); }
            FacingDirection = clock < _attackUntil ? _eventDirection : _motion.Direction;
            if (!IsDying) { transform.position = _motion.Sample(IsAttacking ? 1f : alpha); }
            _renderer.sortingOrder = CreatureMotion.SortOrder(transform.position.y) + 1;
            int move = _motion.Moving && !IsDying && !IsAttacking ? 1 : 0;
            FrameIndex = IsAttacking ? attack != null ? CombatPose.Frame(attack, _tick, alpha) :
                Mathf.Clamp(Mathf.FloorToInt((clock - _attackFrom) * 16f), 0, 3) :
                Mathf.FloorToInt((clock + Id.Value * 0.11f) * (move == 1 ? PlaybackRate : 2f)) % 4;
            SpriteKey = IsAttacking ? "animal_attack_" + (int)_snapshot.Species + "_" + FacingDirection + "_" + FrameIndex :
                "animal_" + (int)_snapshot.Species + "_" + FacingDirection + "_" + move + "_" + FrameIndex;
            if (attack != null && attack.Kind == CombatAttackKind.Charge && IsAttacking)
            {
                int stage = attack.Phase == CombatAttackPhase.Preparing ? 0 : attack.ChargeRemaining > 0 ? 1 : 2;
                SpriteKey = "animal_charge_" + FacingDirection + "_" + stage + "_" + FrameIndex;
            }
            else if (clock < _flashUntil && !IsDying)
            {
                FrameIndex = Mathf.Clamp(Mathf.FloorToInt((clock - _hurtFrom) * 16f), 0, 3);
                SpriteKey = "animal_hurt_" + (int)_snapshot.Species + "_" + FacingDirection + "_" + FrameIndex;
            }
            _renderer.sprite = _art.Get(SpriteKey);
            Color color = clock < _flashUntil ? new Color(1f, 0.55f, 0.48f) : Color.white;
            color.a = IsDying ? 1f - Mathf.Clamp01((clock - _deathAt) / 0.3f) : Mathf.Clamp01((clock - _bornAt) / 0.3f);
            _renderer.color = color;
            return IsDying && color.a <= 0;
        }
    }
}
