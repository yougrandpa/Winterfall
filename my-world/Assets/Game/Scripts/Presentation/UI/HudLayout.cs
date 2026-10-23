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
            outline.effectColor = HudTheme.LineSoft;
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

        public static RectTransform Stack(Transform parent, string name, float spacing = 6)
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
            viewport.offsetMin = Vector2.zero; viewport.offsetMax = new Vector2(-10, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Stack(viewport, "Content");
            var track = UiFactory.CreatePanel(rect, "Scrollbar", Vector2.right, Vector2.one, Vector2.one,
                Vector2.zero, new Vector2(3, 0), HudTheme.LineSoft);
            track.offsetMin = new Vector2(-3, 0); track.offsetMax = Vector2.zero;
            var handle = UiFactory.CreatePanel(track, "Handle", Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, Vector2.zero, HudTheme.GoldSoft);
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
                rect.GetComponent<Image>().raycastTarget = false;
                Height(rect, 32);
                var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 4, 4); layout.spacing = 4;
                layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
                for (int col = 0; col < headers.Length; col++)
                {
                    var cell = Label(rect, name + "Cell" + row + "_" + col, row < 0 ? headers[col] : "—", row < 0 ? 11 : 13,
                        row < 0 ? HudTheme.Faint : HudTheme.Ink);
                    cell.alignment = col == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                    if (row < 0) { cell.fontStyle = FontStyle.Bold; }
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
            outline.effectColor = HudTheme.LineSoft;
            outline.effectDistance = new Vector2(1, -1);
            return rect;
        }

        public static RectTransform Inset(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var rect = UiFactory.CreatePanel(parent, name, Vector2.up, Vector2.up, Vector2.up, position, size, color);
            HudTheme.StyleSurface(rect.GetComponent<Image>());
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
            element.flexibleHeight = 0;
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

        public static Button IconButton(Transform parent, string name, string icon, string label, Vector2 position,
            Vector2 size, UnityAction action, out Image iconImage)
        {
            var button = UiFactory.CreateButton(parent, name, label, Vector2.up, Vector2.up, Vector2.up,
                position, size, action);
            var text = button.GetComponentInChildren<Text>();
            text.alignment = label.Length == 0 ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (label.Length == 0)
            {
                text.text = "";
                iconImage = HudTheme.Icon(button.transform, "Icon", icon,
                    new Vector2((size.x - 18f) * 0.5f, -(size.y - 18f) * 0.5f), new Vector2(18, 18), HudTheme.Ink);
            }
            else
            {
                text.rectTransform.offsetMin = new Vector2(34, 0);
                text.rectTransform.offsetMax = new Vector2(-6, 0);
                iconImage = HudTheme.Icon(button.transform, "Icon", icon,
                    new Vector2(10, -(size.y - 18f) * 0.5f), new Vector2(18, 18), HudTheme.Ink);
            }
            return button;
        }

        public static Button CloseButton(Transform parent, string name, Vector2 position, UnityAction action)
        {
            Image ignored;
            return IconButton(parent, name, "close", "", position, new Vector2(30, 30), action, out ignored);
        }

        public static RectTransform Section(Transform parent, string title)
        {
            var row = Rect(parent, "Section_" + title);
            Height(row, 28);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var mark = Rect(row, "Mark");
            var markImage = mark.gameObject.AddComponent<Image>();
            markImage.color = HudTheme.Gold;
            markImage.raycastTarget = false;
            HudTheme.StyleSurface(markImage);
            var markLayout = mark.gameObject.AddComponent<LayoutElement>();
            markLayout.minWidth = markLayout.preferredWidth = 4;
            markLayout.minHeight = markLayout.preferredHeight = 4;
            markLayout.flexibleWidth = markLayout.flexibleHeight = 0;

            var label = Label(row, "Heading", title, 12);
            label.color = HudTheme.Gold;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.minWidth = 0;
            labelLayout.preferredWidth = -1;
            labelLayout.flexibleWidth = 0;

            var rule = Rect(row, "Rule");
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = HudTheme.LineSoft;
            ruleImage.raycastTarget = false;
            var ruleLayout = rule.gameObject.AddComponent<LayoutElement>();
            ruleLayout.minHeight = ruleLayout.preferredHeight = 1;
            ruleLayout.flexibleWidth = 1;
            ruleLayout.flexibleHeight = 0;
            return row;
        }

        public static RectTransform Card(Transform parent, string name)
        {
            var card = Column(parent, name, 6f, 12);
            var image = card.gameObject.AddComponent<Image>();
            image.color = HudTheme.Inset;
            image.raycastTarget = false;
            HudTheme.StyleSurface(image);
            AddAccent(card, HudTheme.Gold);
            return card;
        }

        public static void AddAccent(RectTransform card, Color color)
        {
            var rail = new GameObject("Accent", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rail.SetParent(card, false);
            rail.anchorMin = new Vector2(0, 0); rail.anchorMax = new Vector2(0, 1);
            rail.pivot = new Vector2(0, 0.5f);
            rail.anchoredPosition = Vector2.zero;
            rail.sizeDelta = new Vector2(3, 0);
            rail.GetComponent<Image>().color = color;
            rail.GetComponent<Image>().raycastTarget = false;
            var layout = rail.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        public static Text StatCard(Transform parent, string name, string caption, string icon, Color accent, out Image iconImage)
        {
            var card = Rect(parent, name);
            Flexible(card);
            Height(card, 66);
            var image = card.gameObject.AddComponent<Image>();
            image.color = HudTheme.Inset;
            image.raycastTarget = false;
            HudTheme.StyleSurface(image);
            iconImage = HudTheme.Icon(card, "Icon", icon, new Vector2(10, -10), new Vector2(16, 16), accent);
            var title = FixedLabel(card, "Caption", caption, new Vector2(32, -9), new Vector2(60, 18), 11, true);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var value = UiFactory.CreateText(card, "Value", "—", 20, TextAnchor.MiddleLeft, HudTheme.Ink);
            value.rectTransform.anchorMin = new Vector2(0, 0);
            value.rectTransform.anchorMax = new Vector2(1, 1);
            value.rectTransform.offsetMin = new Vector2(10, 4);
            value.rectTransform.offsetMax = new Vector2(-6, -28);
            value.fontStyle = FontStyle.Bold;
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            return value;
        }

        public static Text FactCell(Transform parent, string name, string caption, string icon, Color iconColor, out Image iconImage)
        {
            var cell = Rect(parent, "Fact_" + name);
            Flexible(cell);
            Height(cell, 27);
            iconImage = HudTheme.Icon(cell, "Icon", icon, new Vector2(0, -7), new Vector2(14, 14), iconColor);
            var title = FixedLabel(cell, "Caption", caption, new Vector2(20, -5), new Vector2(86, 18), 12, true);
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var value = UiFactory.CreateText(cell, "Value", "—", 12, TextAnchor.MiddleRight, HudTheme.Ink);
            value.rectTransform.anchorMin = new Vector2(0, 0);
            value.rectTransform.anchorMax = new Vector2(1, 1);
            value.rectTransform.offsetMin = new Vector2(96, 0);
            value.rectTransform.offsetMax = new Vector2(0, 0);
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            return value;
        }

        public static Image Meter(Transform parent, string name, float height, Color trackColor, Color fillColor)
        {
            var track = UiFactory.CreatePanel(parent, name, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),
                Vector2.zero, new Vector2(0, height), trackColor);
            var trackImage = track.GetComponent<Image>();
            HudTheme.StyleSurface(trackImage);
            trackImage.raycastTarget = false;
            var fill = UiFactory.CreatePanel(track, "Fill", Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, Vector2.zero, fillColor);
            var fillImage = fill.GetComponent<Image>();
            HudTheme.StyleSurface(fillImage);
            fillImage.raycastTarget = false;
            return fillImage;
        }

        public static void SetMeter(Image fill, float value)
        {
            if (fill == null) { return; }
            var rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static void Divider(Transform parent, float height = 1f)
        {
            var rect = UiFactory.CreatePanel(parent, "Divider", Vector2.up, Vector2.one, Vector2.up,
                Vector2.zero, new Vector2(0, height), HudTheme.LineSoft);
            rect.GetComponent<Image>().raycastTarget = false;
            Height(rect, height);
        }

        public static void KeyRow(Transform parent, string key, string caption)
        {
            var row = Rect(parent, "Key_" + key);
            Height(row, 28);
            var cap = UiFactory.CreatePanel(row, "Keycap", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(0, -3), new Vector2(88, 22), HudTheme.Ink);
            HudTheme.StyleSurface(cap.GetComponent<Image>());
            cap.GetComponent<Image>().raycastTarget = false;
            var capText = UiFactory.CreateText(cap, "Text", key, 10, TextAnchor.MiddleCenter, HudTheme.Paper);
            capText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var label = FixedLabel(row, "Action", caption, new Vector2(100, -3), new Vector2(188, 22), 12);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        public static RectTransform Scroll(Transform parent, string name, float top, float bottom, out ScrollRect scroll)
        {
            scroll = Scroll(parent, name, 16, top, 16, bottom);
            return scroll.content;
        }

        public static Text Metric(Transform parent, string label, string initial = "—")
        {
            var row = Row(parent, "Metric_" + label, 28f, 8f);
            row.GetComponent<LayoutElement>().preferredHeight = -1;
            var caption = Label(row, "Caption", label, 13, true);
            caption.alignment = TextAnchor.MiddleLeft;
            Flexible(caption.rectTransform);
            var value = Label(row, "Value", initial, 13);
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
        private readonly List<Image> _rails = new List<Image>();
        private readonly List<RectTransform> _progressGroups = new List<RectTransform>();
        private readonly List<Image> _progressFills = new List<Image>();
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

        public void Add(string title, string body, Color? accent = null, float progress = -1f, Color? titleColor = null)
        {
            if (_count == _cards.Count)
            {
                var card = HudLayout.Card(_root, "Entry" + _count);
                var heading = HudLayout.Label(card, "DateOrTitle", "", 11, true);
                heading.color = HudTheme.Gold;
                heading.fontStyle = FontStyle.Bold;
                heading.horizontalOverflow = HorizontalWrapMode.Wrap;
                var bodyText = HudLayout.Label(card, "Description", "");
                var progressGroup = HudLayout.Rect(card, "Progress");
                HudLayout.Height(progressGroup, 6);
                var fill = HudLayout.Meter(progressGroup, "Track", 6, HudTheme.InsetDeep, HudTheme.Gold);
                _titles.Add(heading);
                _bodies.Add(bodyText);
                _cards.Add(card);
                _rails.Add(card.Find("Accent").GetComponent<Image>());
                _progressGroups.Add(progressGroup);
                _progressFills.Add(fill);
            }
            var current = _cards[_count];
            if (!current.gameObject.activeSelf) { current.gameObject.SetActive(true); }
            var titleText = _titles[_count];
            titleText.text = title;
            titleText.color = titleColor ?? HudTheme.Gold;
            _bodies[_count].text = body;
            _rails[_count].color = accent ?? HudTheme.Gold;
            bool showProgress = progress >= 0f;
            _progressGroups[_count].gameObject.SetActive(showProgress);
            if (showProgress) { HudLayout.SetMeter(_progressFills[_count], progress); }
            _text.Append(title).Append('\n').Append(body).Append('\n');
            _count++;
        }

        public void End()
        {
            for (int i = _count; i < _cards.Count; i++) { _cards[i].gameObject.SetActive(false); }
        }
    }
}
