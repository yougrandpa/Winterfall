using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    public sealed class HudMeterMotion : MonoBehaviour
    {
        private RectTransform _rect;
        private float _from, _target, _started;
        private bool _bound, _moving;
        public float Target => _target;

        public void SetTarget(float value, bool instant = false)
        {
            if (_rect == null) { _rect = GetComponent<RectTransform>(); }
            value = Mathf.Clamp01(value);
            if (instant || !_bound || HudMotion.Reduced || !Visible())
            {
                _bound = true; _moving = false; _target = value;
                Apply(value);
                return;
            }
            if (Mathf.Approximately(value, _target)) { return; }
            _from = _rect.anchorMax.x;
            _target = value;
            _started = Time.unscaledTime;
            _moving = true;
        }

        private bool Visible()
        {
            var panel = GetComponentInParent<PanelTransition>();
            return gameObject.activeInHierarchy && (panel == null || panel.IsOpen);
        }

        private void OnEnable() { HudMotion.PreferenceChanged += Complete; }
        private void OnDisable() { HudMotion.PreferenceChanged -= Complete; Complete(); }
        public void Complete()
        {
            if (!_bound) { return; }
            _moving = false;
            Apply(_target);
        }

        private void Update()
        {
            if (!_moving) { return; }
            float progress = (Time.unscaledTime - _started) / HudMotion.MeterDuration;
            Apply(Mathf.Lerp(_from, _target, HudMotion.Ease(progress)));
            if (progress >= 1f) { _moving = false; }
        }

        private void Apply(float value)
        {
            if (_rect == null) { return; }
            _rect.anchorMax = new Vector2(value, 1f);
            _rect.offsetMin = _rect.offsetMax = Vector2.zero;
        }
    }
}
