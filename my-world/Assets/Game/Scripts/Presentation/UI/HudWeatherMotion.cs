using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    [RequireComponent(typeof(Image))]
    public sealed class HudWeatherMotion : MonoBehaviour
    {
        private Image _current;
        private readonly Image[] _previous = new Image[2];
        private readonly float[] _from = new float[2];
        private float _started, _currentFrom;
        private bool _moving;

        public void Bind(Sprite sprite, bool instant)
        {
            if (_current == null)
            {
                _current = GetComponent<Image>();
                // Three fixed layers cover sun, moon and snow without allocating on changes.
                for (int i = 0; i < _previous.Length; i++)
                {
                    _previous[i] = UiFactory.CreateIcon(transform.parent, "PreviousWeatherIcon" + i, null,
                        _current.rectTransform.anchoredPosition, _current.rectTransform.sizeDelta);
                    _previous[i].color = Color.clear;
                }
            }
            if (instant) { _current.sprite = sprite; Complete(); return; }
            if (_current.sprite == sprite) { return; }
            // Transfer the full visible composition before assigning the new target.
            float existing = 0f;
            for (int i = 0; i < _previous.Length; i++)
            {
                if (_previous[i].sprite != sprite) { continue; }
                existing += _previous[i].color.a;
                _previous[i].color = Color.clear;
            }
            for (int i = 0; i < _previous.Length; i++)
            {
                if (_previous[i].color.a > 0f) { continue; }
                _previous[i].sprite = _current.sprite;
                _previous[i].color = _current.color;
                break;
            }
            _current.sprite = sprite;
            _currentFrom = existing;
            var tint = HudTheme.Ink; tint.a = existing; _current.color = tint;
            for (int i = 0; i < _previous.Length; i++) { _from[i] = _previous[i].color.a; }
            _started = Time.unscaledTime;
            _moving = true;
        }

        private void OnEnable() { HudMotion.PreferenceChanged += Complete; }
        private void OnDisable() { HudMotion.PreferenceChanged -= Complete; Complete(); }
        public void Complete()
        {
            _moving = false;
            if (_current != null) { _current.color = HudTheme.Ink; }
            foreach (var previous in _previous) { if (previous != null) { previous.color = Color.clear; } }
        }

        private void Update()
        {
            if (!_moving) { return; }
            float progress = (Time.unscaledTime - _started) / HudMotion.Duration(HudMotion.WeatherDuration);
            float eased = HudMotion.Ease(progress);
            var tint = HudTheme.Ink; tint.a = Mathf.Lerp(_currentFrom, 1f, eased); _current.color = tint;
            for (int i = 0; i < _previous.Length; i++)
            {
                tint.a = _from[i] * (1f - eased); _previous[i].color = tint;
            }
            if (progress >= 1f) { Complete(); }
        }
    }
}
