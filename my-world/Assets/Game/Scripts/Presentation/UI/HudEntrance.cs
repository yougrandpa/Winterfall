using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class HudEntrance : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _started, _delay, _duration;
        private bool _running;

        public void Begin(float delay, float duration = HudMotion.EntranceDuration)
        {
            _group = GetComponent<CanvasGroup>();
            // Entrance never gates input or alters layout/hit rectangles.
            _group.interactable = _group.blocksRaycasts = true;
            _duration = duration;
            _started = Time.unscaledTime;
            _delay = HudMotion.Reduced ? 0f : delay;
            _running = true;
            _group.alpha = 0f;
        }

        private void OnEnable() { HudMotion.PreferenceChanged += Complete; }
        private void OnDisable() { HudMotion.PreferenceChanged -= Complete; }
        public void Complete()
        {
            _running = false;
            if (_group != null) { _group.alpha = 1f; }
        }

        private void Update()
        {
            if (!_running) { return; }
            float progress = (Time.unscaledTime - _started - _delay) / HudMotion.Duration(_duration);
            _group.alpha = HudMotion.Ease(progress);
            if (progress >= 1f) { _running = false; }
        }
    }
}
