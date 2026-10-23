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
    // Cold island palette: desaturated blue-grey, ink green and a small amount of campfire warmth.
    public static class PastoralArtGenerator
    {
        private const string Root = "Assets/Game/Resources/Pastoral/";
        private static readonly Color32 Outline = C(27, 37, 36);
        private static readonly Color32 Deep = C(36, 50, 47);
        private static readonly Color32 Green = C(74, 94, 79);
        private static readonly Color32 GreenDark = C(58, 77, 68);
        private static readonly Color32 GreenLight = C(106, 124, 101);
        private static readonly Color32 Moss = C(94, 108, 84);
        private static readonly Color32 Soil = C(103, 87, 71);
        private static readonly Color32 SoilDark = C(76, 64, 54);
        private static readonly Color32 SoilLight = C(143, 125, 102);
        private static readonly Color32 Cream = C(224, 218, 199);
        private static readonly Color32 Stone = C(117, 122, 118);
        private static readonly Color32 StoneLight = C(155, 158, 148);
        private static readonly Color32 Water = C(66, 92, 100);
        private static readonly Color32 WaterLight = C(94, 126, 132);
        private static readonly Color32 WaterPale = C(142, 168, 170);
        private static readonly Color32 FogColor = C(182, 194, 198);
        private static readonly Color32 Ember = C(226, 134, 62);
        private static readonly Color32 EmberLight = C(246, 186, 96);
        private static readonly Color32 EmberCore = C(255, 228, 166);

        private sealed class AtlasTarget
        {
            public readonly string FileName;
            public readonly int Width;
            public readonly int Height;
            public readonly int Columns;
            public readonly FilterMode Filter;
            public readonly Color32[] Pixels;
            public readonly List<SpriteMetaData> Slices = new List<SpriteMetaData>();

            public AtlasTarget(string fileName, int width, int height, int columns, FilterMode filter)
            {
                FileName = fileName;
                Width = width;
                Height = height;
                Columns = columns;
                Filter = filter;
                Pixels = new Color32[width * height];
            }
        }

        private static readonly List<AtlasTarget> Targets = new List<AtlasTarget>();
        private static AtlasTarget _target;
        private static int _ox;
        private static int _oy;
        private static int _w;
        private static int _h;

        [MenuItem("Tools/My AI World/Art/Generate Pastoral Art")]
        public static void Generate()
        {
            Directory.CreateDirectory(Root);
            Targets.Clear();
            var world = new AtlasTarget("PastoralAtlas.png", 1024, 4096, 16, FilterMode.Point);
            var npcA = new AtlasTarget("NpcAtlasA.png", 2048, 2304, 32, FilterMode.Point);
            var npcB = new AtlasTarget("NpcAtlasB.png", 2048, 2304, 32, FilterMode.Point);
            var soft = new AtlasTarget("AtmosphereAtlas.png", 1024, 1024, 8, FilterMode.Bilinear);
            Targets.Add(world);
            Targets.Add(npcA);
            Targets.Add(npcB);
            Targets.Add(soft);

            Use(world);
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
            for (int variant = 0; variant < 6; variant++)
            {
                Begin("tree_" + variant); Conifer(variant);
            }
            for (int variant = 0; variant < 2; variant++)
            {
                Begin("snag_" + variant); Snag(variant);
            }
            Begin("stump"); Stump();
            Begin("rock_0"); Rock(0);
            Begin("rock_1"); Rock(1);
            Begin("fern"); Fern();
            Begin("dead_log"); DeadLog();
            Begin("heather"); Heather();
            Begin("cold_flower"); ColdFlower();
            Begin("shore_pebbles"); ShorePebbles();
            for (int variant = 0; variant < 2; variant++)
            {
                Begin("berries_" + variant); Berry(variant);
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
            for (int variant = 0; variant < 3; variant++)
            {
                Begin("cottage_" + variant); Cottage(variant);
            }
            Begin("reeds"); Reeds();
            Begin("lilies"); Lilies();
            Begin("selection_ring");
            E(32, 8, 15, 6, C(20, 30, 30, 210));
            E(32, 9, 14, 5, C(245, 205, 120));
            E(32, 9, 11, 3, C(0, 0, 0, 0));
            Begin("selection_pointer");
            for (int y = 0; y < 6; y++) { R(32 - y, 50 + y, y * 2 + 1, 1, C(248, 208, 118)); }
            R(30, 49, 5, 1, C(40, 48, 40));
            Begin("alert_low"); AlertBadge(false);
            Begin("alert_down"); AlertBadge(true);
            Begin("snow_0", 32, true); Flake(0);
            Begin("snow_1", 32, true); Flake(1);
            Begin("mote", 32, true); Mote();
            Begin("ember", 32, true); EmberSprite();
            for (int variant = 0; variant < 4; variant++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("water_" + variant + "_" + frame, 32, true);
                WaterFrame(variant, frame); PadEdges();
            }
            for (int direction = 0; direction < 4; direction++)
            for (int move = 0; move < 2; move++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_0_" + direction + "_" + move + "_" + frame); Animal(0, direction, move == 1, frame);
            }
            for (int direction = 0; direction < 4; direction++)
            for (int move = 0; move < 2; move++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_1_" + direction + "_" + move + "_" + frame); Animal(1, direction, move == 1, frame);
            }
            for (int direction = 0; direction < 4; direction++)
            for (int move = 0; move < 2; move++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_2_" + direction + "_" + move + "_" + frame); Deer(direction, move == 1, frame);
            }
            for (int crop = 2; crop <= 3; crop++)
            for (int stage = 0; stage < 4; stage++)
            for (int dry = 0; dry < 2; dry++)
            {
                Begin("crop_type_" + crop + "_" + stage + "_" + dry, 32, true); FieldCrop(crop, stage, dry == 1); PadEdges();
            }
            for (int facility = 0; facility < 5; facility++)
            for (int built = 0; built < 2; built++)
            {
                Begin("facility_" + facility + "_" + built); Facility(facility, built == 1);
            }
            for (int item = 3; item <= 7; item++)
            {
                Begin("resource_" + item); Resource(item);
            }
            Begin("ground_stock"); R(22, 9, 20, 14, SoilDark); R(24, 11, 16, 10, SoilLight); R(30, 10, 3, 12, Outline);
            for (int effect = 0; effect < 5; effect++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("work_effect_" + effect + "_" + frame); WorkEffect(effect, frame);
            }
            for (int icon = 0; icon < 6; icon++)
            {
                Begin("icon_" + icon, 32, true); Icon(icon); PadEdges();
            }
            for (int variant = 0; variant < 2; variant++) { Begin("tent_" + variant); Tent(variant); }
            Begin("lean_to"); LeanTo();
            Begin("woodpile"); Woodpile();
            for (int variant = 0; variant < 2; variant++) { Begin("crate_" + variant); Crate(variant); }
            Begin("barrel"); Barrel();
            Begin("fish_rack"); FishRack();
            for (int frame = 0; frame < 4; frame++) { Begin("campfire_" + frame); Campfire(frame); }
            for (int frame = 0; frame < 2; frame++) { Begin("campfire_embers_" + frame); CampfireEmbers(frame); }

            for (int look = 0; look < 4; look++)
            {
                Use(look < 2 ? npcA : npcB);
                for (int role = 0; role < 5; role++)
                for (int direction = 0; direction < 4; direction++)
                for (int action = 0; action < 7; action++)
                for (int frame = 0; frame < 4; frame++)
                {
                    Begin("npc_" + role + "_" + look + "_" + direction + "_" + action + "_" + frame);
                    Person(role, look, direction, action, frame);
                }
            }

            Use(soft);
            for (int i = 0; i < 4; i++)
            {
                BeginSoft("fog_puff_" + i, 0, i * 128, 256, 128);
                FogPuff(i);
            }
            BeginSoft("mist_band_0", 256, 0, 512, 128); MistBand(0);
            BeginSoft("mist_band_1", 256, 128, 512, 128); MistBand(1);
            BeginSoft("glow_radial", 0, 512, 256, 256); Glow();
            BeginSoft("light_shaft_0", 256, 256, 96, 512); LightShaft(0);
            BeginSoft("light_shaft_1", 352, 256, 96, 512); LightShaft(1);
            BeginSoft("light_shaft_2", 448, 256, 96, 512); LightShaft(2);

            foreach (AtlasTarget target in Targets) { Save(target); }

            var config = AssetDatabase.LoadAssetAtPath<PastoralArtConfig>(Root + "PastoralArt.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PastoralArtConfig>();
                AssetDatabase.CreateAsset(config, Root + "PastoralArt.asset");
            }
            config.sprites = Targets
                .SelectMany(target => AssetDatabase.LoadAllAssetsAtPath(Root + target.FileName).OfType<Sprite>())
                .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                .Select(sprite => new PastoralArtConfig.Entry { key = sprite.name, sprite = sprite })
                .ToArray();
            config.chineseFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/NotoSansSC.ttf");
            config.panel = new Color32(234, 234, 224, 250);
            config.ink = new Color32(38, 50, 50, 255);
            config.muted = new Color32(103, 116, 114, 255);
            config.accent = new Color32(76, 120, 116, 255);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EnsureAtmosphereProfile();
            _target = null;
            Debug.Log("Cold island art generated: " + config.sprites.Length + " sprites across " + Targets.Count + " atlases.");
        }

        private static void EnsureAtmosphereProfile()
        {
            const string path = "Assets/Game/Resources/Atmosphere/AtmosphereProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<MyAiWorld.Presentation.Atmosphere.AtmosphereProfile>(path);
            if (profile != null) { return; }
            profile = ScriptableObject.CreateInstance<MyAiWorld.Presentation.Atmosphere.AtmosphereProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
        }

        private static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

        private static void Use(AtlasTarget target) { _target = target; }

        private static void Begin(string key, int size = 64, bool centered = false)
        {
            int index = _target.Slices.Count;
            int ox = index % _target.Columns * 64 + (size == 32 ? 1 : 0);
            int oy = index / _target.Columns * 64 + (size == 32 ? 1 : 0);
            if (oy + 64 > _target.Height)
            {
                throw new InvalidOperationException("Atlas overflow: " + _target.FileName + " at " + key);
            }
            _ox = ox; _oy = oy; _w = size; _h = size;
            _target.Slices.Add(new SpriteMetaData
            {
                name = key,
                rect = new Rect(ox, oy, size, size),
                alignment = 9,
                pivot = centered ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0.125f)
            });
        }

        private static void BeginSoft(string key, int x, int y, int width, int height)
        {
            _ox = x; _oy = y; _w = width; _h = height;
            _target.Slices.Add(new SpriteMetaData
            {
                name = key,
                rect = new Rect(x, y, width, height),
                alignment = 9,
                pivot = new Vector2(0.5f, 0.5f)
            });
        }

        private static void Save(AtlasTarget target)
        {
            var texture = new Texture2D(target.Width, target.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(target.Pixels);
            texture.Apply();
            string path = Root + target.FileName;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = target.Filter;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = target.Filter == FilterMode.Point ? 1u : 0u;
            importer.SetTextureSettings(settings);
            TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize = 4096;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.overridden = false;
            importer.SetPlatformTextureSettings(platform);
            importer.spritesheet = target.Slices.ToArray();
            importer.SaveAndReimport();
        }

        private static void P(int x, int y, Color32 c)
        {
            int min = _w < 64 ? -1 : 0;
            int max = _w < 64 ? _w : _w - 1;
            if (x < min || x > max || y < min || y > max) { return; }
            _target.Pixels[(_oy + y) * _target.Width + _ox + x] = c;
        }

        private static Color32 Get(int x, int y)
        {
            int min = _w < 64 ? -1 : 0;
            int max = _w < 64 ? _w : _w - 1;
            if (x < min || x > max || y < min || y > max) { return new Color32(0, 0, 0, 0); }
            return _target.Pixels[(_oy + y) * _target.Width + _ox + x];
        }

        private static void PadEdges()
        {
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

        private static void Shadow(int rx = 11) { E(32, 8, rx, 3, C(30, 43, 41, 65)); }

        private static void Terrain(int type, int variant)
        {
            Color32 fill;
            switch (type)
            {
                case 4: fill = Water; break;
                case 7: fill = C(91, 77, 64); break;
                case 1: fill = C(105, 93, 78); break;
                case 2: fill = C(124, 112, 93); break;
                case 3: fill = C(61, 76, 67); break;
                case 5: fill = C(96, 101, 97); break;
                case 6: fill = C(98, 89, 76); break;
                default: fill = C(81, 97, 84); break;
            }
            R(0, 0, 32, 32, fill);
            if (type == 7)
            {
                for (int y = 4; y < 32; y += 7) { R(0, y, 32, 2, C(73, 61, 51)); R(0, y + 2, 32, 1, C(118, 101, 82)); }
                return;
            }
            if (type == 2)
            {
                for (int i = 0; i < 5; i++)
                {
                    int x = (i * 9 + variant * 5) % 27 + 2;
                    int y = (i * 11 + variant * 7) % 27 + 2;
                    P(x, y, C(139, 128, 108));
                }
            }
            for (int i = 0; i < 8; i++)
            {
                int x = (i * 13 + variant * 7 + type) % 29 + 1;
                int y = (i * 7 + variant * 11) % 29 + 1;
                if (type == 4) { R(x, y, 3 + i % 3, 1, WaterLight); }
                else if (type == 1) { R(x, y, 2, 1, C(90, 79, 66)); }
                else if (type == 5 || type == 6) { P(x, y, C(112, 114, 106)); }
                else if (type == 3) { P(x, y, C(53, 68, 60)); if (i % 3 == 0) { P(x + 1, y + 1, C(76, 90, 76)); } }
                else { P(x, y, C(69, 86, 75)); if (i % 3 == 0) { P(x + 1, y + 1, C(99, 113, 94)); } }
            }
            if (type == 0 && variant == 3 && _w == 32)
            {
                P(9, 21, C(188, 190, 170)); P(21, 12, C(176, 180, 166));
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
                    Color32 color;
                    if (type == 2)
                    {
                        color = q < 2 ? C(126, 121, 106) : q < 3 ? C(150, 170, 168) : C(176, 196, 194);
                    }
                    else if (type == 1)
                    {
                        color = q < 2 ? C(108, 99, 82) : C(94, 88, 76);
                    }
                    else
                    {
                        color = q < 2 ? C(88, 81, 68) : C(84, 96, 78);
                    }
                    P(x, y, color);
                }
            }
        }

        private static void Conifer(int variant)
        {
            int shape = variant % 3;
            int tone = variant / 3;
            Color32 outline = tone == 0 ? Outline : C(58, 72, 72);
            Color32 dark = tone == 0 ? C(36, 55, 47) : C(62, 78, 75);
            Color32 mid = tone == 0 ? C(48, 72, 58) : C(78, 94, 90);
            Color32 light = tone == 0 ? C(76, 101, 78) : C(104, 118, 111);
            Color32 rim = tone == 0 ? C(112, 132, 104) : C(138, 150, 144);
            int cx = 32 + (shape == 2 ? -1 : shape == 1 ? 1 : 0);
            Shadow(shape == 0 ? 8 : 10);
            int trunkTop = shape == 0 ? 20 : shape == 1 ? 26 : 24;
            R(cx - 2, 8, 4, trunkTop - 8, outline);
            R(cx - 1, 9, 2, trunkTop - 9, Soil);
            if (tone == 0) { R(cx, 10, 1, trunkTop - 11, SoilLight); }

            int[] bases = shape == 0 ? new int[] { 13, 23, 32 } : shape == 1 ? new int[] { 15, 27, 38 } : new int[] { 16, 30, 43 };
            int[] tops = shape == 0 ? new int[] { 27, 37, 47 } : shape == 1 ? new int[] { 33, 46, 57 } : new int[] { 38, 50, 60 };
            int[] halves = shape == 0 ? new int[] { 8, 6, 4 } : shape == 1 ? new int[] { 11, 8, 5 } : new int[] { 9, 7, 4 };
            for (int tier = 0; tier < 3; tier++)
            {
                int baseY = bases[tier];
                int topY = tops[tier];
                int halfMax = halves[tier];
                R(cx - halfMax + 1, baseY, halfMax * 2 - 1, 1, dark);
                for (int y = topY; y >= baseY; y--)
                {
                    float progress = (topY - y) / (float)Math.Max(1, topY - baseY);
                    int half = 1 + Mathf.RoundToInt(progress * (halfMax - 1));
                    int jag = y % 3 == 0 && half > 2 ? 1 : 0;
                    for (int x = cx - half; x <= cx + half; x++) { P(x, y, mid); }
                    P(cx - half, y, outline);
                    P(cx + half, y, outline);
                    if (y == baseY) { R(cx - half, y, half * 2 + 1, 1, outline); }
                    for (int x = cx - half + 1; x <= cx - half + 3 + jag; x++)
                    {
                        if (x < cx - 1) { P(x, y, light); }
                    }
                    P(cx - half + 1, y, rim);
                    for (int x = cx + half - 2; x < cx + half; x++) { P(x, y, dark); }
                    if (tone == 1 && (y % 7 == 0) && half > 3) { P(cx - half + 2, y, C(210, 216, 210)); }
                }
                if (tier < 2) { R(cx - 3, baseY - 1, 7, 1, outline); }
            }
            if (shape == 2)
            {
                R(cx - 1, tops[2] + 1, 2, 4, dark);
                P(cx, tops[2] + 5, rim);
            }
        }

        private static void Snag(int variant)
        {
            Shadow(8);
            int lean = variant == 1 ? -2 : 1;
            L(32, 8, 32 + lean, 48, Outline);
            L(33, 8, 33 + lean, 48, C(104, 94, 80));
            L(31, 9, 31 + lean, 46, C(76, 72, 64));
            for (int i = 0; i < 4; i++)
            {
                int y = 18 + i * 7;
                int dir = i % 2 == 0 ? 1 : -1;
                L(32 + lean, y, 32 + lean + dir * (7 + i), y + 3, C(86, 82, 72));
                L(32 + lean, y + 1, 32 + lean + dir * (5 + i), y + 3, C(64, 66, 60));
            }
            P(32 + lean, 49, C(128, 126, 114));
        }

        private static void Stump()
        {
            Shadow(7);
            E(32, 13, 9, 5, Outline);
            E(32, 15, 8, 4, C(92, 78, 64));
            E(32, 17, 7, 3, C(144, 124, 98));
            E(32, 17, 4, 2, C(112, 94, 76));
            P(30, 16, C(72, 64, 54)); P(34, 18, C(72, 64, 54));
            R(25, 10, 3, 3, GreenDark);
        }

        private static void Fern()
        {
            for (int i = 0; i < 5; i++)
            {
                int x = 22 + i * 5;
                int top = 20 + (i % 2) * 6;
                L(32, 10, x, top, C(62, 84, 68));
                for (int leaf = 0; leaf < 3; leaf++)
                {
                    int ly = 12 + leaf * 5;
                    int lx = 32 + (x - 32) * leaf / 4;
                    P(lx - 2, ly, C(86, 108, 84)); P(lx + 2, ly + 1, C(70, 92, 74));
                }
            }
        }

        private static void DeadLog()
        {
            Shadow(14);
            E(30, 15, 14, 6, Outline);
            E(30, 17, 13, 5, C(92, 82, 70));
            E(30, 19, 12, 3, C(122, 108, 88));
            R(18, 15, 26, 1, C(74, 70, 61));
            for (int i = 0; i < 4; i++) { P(23 + i * 6, 21, C(76, 96, 74)); }
            E(43, 17, 3, 4, C(74, 66, 56));
        }

        private static void Heather()
        {
            Shadow(8);
            for (int i = 0; i < 5; i++)
            {
                int x = 24 + i * 4;
                L(x, 10, x - 2 + i % 2 * 4, 18 + i % 3 * 3, C(74, 92, 76));
                P(x - 1, 19 + i % 3 * 3, C(150, 126, 148));
                P(x, 20 + i % 3 * 3, C(174, 150, 166));
            }
        }

        private static void ColdFlower()
        {
            for (int i = 0; i < 3; i++)
            {
                int x = 24 + i * 6;
                R(x, 10, 1, 6 + i % 2 * 3, C(76, 96, 82));
                E(x, 16 + i % 2 * 3, 2, 2, i == 1 ? C(216, 220, 214) : C(178, 198, 204));
                P(x, 16 + i % 2 * 3, C(232, 208, 134));
            }
        }

        private static void ShorePebbles()
        {
            E(25, 12, 4, 2, C(120, 122, 112));
            E(33, 10, 3, 2, C(150, 152, 140));
            E(38, 13, 2, 2, C(104, 112, 110));
            P(26, 13, C(178, 180, 166)); P(34, 11, C(198, 198, 184));
            E(30, 15, 2, 1, C(166, 176, 172));
        }

        private static void Prop(string kind, int variant = 0)
        {
            switch (kind)
            {
                case "rock": Rock(variant); return;
                case "berries": Berry(variant); return;
            }

            Shadow(kind == "house" ? 23 : 12);
            switch (kind)
            {
                case "house": Cottage(0); break;
                case "well":
                    E(32, 15, 13, 8, Outline); R(19, 14, 27, 10, C(120, 126, 122));
                    E(32, 23, 13, 7, Cream); E(32, 24, 9, 4, C(66, 96, 106));
                    R(19, 20, 3, 23, Soil); R(43, 20, 3, 23, Soil); R(16, 42, 33, 4, Outline); R(17, 44, 31, 3, C(130, 100, 80));
                    R(32, 28, 1, 15, Cream); R(29, 27, 7, 6, Soil); break;
                case "berries_empty":
                    E(32, 18, 14, 10, Outline); E(31, 20, 13, 9, GreenDark); E(27, 25, 7, 5, C(64, 86, 72));
                    E(38, 22, 6, 5, C(76, 96, 78)); break;
                case "stock":
                    R(18, 9, 27, 21, Outline); R(20, 11, 23, 17, Soil);
                    for (int x = 21; x < 44; x += 7) { R(x, 11, 1, 17, C(72, 62, 52)); }
                    R(18, 14, 27, 3, C(150, 132, 102)); R(18, 25, 27, 3, C(150, 132, 102));
                    E(26, 32, 7, 5, Cream); R(35, 29, 6, 9, C(78, 104, 116));
                    R(13, 10, 3, 41, Outline); R(48, 10, 3, 41, Outline);
                    R(14, 12, 1, 38, Soil); R(49, 12, 1, 38, Soil);
                    R(10, 40, 44, 13, Outline);
                    for (int stripe = 0; stripe < 6; stripe++)
                    {
                        Color32 cloth = stripe % 2 == 0 ? C(72, 100, 96) : C(196, 192, 174);
                        R(11 + stripe * 7, 43, 7, 9, cloth);
                        R(11 + stripe * 7, 39, 7, 4, cloth);
                    }
                    R(10, 52, 44, 2, C(208, 198, 172));
                    R(19, 19, 25, 1, C(196, 172, 118)); break;
                case "flower":
                    for (int i = 0; i < 3; i++) { int x = 24 + i * 6; R(x, 10, 1, 6 + i % 2 * 3, C(70, 92, 78)); E(x, 16 + i % 2 * 3, 2, 2, i == 1 ? Cream : C(176, 190, 196)); P(x, 16 + i % 2 * 3, C(220, 196, 120)); } break;
                case "tuft":
                    L(27, 10, 24, 16, Green); L(28, 10, 29, 18, Green); L(30, 10, 34, 15, Green);
                    L(31, 10, 37, 13, GreenLight); break;
                default:
                    E(29, 12, 3, 2, StoneLight); E(36, 11, 2, 1, Cream); break;
            }
        }

        private static void Cottage(int variant)
        {
            Shadow(23);
            Color32 roof = variant == 1 ? C(72, 92, 94) : variant == 2 ? C(104, 92, 76) : C(118, 82, 68);
            Color32 roofShade = variant == 1 ? C(50, 68, 72) : variant == 2 ? C(78, 70, 60) : C(88, 64, 56);
            R(10, 10, 44, 29, Outline); R(12, 12, 40, 26, C(176, 168, 146));
            R(12, 12, 40, 4, C(120, 114, 98)); R(47, 16, 5, 22, C(198, 188, 158));
            R(12, 32, 40, 5, C(160, 150, 126));
            R(12, 16, 3, 21, Soil); R(50, 16, 2, 21, Soil);
            R(28, 11, 12, 20, Outline); R(30, 12, 8, 17, C(112, 82, 60));
            R(31, 13, 1, 15, C(146, 112, 80)); P(36, 21, C(224, 204, 140));
            R(27, 9, 14, 2, C(150, 146, 132));
            for (int x = 17; x <= 42; x += 25)
            {
                R(x - 1, 22, 9, 11, Outline); R(x, 24, 7, 7, C(66, 88, 96));
                R(x, 29, 6, 2, C(124, 152, 152)); R(x + 3, 23, 1, 9, Cream);
                R(x, 26, 7, 1, Cream); R(x - 2, 21, 11, 2, Soil);
                R(x - 1, 18, 9, 3, C(108, 78, 58));
                for (int f = 0; f < 3; f++)
                {
                    P(x + f * 3, 21, Green); P(x + f * 3, 22, variant == 1 ? Cream : C(166, 132, 110));
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
            R(5, 34, 54, 2, Outline); R(6, 36, 52, 1, C(150, 138, 112));
            R(22, 52, 20, 2, roofShade); R(23, 54, 18, 1, roof);
            R(43, 46, 6, 11, C(126, 106, 84)); R(44, 48, 2, 8, C(168, 144, 108));
            R(42, 56, 8, 2, Outline); R(43, 58, 6, 1, Soil);
        }

        private static void Reeds()
        {
            for (int i = 0; i < 5; i++)
            {
                int x = 23 + i * 4;
                L(x, 8, x - 2, 22 + i % 3 * 3, C(66, 92, 72));
                L(x, 8, x + 4, 19, C(108, 124, 92));
                R(x - 3, 23 + i % 3 * 3, 3, 5, C(104, 82, 60));
                P(x - 2, 28 + i % 3 * 3, C(200, 194, 170));
            }
        }

        private static void Lilies()
        {
            E(26, 9, 6, 2, C(48, 78, 84)); E(37, 14, 7, 3, C(48, 78, 84));
            E(26, 10, 5, 2, C(78, 106, 88)); E(37, 15, 6, 3, C(92, 118, 98));
            L(37, 15, 42, 17, C(48, 78, 84));
            E(26, 13, 2, 2, C(208, 206, 194)); P(26, 14, Cream);
        }

        private static void WaterFrame(int variant, int frame)
        {
            R(0, 0, 32, 32, Water);
            for (int i = 0; i < 5; i++)
            {
                int x = (i * 13 + variant * 7) % 24 + 2;
                int y = (i * 7 + variant * 11) % 28 + 2;
                int phase = (frame + i) % 4;
                R(x + phase / 2, y, 3 + phase, 1, WaterLight);
                if (phase == 2) { P(x + 2, y + 1, WaterPale); }
            }
            if (variant == 3) { P(6, 27, C(116, 142, 146)); P(25, 5, C(116, 142, 146)); }
        }

        private static void Rock(int variant)
        {
            Shadow(variant == 1 ? 9 : 12);
            if (variant == 0)
            {
                E(32, 19, 15, 11, Outline); E(31, 21, 14, 10, C(112, 118, 114));
                E(27, 25, 9, 6, C(148, 152, 142)); L(36, 28, 41, 18, C(84, 94, 92)); R(22, 12, 18, 2, C(88, 100, 94));
                P(24, 20, C(78, 96, 82)); P(25, 21, C(92, 108, 92));
            }
            else
            {
                E(29, 17, 11, 9, Outline); E(28, 19, 10, 8, C(120, 126, 122));
                E(25, 22, 6, 4, C(154, 158, 148)); L(33, 24, 37, 16, C(90, 98, 96));
                E(39, 13, 4, 3, C(72, 96, 78)); P(40, 14, C(102, 124, 98));
            }
        }

        private static void Berry(int variant)
        {
            Shadow(10);
            E(32, 18, 14, 10, Outline); E(31, 20, 13, 9, variant == 1 ? C(62, 84, 70) : GreenDark);
            E(27, 25, 7, 5, C(78, 100, 82));
            int shift = variant == 1 ? 3 : 0;
            for (int i = 0; i < 6; i++)
            {
                int x = 22 + i * 4 + ((i + variant) % 2) * 2 - shift;
                int y = 17 + i % 3 * 4 + (variant == 1 ? 2 : 0);
                E(x, y, 2, 2, C(150, 72, 72));
                P(x, y + 1, C(208, 150, 138));
            }
        }

        private static void Deer(int direction, bool moving, int frame)
        {
            Shadow(11);
            int hop = moving && frame % 2 == 1 ? 2 : 0;
            Color32 fur = C(144, 116, 92);
            E(31, 23 + hop, 13, 7, Outline); E(31, 24 + hop, 12, 6, fur);
            int head = direction == 3 ? 19 : direction == 1 ? 43 : 32;
            R(head - 2, 25 + hop, 5, 13, fur); E(head, 39 + hop, 5, 5, fur);
            R(head - 5, 44 + hop, 1, 7, Soil); R(head + 4, 44 + hop, 1, 7, Soil);
            R(head - 8, 47 + hop, 4, 1, Soil); R(head + 4, 48 + hop, 4, 1, Soil);
            P(head + (direction == 3 ? -3 : 3), 40 + hop, Outline);
            for (int leg = 0; leg < 4; leg++) { int x = 22 + leg * 6 + (moving ? (frame % 2 == leg % 2 ? 2 : -2) : 0); R(x, 9 + hop, 2, 12, fur); R(x, 8 + hop, 3, 2, Outline); }
            E(22, 25 + hop, 3, 3, C(196, 188, 168));
        }

        private static void FieldCrop(int crop, int stage, bool dry)
        {
            if (!dry) { E(16, 15, 13, 9, C(84, 68, 54, 135)); }
            if (stage == 0) { return; }
            for (int i = 0; i < 3; i++)
            {
                int x = 6 + i * 9, y = 5 + i % 2 * 3, height = 4 + stage * 4;
                R(x, y, 2, height, dry ? C(130, 110, 78) : Green);
                if (crop == 2) { E(x, y + height, 2, 4, stage == 3 ? C(206, 178, 92) : GreenLight); R(x - 3, y + height - 3, 7, 1, Cream); }
                else { E(x - 2, y + height / 2, 3, 2, Green); E(x + 3, y + height, 3, 2, GreenLight); if (stage == 3) { R(x + 3, y + 4, 2, 5, C(98, 132, 82)); } }
            }
        }

        private static void Facility(int kind, bool complete)
        {
            Shadow(14);
            if (!complete) { R(16, 8, 32, 5, Soil); R(18, 12, 3, 23, Outline); R(44, 12, 3, 23, Outline); R(17, 29, 30, 3, Cream); R(26, 14, 14, 8, Soil); return; }
            if (kind == 0) { R(16, 24, 32, 6, Soil); R(18, 10, 4, 15, Outline); R(42, 10, 4, 15, Outline); R(24, 30, 12, 3, Cream); R(35, 30, 3, 10, Outline); }
            else if (kind == 1) { R(20, 10, 24, 17, C(94, 102, 100)); E(32, 28, 12, 6, Outline); E(32, 29, 10, 4, C(82, 118, 136)); E(32, 15, 5, 4, Ember); R(39, 31, 4, 13, C(122, 128, 124)); }
            else if (kind == 2) { E(32, 15, 17, 10, C(88, 80, 68)); E(32, 19, 17, 10, C(198, 192, 172)); E(32, 20, 14, 7, C(72, 108, 122)); }
            else if (kind == 3) { Prop("well"); }
            else { R(16, 10, 32, 5, C(92, 108, 110)); R(30, 10, 5, 22, C(92, 108, 110)); E(32, 30, 6, 4, C(68, 110, 134)); R(20, 28, 25, 2, C(140, 168, 174)); }
        }

        private static void Resource(int item)
        {
            Shadow(10);
            if (item == 3) { E(32, 14, 12, 5, C(64, 108, 130)); R(22, 18, 4, 9, Green); }
            else if (item == 4) { R(19, 12, 26, 9, Soil); E(20, 17, 4, 5, Cream); R(22, 24, 23, 7, Soil); }
            else if (item == 5) { Rock(0); }
            else { R(29, 10, 3, 20, Green); E(24, 22, 6, 3, GreenLight); E(35, 29, 7, 4, Green); if (item == 7) { E(29, 33, 4, 3, C(150, 108, 126)); } }
        }

        private static void WorkEffect(int effect, int frame)
        {
            int y = 24 + frame % 2 * 3;
            if (effect == 0) { R(42, y, 10, 10, Soil); R(43, y + 1, 8, 7, C(76, 118, 138)); P(48, y - 3 - frame, C(118, 168, 186)); }
            else if (effect == 1) { R(42, y + 5, 15, 1, Cream); R(54, y + 4, 3, 3, Outline); }
            else if (effect == 2) { R(44, y, 2, 13, Soil); R(40, y + 10, 10, 5, C(132, 138, 134)); }
            else if (effect == 3) { R(43, 44, 4, 12, C(176, 92, 82)); R(39, 48, 12, 4, C(176, 92, 82)); }
            else { R(43, y, 2, 14, Soil); R(40, y - 2, 8, 3, C(132, 138, 134)); }
        }

        private static void Crop(int stage, bool dry)
        {
            if (!dry) { E(16, 15, 13, 9, C(84, 68, 54, 135)); }
            if (stage == 0) { return; }
            for (int i = 0; i < 3; i++)
            {
                int x = 7 + i * 9, y = 10 + i % 2 * 7;
                if (stage == 3) { E(x, y + 2, 4, 4, Cream); P(x, y - 3, Cream); }
                R(x, y + 2, 1, 3 + stage * 2, Green);
                E(x - 2, y + 4 + stage, 2 + stage, 2, dry ? C(126, 118, 74) : Green);
                E(x + 2, y + 5 + stage, 2 + stage, 2, GreenLight);
            }
            if (dry) { R(27, 2, 2, 5, C(184, 144, 78)); P(27, 0, Cream); }
        }

        private static void Tent(int variant)
        {
            Shadow(15);
            Color32 canvas = variant == 0 ? C(168, 168, 150) : C(120, 134, 132);
            Color32 shade = variant == 0 ? C(132, 134, 120) : C(92, 108, 108);
            for (int y = 0; y < 26; y++)
            {
                int half = 2 + (25 - y) * 10 / 25;
                R(32 - half, 14 + y, half * 2, 1, y % 5 == 0 ? shade : canvas);
                P(32 - half, 14 + y, Outline); P(32 + half - 1, 14 + y, Outline);
            }
            R(30, 14, 5, 9, C(60, 72, 70));
            R(31, 14, 3, 8, C(40, 52, 52));
            R(31, 12, 2, 34, C(104, 88, 68));
            L(20, 12, 28, 38, C(82, 78, 64)); L(44, 12, 36, 38, C(82, 78, 64));
            R(30, 40, 5, 1, C(196, 196, 178));
            P(32, 44, Cream);
            R(26, 10, 12, 1, C(72, 66, 54));
        }

        private static void LeanTo()
        {
            Shadow(16);
            for (int i = 0; i < 5; i++)
            {
                int x = 10 + i * 9;
                L(x, 16, x + 12, 43, C(86, 76, 62));
                L(x + 2, 17, x + 14, 42, C(108, 96, 78));
            }
            R(16, 20, 32, 3, C(70, 82, 72));
            R(20, 26, 24, 2, C(74, 86, 76));
            R(13, 12, 3, 30, Outline); R(48, 12, 3, 30, Outline);
            R(13, 12, 38, 2, C(96, 82, 66));
            R(18, 10, 28, 3, C(118, 104, 82));
            R(26, 12, 12, 12, C(54, 64, 62));
            R(29, 14, 6, 4, C(42, 52, 52));
        }

        private static void Woodpile()
        {
            Shadow(14);
            for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4 - (row % 2); i++)
            {
                int x = 18 + i * 8 + (row % 2) * 4;
                int y = 10 + row * 7;
                E(x, y, 5, 3, Outline);
                E(x, y, 4, 2, C(122, 96, 70));
                E(x - 1, y + 1, 2, 1, C(168, 138, 100));
                P(x + 2, y, C(88, 72, 58));
            }
            R(16, 9, 3, 27, C(76, 68, 56)); R(48, 9, 3, 27, C(76, 68, 56));
        }

        private static void Crate(int variant)
        {
            Shadow(12);
            R(18, 10, 28, 22, Outline);
            R(20, 12, 24, 18, variant == 0 ? C(124, 102, 76) : C(104, 106, 94));
            for (int i = 0; i < 3; i++) { R(20, 14 + i * 6, 24, 1, C(86, 70, 56)); }
            R(20, 20, 24, 2, C(80, 84, 80));
            L(20, 12, 44, 30, C(96, 80, 62)); L(44, 12, 20, 30, C(96, 80, 62));
            R(20, 10, 4, 3, C(152, 142, 116)); R(41, 10, 3, 3, C(152, 142, 116));
        }

        private static void Barrel()
        {
            Shadow(9);
            E(32, 21, 10, 12, Outline);
            E(32, 21, 9, 11, C(114, 90, 68));
            R(23, 18, 18, 2, C(78, 80, 76));
            R(23, 25, 18, 2, C(78, 80, 76));
            R(29, 12, 6, 1, C(86, 78, 66));
            for (int x = 25; x < 40; x += 4) { R(x, 15, 1, 15, C(96, 76, 58)); }
            E(32, 11, 8, 2, C(138, 112, 82));
        }

        private static void FishRack()
        {
            Shadow(12);
            R(14, 10, 3, 30, Outline); R(44, 10, 3, 30, Outline);
            R(14, 38, 33, 2, C(102, 86, 66));
            for (int i = 0; i < 4; i++)
            {
                int x = 19 + i * 7;
                L(x, 37, x, 27, C(92, 80, 64));
                E(x + 1, 23, 3, 4, C(152, 162, 160));
                P(x - 1, 24, C(112, 124, 124));
                P(x + 4, 23, C(112, 124, 124));
                P(x + 1, 26, C(206, 204, 190));
            }
            R(20, 38, 22, 2, C(82, 72, 58));
        }

        private static void Campfire(int frame)
        {
            Shadow(12);
            E(32, 12, 13, 5, Outline);
            E(32, 14, 12, 4, C(112, 108, 98));
            E(32, 15, 9, 3, C(52, 44, 38));
            L(24, 15, 40, 19, C(94, 70, 50)); L(40, 15, 24, 19, C(82, 64, 48));
            L(25, 18, 39, 15, C(110, 84, 58));
            int lift = frame % 2 == 0 ? 0 : 1;
            int flameBase = 17;
            for (int y = 0; y < 13 + frame % 3 * 2; y++)
            {
                int half = 5 - y / 4 + (y < 3 ? 1 : 0);
                for (int x = -half; x <= half; x++)
                {
                    if ((x + y + frame) % 5 == 0 && y > 4) { continue; }
                    P(32 + x, flameBase + y + lift, Ember);
                }
            }
            for (int y = 0; y < 8 + frame % 2 * 2; y++)
            {
                int half = 2 - y / 5;
                for (int x = -half; x <= half; x++) { P(32 + x, flameBase + 1 + y + lift, EmberLight); }
            }
            R(31, flameBase + 2 + lift, 2, 3, EmberCore);
            P(32, flameBase + 5 + lift, EmberCore);
            P(30 + frame % 2, flameBase + 9 + lift, Ember);
            P(34 - frame % 2, flameBase + 6 + lift, EmberLight);
            R(23, 11, 3, 2, Stone); R(38, 11, 4, 2, Stone);
            P(28, 11, StoneLight); P(36, 10, StoneLight);
        }

        private static void CampfireEmbers(int frame)
        {
            int shift = frame == 0 ? 0 : 2;
            E(32, 24 + shift, 2, 2, EmberLight);
            P(38, 31 + shift, Ember);
            P(26, 34 - shift, EmberCore);
            P(36, 38, Ember);
            P(29, 41 - shift, EmberLight);
        }

        private struct NpcLook
        {
            public Color32 Coat;
            public Color32 Trim;
            public Color32 Hair;
            public Color32 Skin;
            public int Style;
        }

        private static NpcLook LookOf(int look)
        {
            switch (look & 3)
            {
                case 0:
                    return new NpcLook { Coat = C(84, 106, 112), Trim = C(198, 202, 190), Hair = C(70, 58, 48), Skin = C(206, 174, 146), Style = 0 };
                case 1:
                    return new NpcLook { Coat = C(88, 104, 82), Trim = C(156, 114, 88), Hair = C(48, 46, 44), Skin = C(184, 148, 120), Style = 1 };
                case 2:
                    return new NpcLook { Coat = C(124, 100, 84), Trim = C(100, 120, 132), Hair = C(118, 92, 66), Skin = C(220, 188, 160), Style = 2 };
                default:
                    return new NpcLook { Coat = C(72, 80, 86), Trim = C(138, 162, 168), Hair = C(126, 128, 122), Skin = C(164, 132, 112), Style = 3 };
            }
        }

        private static Color32 RoleAccent(int role)
        {
            switch (role)
            {
                case 0: return C(194, 174, 116);
                case 1: return C(148, 100, 84);
                case 2: return C(64, 82, 78);
                case 3: return C(92, 112, 138);
                default: return C(178, 148, 92);
            }
        }

        private static void Person(int role, int look, int dir, int action, int frame)
        {
            NpcLook style = LookOf(look);
            Color32 coat = style.Coat;
            Color32 accent = RoleAccent(role);
            Shadow(8);
            bool moving = action == 1;
            int bob = moving && frame % 2 == 1 ? 1 : action == 0 && frame == 3 ? 1 : 0;
            if (action == 4)
            {
                E(31, 13, 12, 5, Outline); R(20, 11, 25, 7, coat); E(23, 16, 5, 4, Cream);
                R(40 + frame % 2, 26, 4, 1, Cream); R(42, 28, 3, 1, Cream); return;
            }
            if (dir == 1 || dir == 3)
            {
                PersonSide(role, look, action, frame, coat, accent);
                if (dir == 3)
                {
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
            if (style.Style == 3 && dir == 2) { R(20, 17 + bob, 24, 13, Outline); R(22, 18 + bob, 20, 11, style.Trim); }
            int stride = moving ? (frame == 1 ? 2 : frame == 3 ? -2 : 0) : 0;
            R(27, 9, 4, 7 + stride, Outline); R(34, 9, 4, 7 - stride, Outline);
            R(26, 8 + stride, 6, 3, C(86, 70, 58)); R(33, 8 - stride, 6, 3, C(86, 70, 58));
            R(25, 16 + bob, 15, 13, Outline); R(27, 17 + bob, 11, 11, coat);
            R(27, 17 + bob, 2, 10, style.Trim); R(27, 17 + bob, 11, 2, C(56, 70, 66));
            if (style.Style == 3) { R(26, 16 + bob, 13, 2, style.Trim); R(20, 18 + bob, 3, 9, style.Trim); }
            int arm = (action >= 2 ? frame % 2 * 3 + 3 : stride);
            R(22, 18 + bob + arm, 4, 8, coat); R(39, 18 + bob - stride, 4, 8, coat);
            R(22, 16 + bob + arm, 4, 4, style.Skin); R(39, 16 + bob - stride, 4, 4, style.Skin);
            E(32, 33 + bob, 8, 8, Outline); E(32, 33 + bob, 7, 7, style.Skin);
            R(25, 36 + bob, 14, 5, style.Hair);
            if (dir == 2) { R(26, 30 + bob, 13, 8, style.Hair); }
            else
            {
                int eye = dir == 1 ? 35 : dir == 3 ? 27 : 28;
                P(eye, 33 + bob, Outline); if (dir == 0) { P(35, 33 + bob, Outline); }
                R(30, 29 + bob, 3, 1, C(158, 116, 100));
            }
            DrawHeadwear(style, dir, bob);
            if (role == 0) { R(21, 39 + bob, 23, 3, accent); R(26, 42 + bob, 12, 4, Cream); R(26, 42 + bob, 12, 1, Soil); }
            if (role == 1) { R(25, 40 + bob, 14, 3, accent); R(39, 35 + bob, 3, 7, accent); }
            if (role == 2) { R(25, 39 + bob, 14, 5, coat); L(43, 14, 46, 25, Soil); L(46, 25, 43, 34, Soil); L(43, 14, 43, 34, Cream); }
            if (role == 3) { R(27, 40 + bob, 11, 4, accent); R(30, 26 + bob, 3, 1, Cream); E(43, 15, 3, 4, Soil); }
            if (role == 4) { R(24, 39 + bob, 17, 3, accent); R(27, 42 + bob, 11, 3, Cream); R(44, 13 + arm, 2, 12, Soil); R(41, 24 + arm, 8, 4, C(118, 132, 128)); }
            if (action == 2) { E(26, 28 + arm / 2, 3, 2, C(168, 88, 74)); }
            if (action == 3) { R(22, 25 + arm / 2, 5, 6, C(88, 138, 152)); R(23, 29 + arm / 2, 3, 1, Cream); }
            if (action == 5 || action == 6) { L(20, 12 + arm, 23, 30 + arm, Soil); R(17, 29 + arm, 10, 3, Cream); }
        }

        private static void DrawHeadwear(NpcLook style, int dir, int bob)
        {
            switch (style.Style)
            {
                case 0:
                    R(25, 40 + bob, 14, 3, style.Hair);
                    R(24, 37 + bob, 2, 5, style.Hair);
                    R(38, 37 + bob, 2, 5, style.Hair);
                    break;
                case 1:
                    R(24, 38 + bob, 16, 4, style.Trim);
                    R(25, 42 + bob, 14, 1, C(112, 86, 68));
                    R(31, 44 + bob, 3, 2, style.Trim);
                    break;
                case 2:
                    R(24, 39 + bob, 16, 3, style.Trim);
                    R(24, 34 + bob, 3, 7, style.Hair);
                    R(37, 34 + bob, 3, 7, style.Hair);
                    R(27, 28 + bob, 10, 2, style.Trim);
                    break;
                default:
                    if (dir == 2) { E(32, 35 + bob, 9, 9, style.Coat); R(24, 30 + bob, 17, 5, style.Coat); }
                    else
                    {
                        E(32, 35 + bob, 9, 9, Outline);
                        E(32, 36 + bob, 8, 8, style.Coat);
                        R(26, 29 + bob, 13, 4, style.Coat);
                        R(28, 31 + bob, 9, 3, style.Skin);
                    }
                    break;
            }
        }

        private static void PersonSide(int role, int look, int action, int frame, Color32 coat, Color32 accent)
        {
            NpcLook style = LookOf(look);
            bool moving = action == 1;
            int stride = moving ? (frame == 0 ? -4 : frame == 2 ? 4 : 0) : 0;
            int bob = moving && frame % 2 == 1 ? 1 : 0;
            Color32 skin = style.Skin;
            Color32 boots = C(86, 70, 58);

            L(31, 18, 31 - stride, 10, Outline);
            R(29 - stride, 8 + (moving && frame == 3 ? 2 : 0), 7, 3, boots);
            if (style.Style == 3) { E(25, 23 + bob, 5, 7, Outline); E(25, 24 + bob, 4, 6, style.Trim); }
            R(28, 16 + bob, 10, 14, Outline);
            R(29, 17 + bob, 8, 12, coat);
            R(29, 17 + bob, 8, 2, C(56, 70, 66));
            L(33, 18, 32 + stride, 10, Outline);
            R(30 + stride, 8 + (moving && frame == 1 ? 2 : 0), 8, 3, boots);
            int armX = action >= 2 ? 37 + frame % 2 * 2 : 31 - stride;
            L(32, 26 + bob, armX, 20 + bob, Outline);
            R(armX - 1, 19 + bob, 4, 4, skin);

            E(32, 34 + bob, 7, 8, Outline);
            E(33, 34 + bob, 6, 7, skin);
            R(26, 36 + bob, 11, 5, style.Hair);
            R(26, 31 + bob, 4, 7, style.Hair);
            R(38, 32 + bob, 3, 3, skin);
            P(37, 35 + bob, Outline);
            R(36, 30 + bob, 3, 1, C(158, 116, 100));
            if (style.Style == 1) { R(25, 38 + bob, 14, 4, style.Trim); R(26, 42 + bob, 12, 1, C(112, 86, 68)); }
            else if (style.Style == 2) { R(25, 39 + bob, 14, 3, style.Trim); R(25, 33 + bob, 3, 8, style.Hair); R(27, 28 + bob, 8, 2, style.Trim); }
            else if (style.Style == 3) { E(32, 35 + bob, 9, 9, style.Coat); R(25, 29 + bob, 13, 4, style.Coat); R(40, 33 + bob, 2, 2, skin); }
            if (role == 0) { R(23, 39 + bob, 22, 3, accent); R(28, 42 + bob, 11, 4, Cream); }
            if (role == 1) { R(26, 40 + bob, 13, 3, accent); R(25, 35 + bob, 3, 7, accent); }
            if (role == 2) { R(26, 39 + bob, 13, 5, coat); L(23, 15, 20, 26, Soil); L(20, 26, 23, 35, Soil); L(23, 15, 23, 35, Cream); }
            if (role == 3) { R(28, 40 + bob, 10, 4, accent); E(armX + 1, 16 + bob, 3, 4, Soil); }
            if (role == 4) { R(25, 39 + bob, 16, 3, accent); R(28, 42 + bob, 10, 3, Cream); R(armX + 3, 12, 2, 12, Soil); R(armX, 23, 8, 4, C(118, 132, 128)); }
            if (action == 2) { E(41, 29 + frame % 2, 3, 2, C(168, 88, 74)); }
            if (action == 3) { R(39, 27 + frame % 2, 5, 6, C(88, 138, 152)); R(40, 31 + frame % 2, 3, 1, Cream); }
            if (action == 5 || action == 6) { L(armX + 2, 12, armX + 4, 29, Soil); R(armX, 28, 10, 3, Cream); }
        }

        private static void Animal(int species, int dir, bool moving, int frame)
        {
            bool rabbit = species == 0;
            Shadow(rabbit ? 7 : 12);
            int jump = moving ? (frame == 1 || frame == 2 ? (rabbit ? 5 : 2) : 0) : frame == 3 ? 1 : 0;
            int y = 16 + jump;
            Color32 fur = rabbit ? C(206, 204, 192) : C(104, 94, 84);
            E(32, y, rabbit ? 8 : 13, rabbit ? 7 : 9, Outline);
            E(31, y + 1, rabbit ? 7 : 12, rabbit ? 6 : 8, fur);
            E(28, y + 4, rabbit ? 4 : 8, 3, rabbit ? Cream : C(132, 120, 106));
            int head = dir == 1 ? 41 : dir == 3 ? 22 : 32;
            int hy = dir == 2 ? y + 6 : dir == 0 ? y - 1 : y;
            E(head, hy, rabbit ? 5 : 7, rabbit ? 5 : 6, fur);
            if (rabbit)
            {
                R(head - 4, hy + 4, 3, 11, fur); R(head + 1, hy + 4, 3, 12 - frame % 2, fur);
                R(head - 3, hy + 6, 1, 7, C(184, 158, 150)); R(head + 2, hy + 6, 1, 8, C(184, 158, 150));
                E(dir == 1 ? 24 : 40, y - 1, 3, 3, Cream);
            }
            else
            {
                R(23, y + 7, 17, 3, C(64, 60, 54));
                for (int i = 0; i < 5; i++) { R(24 + i * 3, y + 10, 2, 2 + i % 2, C(64, 60, 54)); }
                R(head - 5, hy + 5, 4, 5, C(88, 80, 70)); E(head, hy - 3, 5, 3, C(150, 128, 110));
                P(head - 2, hy - 3, Outline); P(head + 2, hy - 3, Outline); R(head - 5, hy - 4, 2, 3, Cream);
            }
            if (dir != 2) { P(head + (dir == 3 ? -3 : 3), hy + 1, Outline); if (dir == 0) { P(head - 3, hy + 1, Outline); } }
            int stride = moving ? (frame % 2 == 0 ? -2 : 2) : 0;
            R(26 + stride, 8 + jump, 4, 4, rabbit ? Cream : Outline); R(35 - stride, 8 + jump, 4, 4, rabbit ? Cream : Outline);
        }

        private static void Icon(int icon)
        {
            if (icon == 0) { E(16, 14, 8, 7, Cream); R(14, 20, 3, 7, Green); E(12, 24, 5, 2, Green); E(20, 25, 5, 2, GreenLight); }
            else if (icon == 1) { E(16, 12, 7, 7, C(82, 130, 146)); for (int i = 0; i < 10; i++) { R(16 - i / 2, 25 - i, 1 + i, 1, C(102, 154, 166)); } R(12, 9, 2, 5, Cream); }
            else if (icon == 2) { E(13, 14, 4, 6, Soil); E(21, 17, 4, 6, C(188, 156, 98)); }
            else if (icon == 3)
            {
                E(16, 19, 5, 6, C(206, 174, 146)); R(9, 6, 15, 12, C(72, 94, 80));
                R(9, 14, 15, 4, C(88, 110, 94)); R(11, 3, 11, 5, C(86, 70, 58));
                P(14, 18, Outline); P(18, 18, Outline); R(14, 22, 5, 1, C(158, 116, 100));
            }
            else if (icon == 4) { E(16, 16, 10, 10, C(188, 158, 96)); E(16, 16, 7, 7, Cream); R(15, 10, 2, 12, Soil); }
            else { R(8, 7, 16, 19, Cream); R(10, 9, 12, 15, Soil); for (int i = 0; i < 3; i++) { R(12, 12 + i * 4, 8, 1, Cream); } }
        }

        private static void AlertBadge(bool down)
        {
            E(32, 21, 11, 11, Outline);
            E(32, 21, 9, 9, down ? C(150, 62, 52) : C(178, 106, 54));
            if (down)
            {
                R(28, 16, 4, 10, Cream); R(26, 18, 8, 4, Cream);
            }
            else
            {
                R(31, 15, 3, 8, Cream); R(31, 25, 3, 3, Cream);
            }
            P(32, 12, C(228, 208, 164));
        }

        private static void Flake(int variant)
        {
            int cx = 32, cy = 32;
            if (variant == 0)
            {
                P(cx, cy, C(236, 242, 244)); P(cx, cy + 1, C(218, 228, 234));
                P(cx - 1, cy, C(214, 226, 232)); P(cx + 1, cy, C(214, 226, 232));
            }
            else
            {
                R(cx - 1, cy, 3, 1, C(230, 238, 242)); R(cx, cy - 1, 1, 3, C(230, 238, 242));
                P(cx, cy, C(250, 252, 252));
            }
        }

        private static void Mote()
        {
            P(32, 32, C(188, 206, 210, 220));
            P(33, 32, C(160, 184, 190, 140));
        }

        private static void EmberSprite()
        {
            P(31, 32, Ember); P(32, 33, EmberLight); P(33, 31, EmberCore);
        }

        private static void PAlpha(int x, int y, float alpha)
        {
            if (x < 0 || x >= _w || y < 0 || y >= _h) { return; }
            _target.Pixels[(_oy + y) * _target.Width + _ox + x] =
                new Color32(FogColor.r, FogColor.g, FogColor.b, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
        }

        private static void FogPuff(int seed)
        {
            float sx = seed * 1.7f + 0.3f;
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                float nx = (x + 0.5f) / _w * 2f - 1f;
                float ny = (y + 0.5f) / _h * 2f - 1f;
                float radius = Mathf.Sqrt(nx * nx * 1.25f + ny * ny * 1.6f);
                if (radius >= 1f) { continue; }
                float falloff = Mathf.Pow(1f - radius, 1.6f);
                float wave = 0.55f + 0.22f * Mathf.Sin(nx * 5.3f + sx) + 0.14f * Mathf.Sin(ny * 8.1f - nx * 3.2f + sx * 2f);
                wave += 0.09f * Mathf.Sin(nx * 13.7f + ny * 6.4f + sx * 3f);
                PAlpha(x, y, Mathf.Clamp01(falloff * wave * 0.9f));
            }
        }

        private static void MistBand(int seed)
        {
            float sx = seed * 2.4f + 0.7f;
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                float ny = (y + 0.5f) / _h * 2f - 1f;
                float nx = (x + 0.5f) / _w;
                float band = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(ny) * 1.15f), 1.7f);
                float wave = 0.52f + 0.24f * Mathf.Sin(nx * 9.2f + sx) + 0.12f * Mathf.Sin(nx * 21.3f - ny * 4.1f + sx);
                PAlpha(x, y, Mathf.Clamp01(band * wave));
            }
        }

        private static void Glow()
        {
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                float nx = (x + 0.5f) / _w * 2f - 1f;
                float ny = (y + 0.5f) / _h * 2f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                if (radius >= 1f) { continue; }
                float alpha = Mathf.Pow(1f - radius, 2.3f);
                _target.Pixels[(_oy + y) * _target.Width + _ox + x] =
                    new Color32(255, 186, 124, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
            }
        }

        private static void LightShaft(int seed)
        {
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                float nx = (x + 0.5f) / _w;
                float ny = (y + 0.5f) / _h;
                float across = Mathf.Pow(Mathf.Sin(Mathf.PI * nx), 2.2f);
                float length = Mathf.Pow(Mathf.Sin(Mathf.PI * ny), 0.9f);
                float streak = 0.65f + 0.35f * Mathf.Sin(nx * 11.4f + seed * 2.1f + ny * 3.7f);
                float alpha = across * length * streak * 0.55f;
                _target.Pixels[(_oy + y) * _target.Width + _ox + x] =
                    new Color32(236, 240, 228, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
            }
        }
    }
}
