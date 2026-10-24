using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public static class HudTheme
    {
        public static readonly Color Paper = new Color32(23, 33, 38, 244);
        public static readonly Color PaperRaised = new Color32(31, 43, 49, 255);
        public static readonly Color Inset = new Color32(38, 51, 57, 255);
        public static readonly Color InsetDeep = new Color32(48, 64, 70, 255);
        public static readonly Color Ink = new Color32(232, 239, 238, 255);
        public static readonly Color Muted = new Color32(169, 187, 189, 255);
        public static readonly Color Faint = new Color32(133, 155, 159, 255);
        public static readonly Color Gold = new Color32(212, 154, 98, 255);
        public static readonly Color GoldSoft = new Color32(145, 112, 79, 255);
        public static readonly Color Line = new Color32(71, 91, 98, 255);
        public static readonly Color LineSoft = new Color32(46, 64, 71, 255);
        public static readonly Color Danger = new Color32(203, 112, 103, 255);
        public static readonly Color Positive = new Color32(151, 187, 167, 255);
        public static readonly Color Shadow = new Color32(0, 0, 0, 0);
        public static readonly Color[] Needs =
        {
            new Color32(176, 132, 66, 255), new Color32(72, 131, 147, 255),
            new Color32(102, 137, 86, 255), new Color32(168, 94, 82, 255),
            new Color32(124, 108, 146, 255)
        };
        private static Sprite _surface;

        public static Sprite Surface
        {
            get
            {
                if (_surface != null) { return _surface; }
                const int size = 24;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.name = "HudRoundedSurface";
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Max(3f - (x + 0.5f), x + 0.5f - 21f, 0f);
                        float dy = Mathf.Max(3f - (y + 0.5f), y + 0.5f - 21f, 0f);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(3.5f - Mathf.Sqrt(dx * dx + dy * dy))));
                    }
                }
                texture.Apply();
                _surface = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
                _surface.hideFlags = HideFlags.HideAndDontSave;
                return _surface;
            }
        }

        public static void StyleSurface(Image image, bool shadow = false)
        {
            image.sprite = Surface;
            image.type = Image.Type.Sliced;
            if (!shadow) { return; }
            var effect = image.gameObject.AddComponent<Shadow>();
            effect.effectColor = Shadow;
            effect.effectDistance = new Vector2(0f, -3f);
        }

        public static void Select(Button button, bool selected)
        {
            if (button == null) { return; }
            button.image.color = selected ? Ink : Inset;
            foreach (Text label in button.GetComponentsInChildren<Text>())
            {
                label.color = selected ? Paper : Ink;
            }
            foreach (Image image in button.GetComponentsInChildren<Image>())
            {
                if (image != button.image) { image.color = selected ? Paper : Ink; }
            }
        }

        public static Image Icon(Transform parent, string name, string key, Vector2 position, Vector2 size, Color color)
        {
            var image = UiFactory.CreateIcon(parent, name, HudIcons.Get(key), position, size);
            image.color = color;
            return image;
        }

        public static Image Icon(Transform parent, string name, string key, Color color)
        {
            var image = UiFactory.CreateIcon(parent, name, HudIcons.Get(key), Vector2.zero, new Vector2(18, 18));
            image.color = color;
            return image;
        }
    }
}
