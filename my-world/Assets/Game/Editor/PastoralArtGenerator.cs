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

            // Append only after every legacy slice: old atlas sizes and packing stay unchanged.
            GenerateWinterAndCombatAtlases();
            DrawHuntingAtlas();

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

        [MenuItem("Tools/My AI World/Art/Generate Hunting Art")]
        public static void GenerateHuntingArt()
        {
            Targets.Clear(); DrawHuntingAtlas(); Save(Targets[0]);
            var config = AssetDatabase.LoadAssetAtPath<PastoralArtConfig>(Root + "PastoralArt.asset");
            var entries = config.sprites.Where(e => !e.key.StartsWith("animal_charge_") && !e.key.StartsWith("animal_hurt_") && !e.key.StartsWith("hunt_"));
            config.sprites = entries.Concat(AssetDatabase.LoadAllAssetsAtPath(Root + "HuntingAtlas.png").OfType<Sprite>()
                .Select(sprite => new PastoralArtConfig.Entry { key = sprite.name, sprite = sprite }))
                .OrderBy(e => e.key, StringComparer.Ordinal).ToArray();
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); _target = null;
        }

        private static void DrawHuntingAtlas()
        {
            var target = new AtlasTarget("HuntingAtlas.png", 1024, 1024, 16, FilterMode.Point);
            Targets.Add(target); Use(target);
            for (int direction = 0; direction < 4; direction++)
            for (int stage = 0; stage < 3; stage++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_charge_" + direction + "_" + stage + "_" + frame);
                AnimalAttack(1, direction, stage == 0 ? 0 : stage == 1 ? 2 : 3);
                int shift = stage == 0 ? -frame : stage == 1 ? frame * 2 : 3 - frame;
                ShiftDrawing(direction == 1 ? shift : direction == 3 ? -shift : 0,
                    direction == 0 ? -shift : direction == 2 ? shift : 0);
                if (stage == 1) { L(20, 8, 30, 8, SoilLight); L(16, 12, 24, 12, SoilLight); }
            }
            for (int species = 0; species < 7; species++)
            for (int direction = 0; direction < 4; direction++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_hurt_" + species + "_" + direction + "_" + frame);
                AnimalAttack(species, direction, 0);
                int shift = frame == 1 ? 3 : frame == 2 ? 2 : 0;
                ShiftDrawing(direction == 1 ? -shift : direction == 3 ? shift : 0,
                    direction == 0 ? shift : direction == 2 ? -shift : 0);
            }
            Begin("hunt_charge_warning");
            L(19, 8, 32, 19, C(236, 157, 72)); L(32, 19, 45, 8, C(236, 157, 72));
            L(19, 10, 32, 21, C(236, 157, 72)); L(32, 21, 45, 10, C(236, 157, 72));
            for (int role = 0; role < 3; role++)
            {
                Begin("hunt_role_" + role);
                E(32, 32, 12, 12, Outline);
                if (role == 0) { DrawCombatBow(32, 30, 1, 7); }
                else if (role == 1) { DrawSpear(25, 20, 38, 44); }
                else { L(22, 24, 37, 39, Cream); L(27, 39, 37, 39, Cream); L(37, 39, 37, 29, Cream); }
            }
        }

        private static void GenerateWinterAndCombatAtlases()
        {
            // Begin reserves a 64x64 cell even for centered 32px tiles.
            // Winter: 145/256 cells. Combat: 1715/1792 cells (54 occupied rows).
            var winter = new AtlasTarget("WinterEcologyAtlas.png", 1024, 1024, 16, FilterMode.Point);
            var combat = new AtlasTarget("CombatAtlas.png", 2048, 3584, 32, FilterMode.Point);
            Targets.Add(winter);
            Targets.Add(combat);

            Use(winter);
            for (int species = 3; species < 7; species++)
            for (int direction = 0; direction < 4; direction++)
            for (int move = 0; move < 2; move++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_" + species + "_" + direction + "_" + move + "_" + frame);
                WinterAnimal(species, direction, move == 1, frame);
            }
            for (int species = 0; species < 7; species++)
            {
                Begin("carcass_" + species); Carcass(species);
            }
            Begin("snow_ground", 32, true); WinterTerrain(false); PadEdges();
            Begin("frozen_water", 32, true); WinterTerrain(true); PadEdges();
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("fire_source_" + frame); Campfire(frame);
            }
            for (int item = 15; item <= 18; item++)
            {
                Begin("resource_" + item); WinterResource(item);
            }

            Use(combat);
            for (int species = 0; species < 7; species++)
            for (int direction = 0; direction < 4; direction++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("animal_attack_" + species + "_" + direction + "_" + frame);
                AnimalAttack(species, direction, frame);
            }
            for (int role = 0; role < 5; role++)
            for (int look = 0; look < 4; look++)
            for (int direction = 0; direction < 4; direction++)
            for (int stage = 0; stage < 5; stage++)
            for (int frame = 0; frame < 4; frame++)
            {
                Begin("npc_attack_" + role + "_" + look + "_" + direction + "_" + stage + "_" + frame);
                PersonCombat(role, look, direction, stage, frame);
            }
            Begin("projectile_arrow"); Arrow(20, 8, 45, 8);
            Begin("projectile_hit"); ProjectileImpact(true);
            Begin("projectile_miss"); ProjectileImpact(false);
        }

        private static void WinterTerrain(bool frozen)
        {
            Color32 baseColor = frozen ? C(126, 155, 164) : C(194, 207, 208);
            Color32 shade = frozen ? C(98, 128, 142) : C(174, 191, 197);
            Color32 light = frozen ? C(168, 192, 198) : C(222, 230, 227);
            R(0, 0, 32, 32, baseColor);
            for (int i = 0; i < 12; i++)
            {
                int x = (i * 13 + 3) % 28 + 1;
                int y = (i * 7 + 5) % 28 + 1;
                R(x, y, 2 + i % 3, 1, i % 3 == 0 ? light : shade);
            }
            if (frozen)
            {
                L(3, 24, 11, 19, shade); L(11, 19, 16, 20, shade);
                L(16, 20, 23, 12, shade); L(16, 20, 17, 27, shade);
                L(5, 25, 11, 21, light); R(21, 7, 7, 1, light);
            }
            else
            {
                R(4, 9, 5, 1, light); R(23, 24, 5, 1, light);
                L(15, 5, 16, 8, GreenDark); P(17, 6, Moss);
            }
        }

        private static void WinterAnimal(int species, int direction, bool moving, int frame)
        {
            if (species == 3) { Vole(direction, moving, frame); }
            else if (species == 6) { Raven(direction, moving, frame); }
            else { Canid(species == 4, direction, moving, frame); }
        }

        private static void MirrorLeft()
        {
            for (int y = 0; y < 64; y++)
            for (int x = 1; x < 32; x++)
            {
                Color32 left = Get(x, y);
                P(x, y, Get(64 - x, y)); P(64 - x, y, left);
            }
        }

        private static void Vole(int direction, bool moving, int frame)
        {
            Shadow(6);
            bool side = direction == 1 || direction == 3;
            int bob = moving && frame % 2 == 1 ? 1 : !moving && frame == 3 ? 1 : 0;
            int stride = moving ? (frame == 0 ? -1 : frame == 2 ? 1 : 0) : 0;
            Color32 fur = C(123, 117, 101);
            Color32 pale = C(173, 171, 150);
            L(side ? 24 : 29, 12, side ? 18 : 26, 10 + frame % 2, SoilLight);
            L(side ? 18 : 26, 10 + frame % 2, side ? 16 : 25, 12, Soil);
            R(27 + stride, 8, 3, 3, SoilDark); R(34 - stride, 8, 3, 3, SoilDark);
            E(31, 14 + bob, side ? 8 : 6, 5, Outline);
            E(31, 15 + bob, side ? 7 : 5, 4, fur);
            E(29, 17 + bob, 4, 2, pale);
            int hx = side ? 38 : 32, hy = (direction == 2 ? 19 : 14) + bob;
            E(hx, hy, 4, 4, Outline); E(hx, hy, 3, 3, fur);
            E(hx - 2, hy + 4, 2, 2, Outline); P(hx - 2, hy + 4, SoilLight);
            if (!side) { E(hx + 2, hy + 4, 2, 2, Outline); P(hx + 2, hy + 4, SoilLight); }
            if (direction != 2)
            {
                int noseX = side ? hx + 4 : hx;
                P(noseX, hy - 1, C(157, 128, 120)); P(hx + 1, hy + 1, Outline);
                if (!side) { P(hx - 2, hy + 1, Outline); }
                L(noseX - 1, hy - 1, noseX + 3, hy - 2, pale);
            }
            if (direction == 3) { MirrorLeft(); }
        }

        private static void Canid(bool fox, int direction, bool moving, int frame)
        {
            // Fox: low slender trunk, tall ears, swept white-tipped brush.
            // Wolf: taller shoulders, broad chest, heavy muzzle, hanging grey tail.
            Shadow(fox ? 11 : 15);
            bool side = direction == 1 || direction == 3;
            int bob = moving && frame % 2 == 1 ? 1 : !moving && frame == 3 ? 1 : 0;
            int stride = moving ? (frame == 0 ? -2 : frame == 2 ? 2 : 0) : 0;
            Color32 fur = fox ? C(160, 109, 76) : C(113, 128, 132);
            Color32 light = fox ? C(190, 146, 105) : C(153, 165, 162);
            Color32 dark = fox ? C(89, 69, 58) : C(71, 87, 94);
            if (side)
            {
                if (fox)
                {
                    L(24, 22 + bob, 15, 16 + bob, Outline);
                    E(19, 18 + bob, 8, 4, Outline); E(19, 19 + bob, 7, 3, fur);
                    E(13, 17 + bob, 3, 3, Cream);
                }
                else
                {
                    L(21, 25 + bob, 13, 17 + bob, Outline);
                    L(21, 24 + bob, 13, 16 + bob, dark);
                    L(20, 24 + bob, 12, 17 + bob, fur);
                }
                for (int leg = 0; leg < 4; leg++)
                {
                    int x = 23 + leg * 6 + (leg % 2 == 0 ? stride : -stride);
                    L(x, 20 + bob, x + (leg % 2 == 0 ? -stride : stride), 10, dark);
                    R(x - 1, 8, 4, 3, Outline);
                }
                E(32, (fox ? 22 : 25) + bob, fox ? 11 : 14, fox ? 5 : 7, Outline);
                E(32, (fox ? 23 : 26) + bob, fox ? 10 : 13, fox ? 4 : 6, fur);
                E(29, (fox ? 25 : 29) + bob, fox ? 7 : 9, 2, light);
                if (!fox) { E(40, 28 + bob, 6, 9, fur); E(41, 26 + bob, 3, 6, light); }
            }
            else
            {
                int tailX = direction == 2 ? 27 : 39;
                E(tailX, 17 + bob, fox ? 4 : 2, fox ? 7 : 6, dark);
                E(tailX, 18 + bob, fox ? 3 : 1, 5, fur);
                if (fox) { E(tailX, 12 + bob, 3, 2, Cream); }
                R(26 + stride, 8, 3, fox ? 11 : 14, dark);
                R(35 - stride, 8, 3, fox ? 11 : 14, dark);
                E(32, (fox ? 22 : 25) + bob, fox ? 7 : 10, fox ? 8 : 10, Outline);
                E(32, (fox ? 23 : 26) + bob, fox ? 6 : 9, fox ? 7 : 9, fur);
                if (direction == 0) { E(32, 21 + bob, fox ? 3 : 5, 6, Cream); }
                else { E(32, 28 + bob, fox ? 3 : 5, 5, dark); }
            }
            int hx = side ? (fox ? 43 : 46) : 32;
            int hy = (fox ? 29 : 33) + bob + (direction == 2 ? 2 : 0);
            E(hx, hy, fox ? 5 : 7, fox ? 5 : 6, Outline);
            E(hx, hy + 1, fox ? 4 : 6, fox ? 4 : 5, fur);
            for (int ear = 0; ear < 2; ear++)
            {
                int ex = hx - 4 + ear * (side ? 5 : 8);
                int height = fox ? 8 : 5;
                for (int y = 0; y < height; y++)
                {
                    int half = (height - y) / 3;
                    R(ex - half, hy + 3 + y, half * 2 + 1, 1, Outline);
                    if (half > 0) { R(ex - half + 1, hy + 3 + y, half * 2 - 1, 1, light); }
                }
            }
            if (direction != 2)
            {
                int mx = side ? hx + (fox ? 5 : 6) : hx;
                E(mx, hy - 2, side ? 4 : 3, 2, Cream);
                P(side ? mx + 3 : mx, hy - 2, Outline);
                P(hx + 2, hy + 2, Outline);
                if (!side) { P(hx - 3, hy + 2, Outline); }
            }
            if (direction == 3) { MirrorLeft(); }
        }

        private static void Raven(int direction, bool moving, int frame)
        {
            Shadow(6);
            bool side = direction == 1 || direction == 3;
            int bob = moving && frame % 2 == 1 ? 2 : !moving && frame == 3 ? 1 : 0;
            int stride = moving ? (frame == 0 ? -2 : frame == 2 ? 2 : 0) : 0;
            Color32 feather = C(43, 57, 68);
            Color32 sheen = C(78, 98, 110);
            L(29, 15 + bob, 28 + stride, 8, Outline); L(34, 15 + bob, 35 - stride, 8, Outline);
            R(26 + stride, 8, 5, 1, Outline); R(33 - stride, 8, 5, 1, Outline);
            if (side)
            {
                L(29, 20 + bob, 19, 13 + bob, Outline);
                L(30, 21 + bob, 20, 14 + bob, feather);
            }
            else { R(29, 12 + bob, 7, 8, feather); R(30, 12 + bob, 1, 5, sheen); }
            E(32, 22 + bob, side ? 8 : 6, 8, Outline);
            E(32, 23 + bob, side ? 7 : 5, 7, feather);
            E(side ? 30 : 28, 23 + bob, 4, 5, sheen);
            L(27, 26 + bob, 32, 18 + bob, feather);
            if (!side) { E(36, 23 + bob, 3, 5, feather); }
            int hx = side ? 38 : 32, hy = 31 + bob;
            E(hx, hy, 5, 5, Outline); E(hx, hy + 1, 4, 4, feather);
            P(hx - 1, hy + 3, sheen);
            if (direction != 2)
            {
                L(side ? hx + 3 : hx - 1, hy, side ? hx + 10 : hx, hy - 2, Outline);
                L(side ? hx + 3 : hx + 1, hy + 1, side ? hx + 9 : hx, hy - 1, Stone);
                P(hx + 2, hy + 1, Cream);
                if (!side) { P(hx - 2, hy + 1, Cream); }
            }
            if (direction == 3) { MirrorLeft(); }
        }

        private static void AnimalAttack(int species, int direction, int frame)
        {
            int dir = direction == 3 ? 1 : direction;
            if (species == 2) { DeerAttack(dir, frame); }
            else if (species == 6) { RavenAttack(dir, frame); }
            else
            {
                if (species < 2) { Animal(species, dir, false, 0); }
                else { WinterAnimal(species, dir, false, 0); }
                bool side = dir == 1;
                int hx, hy;
                switch (species)
                {
                    case 0: hx = side ? 41 : 32; hy = dir == 2 ? 22 : dir == 0 ? 15 : 16; break;
                    case 1: hx = side ? 41 : 32; hy = dir == 2 ? 22 : dir == 0 ? 15 : 16; break;
                    case 3: hx = side ? 38 : 32; hy = dir == 2 ? 19 : 14; break;
                    case 4: hx = side ? 43 : 32; hy = dir == 2 ? 31 : 29; break;
                    default: hx = side ? 46 : 32; hy = dir == 2 ? 35 : 33; break;
                }
                int reach = species == 3 ? 3 : species == 0 ? 4 : 6;
                int mx = side ? hx + reach : hx;
                int opening = frame == 2 ? (species == 3 ? 2 : 4) : frame == 1 ? 2 : 1;
                Color32 muzzle = species == 1 ? C(150, 128, 110) : species == 3 ? SoilLight : Cream;
                if (dir != 2)
                {
                    E(mx, hy - 2, side ? 4 : 3, opening, Outline);
                    R(mx - 2, hy - 2, 4, 1, C(144, 101, 96));
                    R(mx - 2, hy - 2 - opening, 5, 1, muzzle);
                    P(mx + (side ? 3 : 0), hy - 1 + opening, Outline);
                    if (frame == 1 || frame == 2)
                    {
                        P(mx - 1, hy - 1, Cream); P(mx + 1, hy - 1, Cream);
                    }
                }
                else
                {
                    // Rear-facing bite: the two cheeks project beyond the nape, no rear eyes.
                    E(hx - 4, hy + frame % 2, 2, 2, muzzle);
                    E(hx + 4, hy + frame % 2, 2, 2, muzzle);
                }
            }
            int lunge = frame == 0 ? -1 : frame == 2 ? 3 : frame == 3 ? 1 : 0;
            ShiftDrawing(dir == 1 ? lunge : 0, dir == 0 ? -lunge : dir == 2 ? lunge : 0);
            if (direction == 3) { MirrorLeft(); }
        }

        private static void DeerAttack(int direction, int frame)
        {
            Shadow(11);
            bool side = direction == 1;
            Color32 fur = C(144, 116, 92);
            int stride = frame == 2 ? 2 : frame == 0 ? -1 : 0;
            for (int leg = 0; leg < 4; leg++)
            {
                int x = 22 + leg * 6 + (leg % 2 == 0 ? stride : -stride);
                R(x, 9, 2, 12, fur); R(x, 8, 3, 2, Outline);
            }
            E(31, 23, 13, 7, Outline); E(31, 24, 12, 6, fur);
            E(22, 25, 3, 3, Cream);
            int hx = side ? 43 : 32;
            int hy = (frame == 0 ? 34 : frame == 1 ? 29 : frame == 2 ? 24 : 30) + (direction == 2 ? 3 : 0);
            L(side ? 40 : 32, 27, hx, hy, fur);
            E(hx, hy, 5, 5, Outline); E(hx, hy + 1, 4, 4, fur);
            if (side)
            {
                E(hx + 4, hy - 2, 4, 2, SoilLight); P(hx + 7, hy - 2, Outline);
                P(hx + 2, hy + 2, Outline);
            }
            else if (direction == 0) { P(hx - 2, hy + 1, Outline); P(hx + 2, hy + 1, Outline); }
            for (int antler = 0; antler < 2; antler++)
            {
                int ax = hx - 4 + antler * 8;
                int tipX = side ? ax + 8 : ax + (antler == 0 ? -4 : 4);
                L(ax, hy + 3, tipX, hy + 11, Soil);
                L(ax + (side ? 4 : antler == 0 ? -2 : 2), hy + 7, tipX + 3, hy + 7, Soil);
                P(tipX, hy + 12, SoilLight);
            }
        }

        private static void RavenAttack(int direction, int frame)
        {
            Raven(direction, false, 0);
            bool side = direction == 1;
            Color32 feather = C(43, 57, 68);
            int dip = frame == 0 ? -1 : frame == 1 ? 4 : frame == 2 ? 9 : 3;
            if (frame == 1 || frame == 2)
            {
                E(24, 24, 7, 3 + frame, Outline); E(24, 25, 6, 2 + frame, feather);
                if (!side) { E(40, 24, 7, 3 + frame, feather); }
            }
            R(side ? 34 : 27, 27, side ? 17 : 11, 12, C(0, 0, 0, 0));
            int hx = side ? 38 + frame : 32, hy = 31 - dip;
            L(34, 25, hx, hy, feather);
            E(hx, hy, 5, 5, Outline); E(hx, hy + 1, 4, 4, feather);
            P(hx - 1, hy + 3, C(78, 98, 110));
            int tx = side ? hx + 7 : hx, ty = hy - (frame == 2 ? 7 : 3);
            L(side ? hx + 3 : hx - 1, hy, tx, ty, Outline);
            L(side ? hx + 3 : hx + 1, hy + 1, tx, ty + 1, Stone);
            if (direction != 2)
            {
                P(hx + 2, hy + 1, Cream);
                if (!side) { P(hx - 2, hy + 1, Cream); }
            }
        }

        private static void ShiftDrawing(int dx, int dy)
        {
            if (dx == 0 && dy == 0) { return; }
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++) { pixels[y * 64 + x] = Get(x, y); }
            R(0, 0, 64, 64, C(0, 0, 0, 0));
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++) { P(x + dx, y + dy, pixels[y * 64 + x]); }
        }

        private static void Carcass(int species)
        {
            int width = species == 3 ? 6 : species == 0 || species == 6 ? 9 : species == 5 ? 17 : 14;
            Color32 fur;
            switch (species)
            {
                case 0: fur = C(206, 204, 192); break;
                case 1: fur = C(104, 94, 84); break;
                case 2: fur = C(144, 116, 92); break;
                case 3: fur = C(123, 117, 101); break;
                case 4: fur = C(160, 109, 76); break;
                case 5: fur = C(113, 128, 132); break;
                default: fur = C(43, 57, 68); break;
            }
            Shadow(width);
            E(31, 13, width, species == 3 ? 3 : 5, Outline);
            E(31, 14, width - 1, species == 3 ? 2 : 4, fur);
            int hx = 32 + width - 1;
            E(hx, 12, species == 3 ? 3 : 5, species == 3 ? 2 : 3, fur);
            // Closed eye, folded limbs and flattened outline; no living idle pose.
            L(hx, 13, hx + 2, 12, Outline);
            if (species == 0)
            {
                L(hx - 2, 15, hx + 7, 17, fur); L(hx, 15, hx + 9, 14, Cream);
                E(23, 12, 3, 2, Cream);
            }
            else if (species == 1)
            {
                R(22, 17, 19, 2, SoilDark); E(hx + 3, 11, 3, 2, SoilLight);
                P(hx + 2, 9, Cream);
            }
            else if (species == 2)
            {
                L(24, 12, 19, 9, fur); L(36, 12, 30, 9, fur);
                L(hx, 15, hx + 3, 23, Soil); L(hx + 3, 21, hx + 8, 21, Soil);
                L(hx + 3, 18, hx + 7, 18, Soil);
            }
            else if (species == 3) { L(26, 13, 21, 11, SoilLight); E(hx - 1, 15, 2, 2, SoilLight); }
            else if (species == 4 || species == 5)
            {
                E(19, 13, species == 4 ? 7 : 4, 3, fur);
                if (species == 4) { E(13, 12, 3, 2, Cream); }
                L(hx - 3, 14, hx - 2, 19, Outline); E(hx + 4, 11, 3, 2, Cream);
                L(25, 11, 22, 9, SoilDark); L(36, 11, 33, 9, SoilDark);
            }
            else
            {
                E(29, 14, 7, 3, C(78, 98, 110)); L(27, 15, 33, 12, fur);
                L(hx + 3, 11, hx + 9, 10, Outline); L(23, 12, 18, 10, fur);
                L(30, 10, 29, 8, Outline);
            }
        }

        private static void WinterResource(int item)
        {
            Shadow(10);
            if (item == 15)
            {
                Arrow(21, 13, 43, 25); Arrow(22, 17, 44, 29); Arrow(20, 10, 42, 22);
                L(29, 16, 31, 23, SoilDark);
            }
            else if (item == 16)
            {
                E(31, 17, 11, 7, Outline); E(31, 18, 10, 6, C(151, 91, 87));
                E(28, 20, 5, 3, C(185, 124, 115)); E(35, 16, 3, 3, Cream);
                E(35, 16, 1, 1, C(132, 91, 87)); L(23, 15, 28, 12, C(206, 173, 155));
            }
            else if (item == 17)
            {
                E(32, 19, 10, 9, Outline); E(32, 20, 9, 8, SoilLight);
                E(23, 12, 4, 3, Soil); E(41, 12, 4, 3, Soil);
                E(23, 27, 3, 4, SoilLight); E(41, 27, 3, 4, SoilLight);
                L(30, 12, 34, 27, Soil); P(28, 24, Cream); P(36, 18, Cream);
            }
            else
            {
                E(32, 30, 8, 8, Outline); E(32, 31, 7, 7, Cream);
                E(32, 31, 4, 4, SoilDark);
                R(22, 10, 21, 19, Outline); R(24, 12, 17, 17, C(83, 105, 116));
                R(17, 15, 6, 13, Outline); R(18, 16, 4, 11, C(83, 105, 116));
                R(42, 15, 6, 13, Outline); R(43, 16, 4, 11, C(83, 105, 116));
                R(24, 11, 17, 3, Cream); R(31, 14, 3, 15, Cream);
                R(18, 15, 4, 3, Cream); R(43, 15, 4, 3, Cream);
                P(32, 19, Soil); P(32, 24, Soil);
            }
        }

        private static void PersonCombat(int role, int look, int direction, int stage, int frame)
        {
            int dir = direction == 3 ? 1 : direction;
            Person(role, look, dir, 0, 0, true);
            NpcLook style = LookOf(look);
            if (stage == 0 || stage == 1)
            {
                // Draw -> release: nock travels away from the bow then snaps back to the limbs.
                int draw = stage == 0 ? 3 + frame * 3 : frame == 0 ? 12 : frame == 1 ? 6 : 0;
                if (dir == 1)
                {
                    int bx = 44, by = stage == 0 ? 28 + frame : 31 - frame;
                    int pullX = bx - draw;
                    DrawCombatBow(bx, by, dir, draw);
                    if (stage == 0 || frame == 0) { Arrow(pullX, by, bx + 12, by); }
                    CombatArm(32, 26, 38, by - 2, bx, by, style);
                    CombatArm(31, 26, 27, by - 4, stage == 1 && frame > 1 ? 30 : pullX, by, style);
                }
                else
                {
                    int sign = dir == 0 ? -1 : 1;
                    int bx = 44, by = dir == 0 ? 19 - frame / 2 : 42 + frame / 2;
                    int pullY = by - sign * draw;
                    DrawCombatBow(bx, by, dir, draw);
                    if (stage == 0 || frame == 0) { Arrow(bx, pullY, bx, by + sign * 10); }
                    CombatArm(39, 25, 44, 27, bx, by, style);
                    CombatArm(24, 25, 31, dir == 0 ? 22 : 30,
                        stage == 1 && frame > 1 ? 33 : bx,
                        stage == 1 && frame > 1 ? 27 : pullY, style);
                }
            }
            else if (stage == 2 || stage == 3)
            {
                int extension = stage == 2 ? -frame : frame == 0 ? 0 : frame == 1 ? 4 : frame == 2 ? 9 : 3;
                if (dir == 1)
                {
                    int handX = 33 + extension, handY = stage == 2 ? 25 + frame : 29 + frame % 2;
                    DrawSpear(handX - 15, handY - 1, handX + 20, handY + (stage == 2 ? 2 : 0));
                    CombatArm(32, 26, 36, 23, handX, handY, style);
                    CombatArm(30, 26, 26, 22, handX - 9, handY, style);
                }
                else
                {
                    int sign = dir == 0 ? -1 : 1;
                    int handX = 43, handY = stage == 2 ? 25 + frame : 30;
                    int reach = stage == 2 ? 15 : 16 + extension;
                    DrawSpear(handX - 2, handY - sign * 12, handX, handY + sign * reach);
                    CombatArm(39, 25, 45, 28, handX, handY, style);
                    CombatArm(24, 25, 31, 22, handX - 1, handY - sign * 8, style);
                }
            }
            else
            {
                // Hurt is a standing recoil, not the legacy downed/work action.
                int lift = frame == 1 ? 5 : frame == 2 ? 3 : frame == 0 ? 1 : 0;
                CombatArm(dir == 1 ? 32 : 24, 26, 22, 26 + lift, 27, 29 + lift, style);
                CombatArm(dir == 1 ? 32 : 39, 26, 43, 22 + lift, 39, 27 + lift, style);
                L(dir == 1 ? 37 : 35, 31, dir == 1 ? 39 : 37, 32, C(171, 106, 97));
                int recoil = frame == 1 ? 3 : frame == 2 ? 2 : frame == 0 ? 1 : 0;
                ShiftDrawing(dir == 1 ? -recoil : 0, dir == 0 ? recoil : dir == 2 ? -recoil : 0);
            }
            if (direction == 3) { MirrorLeft(); }
        }

        private static void CombatArm(int sx, int sy, int ex, int ey, int hx, int hy, NpcLook style)
        {
            // Articulated sleeve and bare grip: weapons are held, not floating work effects.
            for (int offset = -1; offset <= 1; offset++)
            {
                L(sx + offset, sy, ex + offset, ey, Outline);
                L(ex + offset, ey, hx + offset, hy, Outline);
            }
            L(sx, sy, ex, ey, style.Coat); L(ex, ey, hx, hy, style.Coat);
            E(hx, hy, 2, 2, Outline); E(hx, hy, 1, 1, style.Skin);
        }

        private static void DrawCombatBow(int x, int y, int direction, int draw)
        {
            int ax, ay, bx, by, bendX, bendY, pullX, pullY;
            if (direction == 1)
            {
                ax = x - 1; ay = y - 10; bx = x - 1; by = y + 10;
                bendX = x + 3; bendY = y; pullX = x - draw; pullY = y;
            }
            else
            {
                int sign = direction == 0 ? -1 : 1;
                ax = x - 10; ay = y; bx = x + 10; by = y;
                bendX = x; bendY = y + sign * 3; pullX = x; pullY = y - sign * draw;
            }
            L(ax, ay, bendX, bendY, Outline); L(bendX, bendY, bx, by, Outline);
            L(ax + 1, ay, bendX + 1, bendY, SoilLight);
            L(bendX + 1, bendY, bx + 1, by, SoilLight);
            L(ax, ay, pullX, pullY, Cream); L(pullX, pullY, bx, by, Cream);
            R(bendX, bendY - 1, 2, 3, SoilDark);
        }

        private static void DrawSpear(int x, int y, int tipX, int tipY)
        {
            int dx = tipX - x, dy = tipY - y;
            int length = Math.Max(1, Math.Max(Math.Abs(dx), Math.Abs(dy)));
            int nx = Math.Abs(dx) >= Math.Abs(dy) ? 0 : 1;
            int ny = nx == 0 ? 1 : 0;
            int neckX = tipX - dx * 5 / length, neckY = tipY - dy * 5 / length;
            L(x, y - 1, neckX, neckY - 1, Outline); L(x, y, neckX, neckY, SoilLight);
            for (int offset = -2; offset <= 2; offset++)
            {
                L(neckX + nx * offset, neckY + ny * offset, tipX, tipY, offset < 0 ? Stone : StoneLight);
            }
            L(neckX, neckY, tipX, tipY, Cream);
            R(neckX - 1, neckY - 1, 3, 2, SoilDark);
        }

        private static void Arrow(int x, int y, int tipX, int tipY)
        {
            int dx = tipX - x, dy = tipY - y;
            int length = Math.Max(1, Math.Max(Math.Abs(dx), Math.Abs(dy)));
            int nx = Math.Abs(dx) >= Math.Abs(dy) ? 0 : 1;
            int ny = nx == 0 ? 1 : 0;
            L(x, y - 1, tipX, tipY - 1, Outline);
            L(x, y, tipX, tipY, SoilLight);
            int neckX = tipX - dx * 4 / length, neckY = tipY - dy * 4 / length;
            L(neckX + nx * 2, neckY + ny * 2, tipX, tipY, StoneLight);
            L(neckX - nx * 2, neckY - ny * 2, tipX, tipY, StoneLight);
            L(neckX, neckY, tipX, tipY, Cream);
            int featherX = x + dx * 4 / length, featherY = y + dy * 4 / length;
            L(x + nx * 2, y + ny * 2, featherX, featherY, Cream);
            L(x - nx * 2, y - ny * 2, featherX, featherY, StoneLight);
        }

        private static void ProjectileImpact(bool hit)
        {
            // Origin sits at the normal 64px entity pivot (32, 8), not sprite center.
            Color32 color = hit ? C(177, 105, 92) : C(174, 191, 197);
            for (int ray = 0; ray < 6; ray++)
            {
                int dx = ray % 3 - 1, dy = ray / 3 == 0 ? 1 : -1;
                L(32 + dx * 3, 8 + dy * 2, 32 + dx * 7, 8 + dy * 5, color);
            }
            P(32, 8, hit ? Cream : StoneLight);
            if (!hit) { L(25, 8, 29, 8, SoilLight); L(35, 8, 39, 8, SoilLight); }
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

        // combatBody suppresses only work arms/tools; every legacy call keeps the default pixels.
        private static void Person(int role, int look, int dir, int action, int frame, bool combatBody = false)
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
                PersonSide(role, look, action, frame, coat, accent, combatBody);
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
            if (!combatBody)
            {
                R(22, 18 + bob + arm, 4, 8, coat); R(39, 18 + bob - stride, 4, 8, coat);
                R(22, 16 + bob + arm, 4, 4, style.Skin); R(39, 16 + bob - stride, 4, 4, style.Skin);
            }
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
            if (role == 2)
            {
                R(25, 39 + bob, 14, 5, coat);
                if (!combatBody) { L(43, 14, 46, 25, Soil); L(46, 25, 43, 34, Soil); L(43, 14, 43, 34, Cream); }
            }
            if (role == 3)
            {
                R(27, 40 + bob, 11, 4, accent); R(30, 26 + bob, 3, 1, Cream);
                if (!combatBody) { E(43, 15, 3, 4, Soil); }
            }
            if (role == 4)
            {
                R(24, 39 + bob, 17, 3, accent); R(27, 42 + bob, 11, 3, Cream);
                if (!combatBody) { R(44, 13 + arm, 2, 12, Soil); R(41, 24 + arm, 8, 4, C(118, 132, 128)); }
            }
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

        private static void PersonSide(int role, int look, int action, int frame, Color32 coat, Color32 accent, bool combatBody = false)
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
            if (!combatBody)
            {
                L(32, 26 + bob, armX, 20 + bob, Outline);
                R(armX - 1, 19 + bob, 4, 4, skin);
            }

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
            if (role == 2)
            {
                R(26, 39 + bob, 13, 5, coat);
                if (!combatBody) { L(23, 15, 20, 26, Soil); L(20, 26, 23, 35, Soil); L(23, 15, 23, 35, Cream); }
            }
            if (role == 3)
            {
                R(28, 40 + bob, 10, 4, accent);
                if (!combatBody) { E(armX + 1, 16 + bob, 3, 4, Soil); }
            }
            if (role == 4)
            {
                R(25, 39 + bob, 16, 3, accent); R(28, 42 + bob, 10, 3, Cream);
                if (!combatBody) { R(armX + 3, 12, 2, 12, Soil); R(armX, 23, 8, 4, C(118, 132, 128)); }
            }
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
