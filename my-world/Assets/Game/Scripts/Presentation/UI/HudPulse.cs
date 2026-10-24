using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    // Independent decoration keeps binding and Selectable in charge of semantic colors.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudPulse : MaskableGraphic
    {
        private float _started, _duration, _cycleStarted;
        private bool _cycle;
        private Color _pulseColor, _cycleColor;
        public bool Frame { get; set; }
        public int PulseCount { get; private set; }
        public bool IsPulsing { get; private set; }

        public static HudPulse Attach(Transform parent, string name, bool frame = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var pulse = go.AddComponent<HudPulse>();
            pulse.Frame = frame;
            pulse.raycastTarget = false;
            pulse.color = Color.clear;
            return pulse;
        }

        public void Play(Color accent, float duration)
        {
            _pulseColor = accent;
            _started = Time.unscaledTime;
            _duration = HudMotion.Duration(duration);
            IsPulsing = true;
            PulseCount++;
            Paint();
        }

        public void SetCycle(bool cycle, Color accent)
        {
            if (cycle && !_cycle) { _cycleStarted = Time.unscaledTime; }
            _cycle = cycle;
            _cycleColor = accent;
            if (!cycle && !IsPulsing) { color = Color.clear; }
        }

        public void ResetFeedback()
        {
            IsPulsing = _cycle = false;
            color = Color.clear;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            HudMotion.PreferenceChanged += OnPreferenceChanged;
        }

        protected override void OnDisable()
        {
            HudMotion.PreferenceChanged -= OnPreferenceChanged;
            ResetFeedback();
            base.OnDisable();
        }

        private void OnPreferenceChanged() { IsPulsing = false; Paint(); }

        private void Update()
        {
            if (!IsPulsing && !_cycle) { return; }
            Paint();
        }

        private void Paint()
        {
            float elapsed = Time.unscaledTime - _started;
            if (IsPulsing && elapsed >= _duration) { IsPulsing = false; }
            Color tint = IsPulsing ? _pulseColor : _cycleColor;
            tint.a = IsPulsing ? 0.8f * (1f - HudMotion.Ease(elapsed / _duration)) :
                _cycle && !HudMotion.Reduced ? 0.12f + 0.1f * (1f + Mathf.Sin((Time.unscaledTime - _cycleStarted) * Mathf.PI * 2f / HudMotion.ExhaustedPeriod)) : 0f;
            color = tint;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            AddQuad(mesh, rect.xMin, rect.yMin, rect.xMax, rect.yMin + 1f);
            if (!Frame) { return; }
            AddQuad(mesh, rect.xMin, rect.yMax - 1f, rect.xMax, rect.yMax);
            AddQuad(mesh, rect.xMin, rect.yMin + 1f, rect.xMin + 1f, rect.yMax - 1f);
            AddQuad(mesh, rect.xMax - 1f, rect.yMin + 1f, rect.xMax, rect.yMax - 1f);
        }

        private void AddQuad(VertexHelper mesh, float left, float bottom, float right, float top)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, bottom), color, Vector2.zero);
            mesh.AddVert(new Vector3(left, top), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, top), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, bottom), color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
