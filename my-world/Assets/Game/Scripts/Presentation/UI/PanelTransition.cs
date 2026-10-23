using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class PanelTransition : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _rect;
        private Vector2 _restPosition;
        public bool IsOpen { get; private set; }

        public void SetOpen(bool open, bool instant = false)
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
                _rect = GetComponent<RectTransform>();
                _restPosition = _rect.anchoredPosition;
            }
            IsOpen = open;
            _group.blocksRaycasts = open;
            _group.interactable = open;
            if (instant) { _group.alpha = open ? 1f : 0f; ApplyPosition(); }
        }

        private void Update()
        {
            if (_group == null) { return; }
            _group.alpha = Mathf.MoveTowards(_group.alpha, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.15f);
            ApplyPosition();
        }

        private void ApplyPosition()
        {
            _rect.anchoredPosition = _restPosition + Vector2.down * (6f * (1f - _group.alpha));
        }
    }
}
