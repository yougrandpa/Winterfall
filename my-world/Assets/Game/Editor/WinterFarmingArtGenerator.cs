using System;
using System.Linq;
using MyAiWorld.Presentation.Rendering;
using UnityEditor;
using UnityEngine;

namespace MyAiWorld.EditorTools
{
    public static partial class PastoralArtGenerator
    {
        [MenuItem("Tools/My AI World/Art/Generate Winter Farming Art")]
        public static void GenerateWinterFarmingArt()
        {
            Targets.Clear();
            var atlas = new AtlasTarget("WinterFarmingAtlas.png", 512, 512, 8, FilterMode.Point);
            Targets.Add(atlas); Use(atlas);
            for (int crop = 1; crop <= 3; crop++)
            for (int stage = 0; stage <= 3; stage++)
            for (int damage = 0; damage < 3; damage++)
            {
                Begin("winter_crop_" + crop + "_" + stage + "_" + damage, 32, true);
                Color32 leaf = damage == 2 ? C(128, 106, 73) : damage == 1 ? C(110, 126, 90) : crop == 2 ? C(85, 123, 112) : crop == 3 ? C(115, 146, 92) : C(66, 119, 79);
                if (stage == 0)
                {
                    if (damage == 2) { L(9, 8, 12, 17, SoilLight); L(20, 10, 17, 18, SoilLight); E(10, 13, 3, 1, Soil); E(20, 13, 3, 1, Soil); }
                    continue;
                }
                int radius = stage == 1 ? 3 : stage == 2 ? 5 : 7;
                foreach (int x in new[] { 9, 23 })
                foreach (int y in new[] { 9, 22 })
                {
                    E(x, y - 1, radius, 2, SoilDark);
                    L(x, y - 2, x, y + radius, GreenDark);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        E(x + side * radius / 2, y + radius / 2, radius / 2 + 1, crop == 3 ? 2 : radius / 2 + 1, leaf);
                        L(x, y, x + side * radius, y + radius / 2, GreenLight);
                        if (crop == 2 && stage > 1) { P(x + side * radius, y + radius / 2 + 1, Cream); P(x + side * (radius - 1), y + radius / 2 + 3, leaf); }
                    }
                    E(x, y + radius, crop == 3 ? 2 : 1, 2, leaf);
                }
            }
            for (int complete = 0; complete < 2; complete++)
            for (int snow = 0; snow < 3; snow++)
            {
                Begin("winter_bed_" + complete + "_" + snow, 32, true);
                R(0, 1, 32, 2, Outline); R(1, 3, 30, 2, SoilLight);
                R(0, 4, 2, 26, Soil); R(30, 4, 2, 26, Soil); R(0, 29, 32, 2, SoilLight);
                if (complete == 1)
                {
                    // Raised insulating rims and a pulled-back daytime fibre cover.
                    R(2, 26, 28, 3, Cream); L(3, 26, 29, 26, SoilLight);
                    for (int x = 4; x < 30; x += 4) { L(x, 27, x + 1, 28, SoilLight); }
                }
                if (snow > 0)
                { R(0, 29, 32, 3, FogColor); R(0, 3, 2, 26, Cream); R(30, 3, 2, 26, Cream); }
                if (snow == 2)
                { E(10, 17, 9, 5, FogColor); E(23, 13, 8, 4, Cream); }
            }
            for (int heat = 0; heat < 3; heat++)
            {
                Begin("winter_heater_" + heat, 32, true);
                R(3, 8, 10, 7, Outline); R(4, 9, 8, 5, heat == 0 ? Soil : Stone);
                R(6, 10, 4, 3, heat == 2 ? Ember : Deep); R(5, 15, 3, 6, StoneLight);
                if (heat == 2) { P(7, 11, EmberCore); E(6, 23, 2, 1, FogColor); }
            }
            for (int crop = 1; crop <= 3; crop++)
            {
                Begin("winter_seed_" + crop, 32, true);
                R(7, 4, 18, 23, Outline); R(8, 5, 16, 21, Cream); R(8, 24, 16, 2, SoilLight);
                Color32 leaf = crop == 2 ? C(85, 123, 112) : crop == 3 ? C(115, 146, 92) : C(66, 119, 79);
                E(13, 16, crop == 2 ? 5 : 3, 3, leaf); E(19, 16, 4, crop == 3 ? 2 : 4, leaf);
                L(16, 11, 16, 20, GreenDark);
                for (int i = 0; i < crop; i++) { E(12 + i * 4, 8, 1, 1, SoilDark); }
            }
            Save(atlas);
            var config = AssetDatabase.LoadAssetAtPath<PastoralArtConfig>(Root + "PastoralArt.asset");
            config.sprites = config.sprites.Where(e => !e.key.StartsWith("winter_crop_", StringComparison.Ordinal) && !e.key.StartsWith("winter_bed_", StringComparison.Ordinal) && !e.key.StartsWith("winter_heater_", StringComparison.Ordinal) && !e.key.StartsWith("winter_seed_", StringComparison.Ordinal))
                .Concat(AssetDatabase.LoadAllAssetsAtPath(Root + atlas.FileName).OfType<Sprite>().Select(sprite => new PastoralArtConfig.Entry { key = sprite.name, sprite = sprite }))
                .OrderBy(e => e.key, StringComparer.Ordinal).ToArray();
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); _target = null;
        }
    }
}
