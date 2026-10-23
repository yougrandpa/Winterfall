using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyAiWorld.Presentation.Rendering
{
    [CreateAssetMenu(menuName = "My AI World/Pastoral Art")]
    public sealed class PastoralArtConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public Sprite sprite;
        }

        public int pixelsPerUnit = 32;
        public int visualSeed = 741103;
        public Color panel = new Color32(248, 241, 218, 250);
        public Color ink = new Color32(40, 65, 49, 255);
        public Color muted = new Color32(100, 115, 88, 255);
        public Color accent = new Color32(91, 129, 79, 255);
        public Font chineseFont;
        public Entry[] sprites = Array.Empty<Entry>();
        private Dictionary<string, Sprite> _lookup;

        public static PastoralArtConfig Load()
        {
            var art = Resources.Load<PastoralArtConfig>("Pastoral/PastoralArt");
            if (art == null) { throw new InvalidOperationException("Generate Pastoral Art before starting the world."); }
            return art;
        }

        public Sprite Get(string key)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                foreach (Entry entry in sprites) { _lookup.Add(entry.key, entry.sprite); }
            }
            Sprite sprite;
            return _lookup.TryGetValue(key, out sprite) ? sprite : null;
        }

        public int Variant(int x, int y, int count)
        {
            unchecked
            {
                uint hash = (uint)(x * 374761393 + y * 668265263 + visualSeed);
                hash = (hash ^ (hash >> 13)) * 1274126177;
                return (int)((hash ^ (hash >> 16)) % (uint)count);
            }
        }
    }
}
