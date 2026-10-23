using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    // Shared uGUI primitives. Text participates in layout so long translated
    // labels and failure messages grow vertically instead of being clipped.
    internal static class HudLayout
    {
        public static RectTransform Surface(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 pivot, Vector2 position, Vector2 size, bool shadow = true)
        {
            var rect = UiFactory.CreatePanel(parent, name, min, max, pivot, position, size, HudTheme.Paper);
            HudTheme.StyleSurface(rect.GetComponent<Image>(), shadow);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = HudTheme.Line;
            outline.effectDistance = new Vector2(1, -1);
            return rect;
        }

        public static Text At(Transform parent, string name, string value, float x, float y, float width,
            float height, int size = 14, Color? color = null)
        {
            var label = UiFactory.CreateText(parent, name, value, size, TextAnchor.MiddleLeft, color ?? HudTheme.Ink);
            Place(label.rectTransform, Vector2.up, new Vector2(x, -y), new Vector2(width, Mathf.Max(height, size * 1.6f)));
            return label;
        }

        public static Button Button(Transform parent, string name, string value, float x, float y,
            float width, UnityAction action, float height = 32)
        {
            return UiFactory.CreateButton(parent, name, value, Vector2.up, Vector2.up, Vector2.up,
                new Vector2(x, -y), new Vector2(width, height), action);
        }

        public static RectTransform Stack(Transform parent, string name, float spacing = 10)
        {
            var rect = Column(parent, name, spacing);
            rect.anchorMin = Vector2.up; rect.anchorMax = Vector2.one; rect.pivot = Vector2.up;
            rect.sizeDelta = Vector2.zero;
            rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        public static ScrollRect Scroll(Transform parent, string name, float left, float top, float right, float bottom)
        {
            var rect = Rect(parent, name);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
            var viewport = UiFactory.CreatePanel(rect, "Viewport", Vector2.zero, Vector2.one, Vector2.up,
                Vector2.zero, Vector2.zero, new Color(1, 1, 1, 0.001f));
            viewport.offsetMin = Vector2.zero; viewport.offsetMax = new Vector2(-12, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Stack(viewport, "Content");
            var track = UiFactory.CreatePanel(rect, "Scrollbar", Vector2.right, Vector2.one, Vector2.one,
                Vector2.zero, new Vector2(4, 0), HudTheme.Inset);
            track.offsetMin = new Vector2(-4, 0); track.offsetMax = Vector2.zero;
            var handle = UiFactory.CreatePanel(track, "Handle", Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, Vector2.zero, HudTheme.Gold);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle; bar.targetGraphic = handle.GetComponent<Image>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28; scroll.verticalScrollbar = bar;
            return scroll;
        }

        public static Text Label(Transform parent, string name, string value, int size, Color color)
        {
            var label = Label(parent, name, value, size);
            label.color = color;
            return label;
        }

        public static void Height(Component component, float height) { Height(component.GetComponent<RectTransform>(), height); }

        public static Text MetricValue(Transform parent, string name, string title)
        {
            var row = Row(parent, name, 28);
            row.GetComponent<LayoutElement>().preferredHeight = -1;
            Label(row, "Caption", title, 14, HudTheme.Muted);
            var value = Label(row, "Value", "—"); value.alignment = TextAnchor.UpperRight;
            return value;
        }

        public static Text[] Grid(Transform parent, string name, string[] headers, int rows)
        {
            var result = new Text[headers.Length * rows];
            for (int row = -1; row < rows; row++)
            {
                var rect = UiFactory.CreatePanel(parent, name + "Row" + row, Vector2.up, Vector2.one,
                    Vector2.up, Vector2.zero, Vector2.zero, row % 2 == 0 ? HudTheme.Inset : HudTheme.Paper);
                rect.GetComponent<Image>().raycastTarget = false; Height(rect, 32);
                var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 4, 4); layout.spacing = 4;
                layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
                for (int col = 0; col < headers.Length; col++)
                {
                    var cell = Label(rect, name + "Cell" + row + "_" + col, row < 0 ? headers[col] : "—", row < 0 ? 12 : 14,
                        row < 0 ? HudTheme.Muted : HudTheme.Ink);
                    cell.alignment = col == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                    Flexible(cell.rectTransform);
                    if (row >= 0) { result[row * headers.Length + col] = cell; }
                }
            }
            return result;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static RectTransform Surface(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiFactory.CreatePanel(parent, name, anchor, anchor, anchor, position, size, HudTheme.Paper);
            HudTheme.StyleSurface(rect.GetComponent<Image>(), true);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = HudTheme.Line;
            outline.effectDistance = new Vector2(1, -1);
            return rect;
        }

        public static RectTransform Column(Transform parent, string name, float spacing = 8f, int padding = 0)
        {
            var rect = Rect(parent, name);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            return rect;
        }

        public static RectTransform Row(Transform parent, string name, float height, float spacing = 8f)
        {
            var rect = Rect(parent, name);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.spacing = spacing;
            Height(rect, height);
            return rect;
        }

        public static void Height(RectTransform rect, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
        }

        public static void Flexible(RectTransform rect)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 0;
            element.preferredWidth = 0;
            element.flexibleWidth = 1;
        }

        public static Text Label(Transform parent, string name, string value, int size = 14, bool muted = false)
        {
            var text = UiFactory.CreateText(parent, name, value, size, TextAnchor.UpperLeft, muted ? HudTheme.Muted : HudTheme.Ink);
            text.lineSpacing = 1.18f;
            return text;
        }

        public static Text FixedLabel(Transform parent, string name, string value, Vector2 position, Vector2 size,
            int fontSize = 14, bool muted = false)
        {
            var text = Label(parent, name, value, fontSize, muted);
            text.lineSpacing = 1f;
            size.y = Mathf.Max(size.y, fontSize * 1.6f);
            Place(text.rectTransform, Vector2.up, position, size);
            return text;
        }

        public static Button Button(Transform parent, string name, string label, UnityAction action)
        {
            var button = UiFactory.CreateButton(parent, name, label, Vector2.up, Vector2.up, Vector2.up,
                Vector2.zero, new Vector2(100, 36), action);
            Height(button.GetComponent<RectTransform>(), 36);
            Flexible(button.GetComponent<RectTransform>());
            return button;
        }

        public static Button FixedButton(Transform parent, string name, string label, Vector2 position, Vector2 size, UnityAction action)
        {
            var button = Button(parent, name, label, action);
            Place(button.GetComponent<RectTransform>(), Vector2.up, position, size);
            return button;
        }

        public static void Section(Transform parent, string title)
        {
            var row = Row(parent, "Section_" + title, 36f);
            var label = Label(row, "Heading", title, 20);
            label.color = HudTheme.Gold;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.LowerLeft;
        }

        public static RectTransform Card(Transform parent, string name)
        {
            var card = Column(parent, name, 5f, 12);
            var image = card.gameObject.AddComponent<Image>();
            image.color = HudTheme.Inset;
            image.raycastTarget = false;
            HudTheme.StyleSurface(image);
            return card;
        }

        public static RectTransform Scroll(Transform parent, string name, float top, float bottom, out ScrollRect scroll)
        {
            scroll = Scroll(parent, name, 16, top, 16, bottom);
            return scroll.content;
        }

        public static Text Metric(Transform parent, string label, string initial = "—")
        {
            var row = Row(parent, "Metric_" + label, 30f);
            row.GetComponent<LayoutElement>().preferredHeight = -1;
            var caption = Label(row, "Caption", label, 14, true);
            caption.alignment = TextAnchor.MiddleLeft;
            Flexible(caption.rectTransform);
            var value = Label(row, "Value", initial);
            value.alignment = TextAnchor.MiddleRight;
            Flexible(value.rectTransform);
            return value;
        }
    }

    internal sealed class HudEntries
    {
        private readonly RectTransform _root;
        private readonly List<Text> _titles = new List<Text>();
        private readonly List<Text> _bodies = new List<Text>();
        private readonly List<RectTransform> _cards = new List<RectTransform>();
        private int _count;
        private readonly StringBuilder _text = new StringBuilder();
        public string Text => _text.ToString();

        public HudEntries(Transform parent) : this(parent, "Entries") { }

        public void Set(IReadOnlyList<string> entries)
        {
            Begin();
            foreach (string entry in entries)
            {
                int newline = entry.IndexOf('\n');
                Add(newline < 0 ? "" : entry.Substring(0, newline), newline < 0 ? entry : entry.Substring(newline + 1));
            }
            End();
        }

        public HudEntries(Transform parent, string name)
        {
            _root = HudLayout.Column(parent, name);
        }

        public void Begin()
        {
            _count = 0;
            _text.Clear();
        }

        public void Add(string title, string body)
        {
            if (_count == _cards.Count)
            {
                var card = HudLayout.Card(_root, "Entry" + _count);
                var heading = HudLayout.Label(card, "DateOrTitle", "", 12, true);
                heading.color = HudTheme.Gold;
                _titles.Add(heading);
                _bodies.Add(HudLayout.Label(card, "Description", ""));
                _cards.Add(card);
            }
            var current = _cards[_count];
            if (!current.gameObject.activeSelf) { current.gameObject.SetActive(true); }
            _titles[_count].text = title;
            _bodies[_count].text = body;
            _text.Append(title).Append('\n').Append(body).Append('\n');
            _count++;
        }

        public void End()
        {
            for (int i = _count; i < _cards.Count; i++) { _cards[i].gameObject.SetActive(false); }
        }
    }
}
