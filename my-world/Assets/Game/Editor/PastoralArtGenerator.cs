using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyAiWorld.Presentation.Rendering;
using UnityEditor;
using UnityEngine;

namespace MyAiWorld.EditorTools
{
    // Integer raster drawing is the editable source. No UnityEngine.Random or simulation RNG.
    public static class PastoralArtGenerator
    {
        private const string Root = "Assets/Game/Resources/Pastoral/";
        private static readonly Color32 Outline = C(47, 63, 48);
        private static readonly Color32 Green = C(93, 139, 73);
        private static readonly Color32 Light = C(165, 192, 102);
        private static readonly Color32 Soil = C(161, 119, 77);
        private static readonly Color32 Cream = C(243, 224, 178);
        private static readonly List<SpriteMetaData> Slices = new List<SpriteMetaData>();
        private static Color32[] _atlas;
        private static int _ox;
        private static int _oy;
        private const int Width = 1024;
        private const int Height = 4096;

        [MenuItem("Tools/My AI World/Art/Generate Pastoral Art")]
        public static void Generate()
        {
            Directory.CreateDirectory(Root);
            Slices.Clear();
            _atlas = new Color32[Width * Height];
            for (int type = 0; type < 8; type++)
            for (int variant = 0; variant < 4; variant++)
            {
                Begin("ground_" + type + "_" + variant, 32, true);
                Terrain(type, variant);
                PadEdges();
            }
            for (int type = 0; type < 3; type++)
            for (int mask = 0; mask < 16; mask++)
            {
                Begin("edge_" + type + "_" + mask, 32, true);
                Edge(type, mask);
                PadEdges();
            }
            for (int variant = 0; variant < 3; variant++)
            {
                Begin("tree_" + variant); Prop("tree", variant);
            }
            for (int variant = 0; variant < 2; variant++)
            {
                Begin("rock_" + variant); Prop("rock", variant);
            }
            for (int variant = 0; variant < 2; variant++)
            {
                Begin("berries_" + variant); Prop("berries", variant);
            }
            foreach (string prop in new[] { "house", "well", "berries_empty", "stock", "flower", "tuft", "pebbles" })
            {
                Begin(prop); Prop(prop);
            }
            for (int stage = 0; stage < 4; stage++)
            for (int dry = 0; dry < 2; dry++)
            {
                Begin("crop_" + stage + "_" + dry, 32, true);
                Crop(stage, dry == 1);
                PadEdges();
            }
            for (int role = 0; role < 5; role++)
            for (int direction = 0; direction < 4; direction++)
            for (int action = 0; action < 7; action++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("npc_" + role + "_" + direction + "_" + action + "_" + frame);
                Person(role, direction, action, frame);
            }
            for (int species = 0; species < 2; species++)
            for (int direction = 0; direction < 4; direction++)
            for (int move = 0; move < 2; move++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_" + species + "_" + direction + "_" + move + "_" + frame);
                Animal(species, direction, move == 1, frame);
            }
            for (int icon = 0; icon < 6; icon++)
            {
                Begin("icon_" + icon, 32, true);
                Icon(icon);
                PadEdges();
            }
            // Append new slices so existing sprite names and atlas slots stay stable.
            for (int variant = 0; variant < 3; variant++)
            {
                Begin("cottage_" + variant); Cottage(variant);
            }
            Begin("reeds"); Reeds();
            Begin("lilies"); Lilies();
            Begin("selection_ring");
            E(32, 8, 15, 6, C(42, 65, 57, 210));
            E(32, 9, 14, 5, C(255, 225, 142));
            E(32, 9, 11, 3, C(0, 0, 0, 0));
            Begin("selection_pointer");
            for (int y = 0; y < 5; y++) { R(32 - y, 51 + y, y * 2 + 1, 1, C(255, 225, 142)); }
            for (int variant = 0; variant < 4; variant++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("water_" + variant + "_" + frame, 32, true);
                Water(variant, frame); PadEdges();
            }
            // Survival additions are appended to preserve existing atlas slots and sprite identities.
            for (int direction = 0; direction < 4; direction++)
            for (int move = 0; move < 2; move++)
            for (int frame = 0; frame < 4; frame++)
            { Begin("animal_2_" + direction + "_" + move + "_" + frame); Deer(direction, move == 1, frame); }
            for (int crop = 2; crop <= 3; crop++)
            for (int stage = 0; stage < 4; stage++)
            for (int dry = 0; dry < 2; dry++)
            { Begin("crop_type_" + crop + "_" + stage + "_" + dry, 32, true); FieldCrop(crop, stage, dry == 1); PadEdges(); }
            for (int facility = 0; facility < 5; facility++)
            for (int built = 0; built < 2; built++)
            { Begin("facility_" + facility + "_" + built); Facility(facility, built == 1); }
            for (int item = 3; item <= 7; item++)
            { Begin("resource_" + item); Resource(item); }
            Begin("ground_stock"); R(22, 9, 20, 14, Soil); R(24, 11, 16, 10, Cream); R(30, 10, 3, 12, Outline);
            for (int effect = 0; effect < 5; effect++)
            for (int frame = 0; frame < 4; frame++)
            { Begin("work_effect_" + effect + "_" + frame); WorkEffect(effect, frame); }
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(_atlas); texture.Apply();
            File.WriteAllBytes(Root + "PastoralAtlas.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(Root + "PastoralAtlas.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "PastoralAtlas.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 1;
            importer.SetTextureSettings(settings);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.alphaIsTransparency = true;
            TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize = 4096;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.overridden = false;
            importer.SetPlatformTextureSettings(platform);
            importer.spritesheet = Slices.ToArray();
            importer.SaveAndReimport();
            var config = AssetDatabase.LoadAssetAtPath<PastoralArtConfig>(Root + "PastoralArt.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PastoralArtConfig>();
                AssetDatabase.CreateAsset(config, Root + "PastoralArt.asset");
            }
            config.sprites = AssetDatabase.LoadAllAssetsAtPath(Root + "PastoralAtlas.png").OfType<Sprite>()
                .OrderBy(s => s.name, StringComparer.Ordinal).Select(s => new PastoralArtConfig.Entry { key = s.name, sprite = s }).ToArray();
            config.chineseFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/NotoSansSC.ttf");
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
            _atlas = null;
            Debug.Log("Pastoral art generated: " + config.sprites.Length + " sprites. World configuration preserved.");
        }

        private static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        private static void Begin(string key, int size = 64, bool centered = false)
        {
            int index = Slices.Count;
            _ox = index % 16 * 64 + (size == 32 ? 1 : 0);
            _oy = index / 16 * 64 + (size == 32 ? 1 : 0);
            Slices.Add(new SpriteMetaData { name = key, rect = new Rect(_ox, _oy, size, size), alignment = 9,
                pivot = centered ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0.125f) });
        }
        private static void P(int x, int y, Color32 c)
        {
            if (x >= 0 && x < 64 && y >= 0 && y < 64) { _atlas[(_oy + y) * Width + _ox + x] = c; }
        }
        private static Color32 Get(int x, int y)
        {
            return _atlas[(_oy + y) * Width + _ox + x];
        }
        private static void PadEdges()
        {
            // One-pixel duplicate outside the sprite rect. With spriteExtrude = 1
            // the mesh samples this ring, so tile edges never read transparent
            // atlas padding (which otherwise appears as a dark seam).
            for (int y = 0; y < 32; y++)
            {
                P(-1, y, Get(0, y));
                P(32, y, Get(31, y));
            }
            for (int x = -1; x <= 32; x++)
            {
                int sx = Math.Max(0, Math.Min(31, x));
                P(x, -1, Get(sx, 0));
                P(x, 32, Get(sx, 31));
            }
        }
        private static void R(int x, int y, int w, int h, Color32 c)
        {
            for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) { P(xx, yy, c); }
        }
        private static void E(int x, int y, int rx, int ry, Color32 c)
        {
            for (int yy = -ry; yy <= ry; yy++) for (int xx = -rx; xx <= rx; xx++)
            { if (xx * xx * ry * ry + yy * yy * rx * rx <= rx * rx * ry * ry) { P(x + xx, y + yy, c); } }
        }
        private static void L(int x, int y, int ex, int ey, Color32 c)
        {
            int n = Math.Max(Math.Abs(ex - x), Math.Abs(ey - y));
            for (int i = 0; i <= n; i++) { P(x + (ex - x) * i / Math.Max(1, n), y + (ey - y) * i / Math.Max(1, n), c); }
        }
        private static void Shadow(int rx = 11) { E(32, 8, rx, 3, C(43, 65, 44, 65)); }
        private static void Terrain(int type, int variant)
        {
            Color32 fill = type == 4 ? C(83, 154, 171) : type == 7 ? Soil : type == 1 || type == 2 ? C(200, 173, 119) : C(120, 158, 86);
            R(0, 0, 32, 32, fill);
            if (type == 7)
            {
                for (int y = 4; y < 32; y += 7) { R(0, y, 32, 2, C(129, 91, 62)); R(0, y + 2, 32, 1, C(184, 139, 88)); }
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                int x = (i * 13 + variant * 7 + type) % 29 + 1;
                int y = (i * 7 + variant * 11) % 29 + 1;
                if (type == 4) { R(x, y, 3 + i % 3, 1, C(119, 181, 189)); }
                else if (type == 1 || type == 2) { R(x, y, 2, 1, C(180, 149, 102)); }
                else { P(x, y, C(105, 147, 77)); if (i % 3 == 0) { P(x + 1, y + 1, C(145, 176, 99)); } }
            }
        }
        private static void Edge(int type, int mask)
        {
            for (int side = 0; side < 4; side++)
            {
                if ((mask & (1 << side)) == 0) { continue; }
                for (int p = 0; p < 32; p++)
                for (int q = 0; q < 4 + (p / 5 % 2); q++)
                {
                    int x = side == 1 ? 31 - q : side == 3 ? q : p;
                    int y = side == 0 ? 31 - q : side == 2 ? q : p;
                    Color32 color = type == 2 ? (q < 2 ? C(193, 179, 123) : q < 4 ? C(132, 188, 187) : C(175, 216, 205)) :
                        q < 2 ? C(120, 158, 86) : C(144, 161, 92);
                    P(x, y, color);
                }
            }
        }
        private static void Prop(string kind, int variant = 0)
        {
            switch (kind)
            {
                case "tree": Tree(variant); return;
                case "rock": Rock(variant); return;
                case "berries": Berry(variant); return;
            }

            Shadow(kind == "house" ? 23 : 12);
            switch (kind)
            {
                case "house": Cottage(0); break;
                case "well":
                    E(32, 15, 13, 8, Outline); R(19, 14, 27, 10, C(156, 162, 143));
                    E(32, 23, 13, 7, Cream); E(32, 24, 9, 4, C(54, 107, 125));
                    R(19, 20, 3, 23, Soil); R(43, 20, 3, 23, Soil); R(16, 42, 33, 4, Outline); R(17, 44, 31, 3, C(173, 98, 65));
                    R(32, 28, 1, 15, Cream); R(29, 27, 7, 6, Soil); break;
                case "berries_empty":
                    E(32, 18, 14, 10, Outline); E(31, 20, 13, 9, Green); E(27, 25, 7, 5, C(127, 164, 80));
                    E(38, 22, 6, 5, C(143, 176, 92)); break;
                case "stock":
                    R(18, 9, 27, 21, Outline); R(20, 11, 23, 17, Soil);
                    for (int x = 21; x < 44; x += 7) { R(x, 11, 1, 17, C(115, 82, 54)); }
                    R(18, 14, 27, 3, C(209, 169, 100)); R(18, 25, 27, 3, C(209, 169, 100));
                    E(26, 32, 7, 5, Cream); R(35, 29, 6, 9, C(102, 158, 174));
                    R(13, 10, 3, 41, Outline); R(48, 10, 3, 41, Outline);
                    R(14, 12, 1, 38, Soil); R(49, 12, 1, 38, Soil);
                    R(10, 40, 44, 13, Outline);
                    for (int stripe = 0; stripe < 6; stripe++)
                    {
                        Color32 cloth = stripe % 2 == 0 ? C(85, 126, 108) : Cream;
                        R(11 + stripe * 7, 43, 7, 9, cloth);
                        R(11 + stripe * 7, 39, 7, 4, cloth);
                    }
                    R(10, 52, 44, 2, C(224, 200, 148));
                    R(19, 19, 25, 1, C(231, 190, 119)); break;
                case "flower":
                    for (int i = 0; i < 3; i++) { int x = 24 + i * 6; R(x, 10, 1, 6 + i % 2 * 3, Green); E(x, 16 + i % 2 * 3, 2, 2, i == 1 ? Cream : C(221, 168, 130)); P(x, 16 + i % 2 * 3, C(211, 182, 88)); } break;
                case "tuft":
                    L(27, 10, 24, 16, Green); L(28, 10, 29, 18, Green); L(30, 10, 34, 15, Green);
                    L(31, 10, 37, 13, C(145, 176, 99)); break;
                default:
                    E(29, 12, 3, 2, C(165, 171, 141)); E(36, 11, 2, 1, Cream); break;
            }
        }

        private static void Cottage(int variant)
        {
            Shadow(23);
            Color32 roof = variant == 1 ? C(92, 126, 130) : variant == 2 ? C(149, 122, 77) : C(183, 101, 64);
            Color32 roofShade = variant == 1 ? C(62, 91, 103) : variant == 2 ? C(113, 88, 60) : C(134, 72, 54);
            R(10, 10, 44, 29, Outline); R(12, 12, 40, 26, Cream);
            R(12, 12, 40, 4, C(147, 130, 102)); R(47, 16, 5, 22, C(213, 190, 144));
            R(12, 32, 40, 5, C(192, 164, 119));
            R(12, 16, 3, 21, Soil); R(50, 16, 2, 21, Soil);
            R(28, 11, 12, 20, Outline); R(30, 12, 8, 17, C(143, 97, 61));
            R(31, 13, 1, 15, C(184, 134, 79)); P(36, 21, C(248, 215, 126));
            R(27, 9, 14, 2, C(195, 185, 152));
            for (int x = 17; x <= 42; x += 25)
            {
                R(x - 1, 22, 9, 11, Outline); R(x, 24, 7, 7, C(86, 133, 145));
                R(x, 29, 6, 2, C(160, 205, 196)); R(x + 3, 23, 1, 9, Cream);
                R(x, 26, 7, 1, Cream); R(x - 2, 21, 11, 2, Soil);
                R(x - 1, 18, 9, 3, C(142, 82, 57));
                for (int f = 0; f < 3; f++)
                {
                    P(x + f * 3, 21, Green); P(x + f * 3, 22, variant == 1 ? Cream : C(223, 148, 120));
                }
            }
            for (int y = 0; y < 18; y++)
            {
                R(5 + y, 35 + y, 54 - y * 2, 1, y % 4 == 0 ? roofShade : roof);
                if (y % 4 != 0)
                {
                    for (int x = 10 + (y / 4 % 2) * 4; x < 54 - y; x += 8)
                    { if (x > 5 + y) { P(x, 35 + y, roofShade); } }
                }
            }
            R(5, 34, 54, 2, Outline); R(6, 36, 52, 1, C(216, 163, 110));
            R(22, 52, 20, 2, roofShade); R(23, 54, 18, 1, roof);
            R(43, 46, 6, 11, C(158, 116, 85)); R(44, 48, 2, 8, C(197, 155, 113));
            R(42, 56, 8, 2, Outline); R(43, 58, 6, 1, Soil);
        }

        private static void Reeds()
        {
            for (int i = 0; i < 5; i++)
            {
                int x = 23 + i * 4;
                L(x, 8, x - 2, 22 + i % 3 * 3, C(90, 126, 72));
                L(x, 8, x + 4, 19, C(147, 168, 87));
                R(x - 3, 23 + i % 3 * 3, 3, 5, C(132, 93, 61));
                P(x - 2, 28 + i % 3 * 3, Cream);
            }
        }

        private static void Lilies()
        {
            E(26, 9, 6, 2, C(58, 121, 126)); E(37, 14, 7, 3, C(58, 121, 126));
            E(26, 10, 5, 2, C(109, 153, 91)); E(37, 15, 6, 3, C(131, 168, 98));
            L(37, 15, 42, 17, C(58, 121, 126));
            E(26, 13, 2, 2, C(246, 207, 185)); P(26, 14, Cream);
        }

        private static void Water(int variant, int frame)
        {
            R(0, 0, 32, 32, C(83, 154, 171));
            for (int i = 0; i < 5; i++)
            {
                int x = (i * 13 + variant * 7) % 24 + 2;
                int y = (i * 7 + variant * 11) % 28 + 2;
                int phase = (frame + i) % 4;
                R(x + phase / 2, y, 3 + phase, 1, C(119, 181, 189));
                if (phase == 2) { P(x + 2, y + 1, C(161, 206, 205)); }
            }
        }

        private static void Tree(int variant)
        {
            Shadow(variant == 2 ? 13 : 11);
            int center = variant == 1 ? 29 : variant == 2 ? 35 : 32;
            int lean = variant == 1 ? -2 : variant == 2 ? 2 : 0;
            R(28 + lean, 9, 8, 29, Outline);
            R(30 + lean, 10, 4, 27, Soil);
            R(31 + lean, 12, 2, 22, C(191, 144, 85));
            int top = variant == 1 ? 40 : variant == 2 ? 39 : 41;
            int rx = variant == 1 ? 18 : variant == 2 ? 24 : 21;
            int ry = variant == 1 ? 20 : variant == 2 ? 16 : 18;
            E(center, top, rx + 1, ry + 1, Outline);
            E(center, top, rx, ry, variant == 2 ? C(88, 132, 70) : Green);
            E(center - rx / 2, top + 5, rx / 2 + 2, ry / 2 + 1, variant == 2 ? C(104, 148, 76) : C(115, 158, 77));
            E(center + rx / 2, top + 3, rx / 2 + 1, ry / 2, variant == 2 ? C(116, 158, 83) : C(130, 171, 86));
            E(center - 8, top + ry - 3, 7, 4, Light);
            E(center + 8, top + 2, 7, 5, C(82, 124, 65));
            R(center - 17, top + 1, 6, 2, C(151, 181, 95));
            R(center - 3, top + ry - 1, 5, 2, Light);
            if (variant == 2)
            {
                E(center + 2, top - ry + 2, 7, 4, C(97, 139, 72));
            }
        }

        private static void Rock(int variant)
        {
            Shadow(variant == 1 ? 9 : 12);
            if (variant == 0)
            {
                E(32, 19, 15, 11, Outline); E(31, 21, 14, 10, C(139, 150, 140));
                E(27, 25, 9, 6, C(176, 183, 157)); L(36, 28, 41, 18, C(105, 121, 114)); R(22, 12, 18, 2, C(110, 127, 108));
            }
            else
            {
                E(29, 17, 11, 9, Outline); E(28, 19, 10, 8, C(150, 158, 144));
                E(25, 22, 6, 4, C(181, 186, 160)); L(33, 24, 37, 16, C(112, 127, 116));
                E(39, 13, 4, 3, C(110, 147, 88)); P(40, 14, C(145, 176, 99));
            }
        }

        private static void Berry(int variant)
        {
            Shadow(10);
            E(32, 18, 14, 10, Outline); E(31, 20, 13, 9, variant == 1 ? C(86, 132, 70) : Green);
            E(27, 25, 7, 5, C(127, 164, 80));
            int shift = variant == 1 ? 3 : 0;
            for (int i = 0; i < 6; i++)
            {
                int x = 22 + i * 4 + ((i + variant) % 2) * 2 - shift;
                int y = 17 + i % 3 * 4 + (variant == 1 ? 2 : 0);
                E(x, y, 2, 2, C(173, 67, 72));
                P(x, y + 1, Cream);
            }
        }

        private static void Deer(int direction, bool moving, int frame)
        {
            Shadow(11);
            int hop = moving && frame % 2 == 1 ? 2 : 0;
            Color32 fur = C(173, 119, 65);
            E(31, 23 + hop, 13, 7, Outline); E(31, 24 + hop, 12, 6, fur);
            int head = direction == 3 ? 19 : direction == 1 ? 43 : 32;
            R(head - 2, 25 + hop, 5, 13, fur); E(head, 39 + hop, 5, 5, fur);
            R(head - 5, 44 + hop, 1, 7, Soil); R(head + 4, 44 + hop, 1, 7, Soil);
            R(head - 8, 47 + hop, 4, 1, Soil); R(head + 4, 48 + hop, 4, 1, Soil);
            P(head + (direction == 3 ? -3 : 3), 40 + hop, Outline);
            for (int leg = 0; leg < 4; leg++) { int x = 22 + leg * 6 + (moving ? (frame % 2 == leg % 2 ? 2 : -2) : 0); R(x, 9 + hop, 2, 12, fur); R(x, 8 + hop, 3, 2, Outline); }
            E(22, 25 + hop, 3, 3, Cream);
        }
        private static void FieldCrop(int crop, int stage, bool dry)
        {
            if (!dry) { E(16, 15, 13, 9, C(107, 78, 56, 135)); }
            if (stage == 0) { return; }
            for (int i = 0; i < 3; i++)
            {
                int x = 6 + i * 9, y = 5 + i % 2 * 3, height = 4 + stage * 4;
                R(x, y, 2, height, dry ? Soil : Green);
                if (crop == 2) { E(x, y + height, 2, 4, stage == 3 ? C(225, 187, 82) : Light); R(x - 3, y + height - 3, 7, 1, Cream); }
                else { E(x - 2, y + height / 2, 3, 2, Green); E(x + 3, y + height, 3, 2, Light); if (stage == 3) { R(x + 3, y + 4, 2, 5, C(115, 162, 74)); } }
            }
        }
        private static void Facility(int kind, bool complete)
        {
            Shadow(14);
            if (!complete) { R(16, 8, 32, 5, Soil); R(18, 12, 3, 23, Outline); R(44, 12, 3, 23, Outline); R(17, 29, 30, 3, Cream); R(26, 14, 14, 8, Soil); return; }
            if (kind == 0) { R(16, 24, 32, 6, Soil); R(18, 10, 4, 15, Outline); R(42, 10, 4, 15, Outline); R(24, 30, 12, 3, Cream); R(35, 30, 3, 10, Outline); }
            else if (kind == 1) { R(20, 10, 24, 17, C(112, 120, 117)); E(32, 28, 12, 6, Outline); E(32, 29, 10, 4, C(102, 160, 183)); E(32, 15, 5, 4, C(230, 135, 52)); R(39, 31, 4, 13, C(144, 149, 143)); }
            else if (kind == 2) { E(32, 15, 17, 10, Soil); E(32, 19, 17, 10, Cream); E(32, 20, 14, 7, C(84, 149, 181)); }
            else if (kind == 3) { Prop("well"); }
            else { R(16, 10, 32, 5, C(111, 145, 147)); R(30, 10, 5, 22, C(111, 145, 147)); E(32, 30, 6, 4, C(77, 154, 196)); R(20, 28, 25, 2, C(161, 207, 220)); }
        }
        private static void Resource(int item)
        {
            Shadow(10);
            if (item == 3) { E(32, 14, 12, 5, C(77, 154, 196)); R(22, 18, 4, 9, Green); }
            else if (item == 4) { R(19, 12, 26, 9, Soil); E(20, 17, 4, 5, Cream); R(22, 24, 23, 7, Soil); }
            else if (item == 5) { Prop("rock"); }
            else { R(29, 10, 3, 20, Green); E(24, 22, 6, 3, Light); E(35, 29, 7, 4, Green); if (item == 7) { E(29, 33, 4, 3, C(195, 122, 157)); } }
        }
        private static void WorkEffect(int effect, int frame)
        {
            int y = 24 + frame % 2 * 3;
            if (effect == 0) { R(42, y, 10, 10, Soil); R(43, y + 1, 8, 7, C(92, 170, 206)); P(48, y - 3 - frame, C(125, 207, 238)); }
            else if (effect == 1) { R(42, y + 5, 15, 1, Cream); R(54, y + 4, 3, 3, Outline); }
            else if (effect == 2) { R(44, y, 2, 13, Soil); R(40, y + 10, 10, 5, C(155, 163, 159)); }
            else if (effect == 3) { R(43, 44, 4, 12, C(226, 114, 96)); R(39, 48, 12, 4, C(226, 114, 96)); }
            else { R(43, y, 2, 14, Soil); R(40, y - 2, 8, 3, C(155, 163, 159)); }
        }

        private static void Crop(int stage, bool dry)
        {
            if (!dry) { E(16, 15, 13, 9, C(107, 78, 56, 135)); }
            if (stage == 0) { return; }
            for (int i = 0; i < 3; i++)
            {
                int x = 7 + i * 9, y = 10 + i % 2 * 7;
                if (stage == 3) { E(x, y + 2, 4, 4, Cream); P(x, y - 3, Cream); }
                R(x, y + 2, 1, 3 + stage * 2, Green);
                E(x - 2, y + 4 + stage, 2 + stage, 2, dry ? C(153, 157, 73) : Green);
                E(x + 2, y + 5 + stage, 2 + stage, 2, Light);
            }
            if (dry) { R(27, 2, 2, 5, C(218, 162, 76)); P(27, 0, Cream); }
        }
        private static void Person(int role, int dir, int action, int frame)
        {
            Shadow(8);
            Color32[] coats = { C(101, 139, 84), C(175, 112, 86), C(78, 119, 112), C(112, 116, 165), C(189, 149, 78) };
            Color32 coat = coats[role];
            bool moving = action == 1;
            int bob = moving && frame % 2 == 1 ? 1 : action == 0 && frame == 3 ? 1 : 0;
            if (action == 4)
            {
                E(31, 13, 12, 5, Outline); R(20, 11, 25, 7, coat); E(23, 16, 5, 4, Cream);
                R(40 + frame % 2, 26, 4, 1, Cream); R(42, 28, 3, 1, Cream); return;
            }
            if (dir == 1 || dir == 3)
            {
                PersonSide(role, action, frame, coat);
                if (dir == 3)
                {
                    // Mirror the whole profile, including the nose, hands and tools.
                    for (int y = 0; y < 64; y++)
                    for (int x = 1; x < 32; x++)
                    {
                        Color32 left = Get(x, y);
                        P(x, y, Get(64 - x, y));
                        P(64 - x, y, left);
                    }
                }
                return;
            }
            int stride = moving ? (frame == 1 ? 2 : frame == 3 ? -2 : 0) : 0;
            R(27, 9, 4, 7 + stride, Outline); R(34, 9, 4, 7 - stride, Outline);
            R(26, 8 + stride, 6, 3, C(95, 71, 53)); R(33, 8 - stride, 6, 3, C(95, 71, 53));
            R(25, 16 + bob, 15, 13, Outline); R(27, 17 + bob, 11, 11, coat);
            R(27, 17 + bob, 2, 10, C(197, 179, 126)); R(27, 17 + bob, 11, 2, C(66, 84, 64));
            int arm = (action >= 2 ? frame % 2 * 3 + 3 : stride);
            R(22, 18 + bob + arm, 4, 8, coat); R(39, 18 + bob - stride, 4, 8, coat);
            R(22, 16 + bob + arm, 4, 4, Cream); R(39, 16 + bob - stride, 4, 4, Cream);
            E(32, 33 + bob, 8, 8, Outline); E(32, 33 + bob, 7, 7, C(229, 187, 140));
            R(25, 36 + bob, 14, 5, C(100, 75, 49));
            if (dir == 2) { R(26, 30 + bob, 13, 8, C(100, 75, 49)); }
            else
            {
                int eye = dir == 1 ? 35 : dir == 3 ? 27 : 28;
                P(eye, 33 + bob, Outline); if (dir == 0) { P(35, 33 + bob, Outline); }
                R(30, 29 + bob, 3, 1, C(177, 119, 97));
            }
            if (role == 0) { R(21, 39 + bob, 23, 3, C(210, 179, 98)); R(26, 42 + bob, 12, 4, Cream); R(26, 42 + bob, 12, 1, Soil); }
            if (role == 1) { R(25, 40 + bob, 14, 3, C(174, 99, 79)); R(39, 35 + bob, 3, 7, C(174, 99, 79)); }
            if (role == 2) { R(25, 39 + bob, 14, 5, coat); L(43, 14, 46, 25, Soil); L(46, 25, 43, 34, Soil); L(43, 14, 43, 34, Cream); }
            if (role == 3) { R(27, 40 + bob, 11, 4, coat); R(30, 26 + bob, 3, 1, Cream); E(43, 15, 3, 4, Soil); }
            if (role == 4) { R(24, 39 + bob, 17, 3, C(210, 165, 76)); R(27, 42 + bob, 11, 3, Cream); R(44, 13 + arm, 2, 12, Soil); R(41, 24 + arm, 8, 4, C(130, 145, 140)); }
            if (action == 2) { E(26, 28 + arm / 2, 3, 2, C(187, 82, 64)); }
            if (action == 3) { R(22, 25 + arm / 2, 5, 6, C(103, 172, 187)); R(23, 29 + arm / 2, 3, 1, Cream); }
            if (action == 5 || action == 6) { L(20, 12 + arm, 23, 30 + arm, Soil); R(17, 29 + arm, 10, 3, Cream); }
        }

        private static void PersonSide(int role, int action, int frame, Color32 coat)
        {
            bool moving = action == 1;
            int stride = moving ? (frame == 0 ? -4 : frame == 2 ? 4 : 0) : 0;
            int bob = moving && frame % 2 == 1 ? 1 : 0;
            Color32 skin = C(229, 187, 140);
            Color32 boots = C(95, 71, 53);

            // Two legs pass beneath a narrow torso; extended and passing poses
            // change the silhouette instead of just stretching front-facing legs.
            L(31, 18, 31 - stride, 10, Outline);
            R(29 - stride, 8 + (moving && frame == 3 ? 2 : 0), 7, 3, boots);
            R(28, 16 + bob, 10, 14, Outline);
            R(29, 17 + bob, 8, 12, coat);
            R(29, 17 + bob, 8, 2, C(66, 84, 64));
            L(33, 18, 32 + stride, 10, Outline);
            R(30 + stride, 8 + (moving && frame == 1 ? 2 : 0), 8, 3, boots);
            int armX = action >= 2 ? 37 + frame % 2 * 2 : 31 - stride;
            L(32, 26 + bob, armX, 20 + bob, Outline);
            R(armX - 1, 19 + bob, 4, 4, skin);

            E(32, 34 + bob, 7, 8, Outline);
            E(33, 34 + bob, 6, 7, skin);
            R(26, 36 + bob, 11, 5, C(100, 75, 49));
            R(26, 31 + bob, 4, 7, C(100, 75, 49));
            R(38, 32 + bob, 3, 3, skin); // Profile nose.
            P(37, 35 + bob, Outline);
            R(36, 30 + bob, 3, 1, C(177, 119, 97));
            if (role == 0) { R(23, 39 + bob, 22, 3, C(210, 179, 98)); R(28, 42 + bob, 11, 4, Cream); }
            if (role == 1) { R(26, 40 + bob, 13, 3, C(174, 99, 79)); R(25, 35 + bob, 3, 7, C(174, 99, 79)); }
            if (role == 2) { R(26, 39 + bob, 13, 5, coat); L(23, 15, 20, 26, Soil); L(20, 26, 23, 35, Soil); L(23, 15, 23, 35, Cream); }
            if (role == 3) { R(28, 40 + bob, 10, 4, coat); E(armX + 1, 16 + bob, 3, 4, Soil); }
            if (role == 4) { R(25, 39 + bob, 16, 3, C(210, 165, 76)); R(28, 42 + bob, 10, 3, Cream); R(armX + 3, 12, 2, 12, Soil); R(armX, 23, 8, 4, C(130, 145, 140)); }
            if (action == 2) { E(41, 29 + frame % 2, 3, 2, C(187, 82, 64)); }
            if (action == 3) { R(39, 27 + frame % 2, 5, 6, C(103, 172, 187)); R(40, 31 + frame % 2, 3, 1, Cream); }
            if (action == 5 || action == 6) { L(armX + 2, 12, armX + 4, 29, Soil); R(armX, 28, 10, 3, Cream); }
        }

        private static void Animal(int species, int dir, bool moving, int frame)
        {
            bool rabbit = species == 0;
            Shadow(rabbit ? 7 : 12);
            int jump = moving ? (frame == 1 || frame == 2 ? (rabbit ? 5 : 2) : 0) : frame == 3 ? 1 : 0;
            int y = 16 + jump;
            Color32 fur = rabbit ? C(233, 222, 192) : C(131, 97, 73);
            E(32, y, rabbit ? 8 : 13, rabbit ? 7 : 9, Outline);
            E(31, y + 1, rabbit ? 7 : 12, rabbit ? 6 : 8, fur);
            E(28, y + 4, rabbit ? 4 : 8, 3, rabbit ? Cream : C(158, 122, 88));
            int head = dir == 1 ? 41 : dir == 3 ? 22 : 32;
            int hy = dir == 2 ? y + 6 : dir == 0 ? y - 1 : y;
            E(head, hy, rabbit ? 5 : 7, rabbit ? 5 : 6, fur);
            if (rabbit)
            {
                R(head - 4, hy + 4, 3, 11, fur); R(head + 1, hy + 4, 3, 12 - frame % 2, fur);
                R(head - 3, hy + 6, 1, 7, C(199, 152, 143)); R(head + 2, hy + 6, 1, 8, C(199, 152, 143));
                E(dir == 1 ? 24 : 40, y - 1, 3, 3, Cream);
            }
            else
            {
                R(23, y + 7, 17, 3, C(78, 67, 55));
                for (int i = 0; i < 5; i++) { R(24 + i * 3, y + 10, 2, 2 + i % 2, C(78, 67, 55)); }
                R(head - 5, hy + 5, 4, 5, C(107, 80, 63)); E(head, hy - 3, 5, 3, C(173, 128, 101));
                P(head - 2, hy - 3, Outline); P(head + 2, hy - 3, Outline); R(head - 5, hy - 4, 2, 3, Cream);
            }
            if (dir != 2) { P(head + (dir == 3 ? -3 : 3), hy + 1, Outline); if (dir == 0) { P(head - 3, hy + 1, Outline); } }
            int stride = moving ? (frame % 2 == 0 ? -2 : 2) : 0;
            R(26 + stride, 8 + jump, 4, 4, rabbit ? Cream : Outline); R(35 - stride, 8 + jump, 4, 4, rabbit ? Cream : Outline);
        }
        private static void Icon(int icon)
        {
            if (icon == 0) { E(16, 14, 8, 7, Cream); R(14, 20, 3, 7, Green); E(12, 24, 5, 2, Green); E(20, 25, 5, 2, Light); }
            else if (icon == 1) { E(16, 12, 7, 7, C(85, 151, 174)); for (int i = 0; i < 10; i++) { R(16 - i / 2, 25 - i, 1 + i, 1, C(106, 179, 192)); } R(12, 9, 2, 5, Cream); }
            else if (icon == 2) { E(13, 14, 4, 6, Soil); E(21, 17, 4, 6, C(208, 165, 91)); }
            else if (icon == 3)
            {
                E(16, 19, 5, 6, C(229, 187, 140)); R(9, 6, 15, 12, Green);
                R(9, 14, 15, 4, C(101, 139, 84)); R(11, 3, 11, 5, C(100, 75, 49));
                P(14, 18, Outline); P(18, 18, Outline); R(14, 22, 5, 1, C(177, 119, 97));
            }
            else if (icon == 4) { E(16, 16, 10, 10, C(213, 173, 77)); E(16, 16, 7, 7, Cream); R(15, 10, 2, 12, Soil); }
            else { R(8, 7, 16, 19, Cream); R(10, 9, 12, 15, Soil); for (int i = 0; i < 3; i++) { R(12, 12 + i * 4, 8, 1, Cream); } }
        }
    }
}
