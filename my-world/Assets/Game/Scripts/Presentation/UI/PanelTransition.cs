using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class PanelTransition : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _rect;
        private Vector2 _restPosition;
        private Vector2 _offset = Vector2.right * 8f;
        private float _openDuration = HudMotion.DetailDuration;
        private float _from, _started, _duration;
        private bool _moving;
        public bool IsOpen { get; private set; }
        public float OpenDuration => _openDuration;

        public void Configure(Vector2 offset, float openDuration)
        {
            EnsureInitialized();
            _offset = offset;
            _openDuration = openDuration;
        }

        private void EnsureInitialized()
        {
            if (_group != null) { return; }
            _group = GetComponent<CanvasGroup>();
            _rect = GetComponent<RectTransform>();
            _restPosition = _rect.anchoredPosition;
        }

        public void SetOpen(bool open, bool instant = false)
        {
            EnsureInitialized();
            _group.blocksRaycasts = open;
            _group.interactable = open;
            if (IsOpen == open && !instant) { return; }
            IsOpen = open;
            if (!open)
            {
                foreach (var pulse in GetComponentsInChildren<HudPulse>(true)) { pulse.ResetFeedback(); }
                foreach (var meter in GetComponentsInChildren<HudMeterMotion>(true)) { meter.Complete(); }
            }
            if (instant)
            {
                _moving = false;
                _group.alpha = open ? 1f : 0f;
                ApplyPosition();
                return;
            }
            _from = _group.alpha;
            _started = Time.unscaledTime;
            _duration = HudMotion.Duration(open ? _openDuration : HudMotion.CloseDuration);
            _moving = true;
            ApplyPosition();
        }

        public void Complete()
        {
            EnsureInitialized();
            SetOpen(IsOpen, true);
        }

        private void OnEnable() { HudMotion.PreferenceChanged += Complete; }
        private void OnDisable() { HudMotion.PreferenceChanged -= Complete; }

        private void Update()
        {
            if (!_moving) { return; }
            float progress = (Time.unscaledTime - _started) / _duration;
            _group.alpha = Mathf.Lerp(_from, IsOpen ? 1f : 0f, HudMotion.Ease(progress));
            ApplyPosition();
            if (progress >= 1f) { _moving = false; }
        }

        private void ApplyPosition()
        {
            _rect.anchoredPosition = _restPosition + (HudMotion.Reduced ? Vector2.zero : _offset * (1f - _group.alpha));
        }
    }
}
