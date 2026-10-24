using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class HudButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private Button _button;
        private HudPulse _edge;
        private RectTransform[] _content;
        private Vector2[] _positions;
        private bool _hovered, _selected, _pressed;
        private float _offset, _light;

        private void Start()
        {
            _button = GetComponent<Button>();
            _edge = HudPulse.Attach(transform, "HoverEdge", true);
            // Capture only direct text/icon children, after runtime layout has finished.
            var graphics = GetComponentsInChildren<Graphic>(true);
            int count = 0;
            foreach (var graphic in graphics)
            {
                if (graphic.transform.parent == transform && (graphic is Text || graphic.name == "Icon")) { count++; }
            }
            _content = new RectTransform[count];
            _positions = new Vector2[count];
            int index = 0;
            foreach (var graphic in graphics)
            {
                if (graphic.transform.parent != transform || (!(graphic is Text) && graphic.name != "Icon")) { continue; }
                _content[index] = graphic.rectTransform;
                _positions[index] = graphic.rectTransform.anchoredPosition;
                index++;
            }
        }

        private bool Interactive => _button != null && _button.IsActive() && _button.IsInteractable();

        private void LateUpdate()
        {
            if (_button == null) { return; }
            bool active = Interactive;
            if (!active) { _hovered = _selected = _pressed = false; }
            float targetOffset = active && _pressed && !HudMotion.Reduced ? -1f : 0f;
            float targetLight = active && (_hovered || _selected) ? 0.5f : 0f;
            if (Mathf.Approximately(_offset, targetOffset) && Mathf.Approximately(_light, targetLight)) { return; }
            _offset = Mathf.MoveTowards(_offset, targetOffset, Time.unscaledDeltaTime / HudMotion.ButtonDuration);
            _light = Mathf.MoveTowards(_light, targetLight, Time.unscaledDeltaTime / HudMotion.Duration(HudMotion.ButtonDuration));
            var tint = HudTheme.Ink; tint.a = _light;
            _edge.color = tint;
            for (int i = 0; i < _content.Length; i++)
            {
                _content[i].anchoredPosition = _positions[i] + Vector2.up * _offset;
            }
        }

        private void OnEnable() { HudMotion.PreferenceChanged += ResetMotion; }
        private void OnDisable() { HudMotion.PreferenceChanged -= ResetMotion; ResetMotion(); }
        private void ResetMotion()
        {
            _pressed = _hovered = _selected = false;
            _offset = _light = 0f;
            if (_edge != null) { _edge.color = Color.clear; }
            if (_content == null) { return; }
            for (int i = 0; i < _content.Length; i++)
            {
                if (_content[i] != null) { _content[i].anchoredPosition = _positions[i]; }
            }
        }

        public void OnPointerEnter(PointerEventData data) { _hovered = true; }
        public void OnPointerExit(PointerEventData data) { _hovered = _pressed = false; }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && Interactive) { _pressed = true; } }
        public void OnPointerUp(PointerEventData data) { _pressed = false; }
        public void OnSelect(BaseEventData data) { _selected = true; }
        public void OnDeselect(BaseEventData data) { _selected = _pressed = false; }
    }
}
