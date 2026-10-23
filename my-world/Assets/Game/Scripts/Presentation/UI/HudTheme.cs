using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public static class HudTheme
    {
        public static readonly Color Paper = new Color32(250, 247, 235, 255);
        public static readonly Color Inset = new Color32(239, 236, 221, 255);
        public static readonly Color Ink = new Color32(39, 65, 54, 255);
        public static readonly Color Muted = new Color32(111, 120, 103, 255);
        public static readonly Color Gold = new Color32(166, 119, 60, 255);
        public static readonly Color Line = new Color32(215, 214, 193, 255);
        public static readonly Color Danger = new Color32(170, 67, 51, 255);
        public static readonly Color[] Needs =
        {
            new Color32(183, 137, 57, 255), new Color32(68, 142, 160, 255),
            new Color32(107, 145, 74, 255), new Color32(176, 93, 77, 255),
            new Color32(133, 111, 158, 255)
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
                        float dx = Mathf.Max(6f - (x + 0.5f), x + 0.5f - 18f, 0f);
                        float dy = Mathf.Max(6f - (y + 0.5f), y + 0.5f - 18f, 0f);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(6.5f - Mathf.Sqrt(dx * dx + dy * dy))));
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
            effect.effectColor = new Color(0.12f, 0.19f, 0.13f, 0.2f);
            effect.effectDistance = new Vector2(0f, -3f);
        }

        public static void Select(Button button, bool selected)
        {
            button.image.color = selected ? Ink : Inset;
            button.GetComponentInChildren<Text>().color = selected ? Paper : Ink;
        }
    }
}
