using System.Collections.Generic;
using MyAiWorld.App.Query;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    // Observation only: this graphic never samples or changes simulation state.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class NeedsHistoryGraph : MaskableGraphic
    {
        private IReadOnlyList<NeedsSample> _history;
        private static readonly Color[] SeriesColors = HudTheme.Needs;

        public void SetHistory(IReadOnlyList<NeedsSample> history)
        {
            if (_history == history)
            {
                return;
            }

            _history = history;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_history == null || _history.Count == 0)
            {
                return;
            }

            Rect rect = rectTransform.rect;
            rect.xMin += 2f;
            rect.xMax -= 2f;
            rect.yMin += 3f;
            rect.yMax -= 3f;
            Color gridColor = new Color(0.65f, 0.75f, 0.78f, 0.15f);
            for (int line = 0; line < 3; line++)
            {
                float y = rect.yMin + line * rect.height / 2f;
                AddLine(mesh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), gridColor, 1f);
            }

            long firstTick = _history[0].Tick;
            long duration = System.Math.Max(1, _history[_history.Count - 1].Tick - firstTick);
            for (int series = 0; series < SeriesColors.Length; series++)
            {
                Vector2 previous = Point(rect, _history[0], series, firstTick, duration);
                if (_history.Count == 1)
                {
                    AddLine(mesh, previous, previous + Vector2.right * 3f, SeriesColors[series], 2f);
                }

                for (int i = 1; i < _history.Count; i++)
                {
                    Vector2 current = Point(rect, _history[i], series, firstTick, duration);
                    AddLine(mesh, previous, current, SeriesColors[series], 1.5f);
                    previous = current;
                }
            }
        }

        private static Vector2 Point(Rect rect, NeedsSample sample, int series, long firstTick, long duration)
        {
            int value;
            switch (series)
            {
                case 0: value = sample.Food; break;
                case 1: value = sample.Water; break;
                case 2: value = sample.Energy; break;
                case 3: value = sample.Health; break;
                default: value = sample.Safety; break;
            }

            return new Vector2(rect.xMin + (sample.Tick - firstTick) / (float)duration * rect.width,
                rect.yMin + Mathf.Clamp01(value / 10000f) * rect.height);
        }

        private static void AddLine(VertexHelper mesh, Vector2 start, Vector2 end, Color lineColor, float width)
        {
            Vector2 normal = (end - start).normalized;
            normal = new Vector2(-normal.y, normal.x) * width / 2f;
            int index = mesh.currentVertCount;
            mesh.AddVert(start - normal, lineColor, Vector2.zero);
            mesh.AddVert(start + normal, lineColor, Vector2.zero);
            mesh.AddVert(end + normal, lineColor, Vector2.zero);
            mesh.AddVert(end - normal, lineColor, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
