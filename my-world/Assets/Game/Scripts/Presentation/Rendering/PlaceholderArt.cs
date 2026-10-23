using System;
using System.Collections.Generic;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MyAiWorld.Presentation.Rendering
{
    public static class PlaceholderArt
    {
        public const int PixelsPerUnit = 16;
        private const int TileSize = 16;

        private static readonly Dictionary<int, Tile> TileCache = new Dictionary<int, Tile>();
        private static readonly Dictionary<int, Sprite> ResourceCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> NpcCache = new Dictionary<int, Sprite>();
        private static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        private static readonly Color32[] ShirtColors =
        {
            new Color32(178, 75, 65, 255),
            new Color32(72, 116, 166, 255),
            new Color32(189, 144, 62, 255),
            new Color32(108, 72, 135, 255),
            new Color32(74, 139, 101, 255),
            new Color32(183, 112, 72, 255)
        };

        public static Tile GetTile(TileType type, int variant)
        {
            int key = ((int)type * 7) + (variant & 3);
            Tile tile;
            if (TileCache.TryGetValue(key, out tile))
            {
                return tile;
            }

            Sprite sprite = CreateTileSprite(type, variant);
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "RuntimeTile_" + type + "_" + variant;
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tile.hideFlags = HideFlags.DontSave;
            TileCache[key] = tile;
            return tile;
        }

        public static Sprite GetNpcSprite(int id)
        {
            int key = id % 97;
            Sprite sprite;
            if (NpcCache.TryGetValue(key, out sprite))
            {
                return sprite;
            }

            sprite = CreateNpcSprite(id);
            NpcCache[key] = sprite;
            return sprite;
        }

        public static Sprite GetResourceSprite(ItemKind item, bool depleted)
        {
            int key = (int)item * 2 + (depleted ? 1 : 0);
            Sprite sprite;
            if (ResourceCache.TryGetValue(key, out sprite))
            {
                return sprite;
            }

            var pixels = new Color32[TileSize * TileSize];
            Fill(pixels, Transparent);
            if (item == ItemKind.Water)
            {
                FillRect(pixels, 3, 3, 10, 9, new Color32(40, 43, 40, 255));
                FillRect(pixels, 4, 4, 8, 7, new Color32(161, 156, 128, 255));
                FillRect(pixels, 5, 5, 6, 5, depleted ? new Color32(77, 70, 56, 255) : new Color32(57, 145, 191, 255));
                if (!depleted)
                {
                    FillRect(pixels, 6, 8, 3, 1, new Color32(167, 224, 235, 255));
                }
            }
            else
            {
                FillRect(pixels, 7, 2, 2, 7, new Color32(74, 48, 31, 255));
                FillRect(pixels, 3, 5, 10, 5, new Color32(36, 66, 37, 255));
                FillRect(pixels, 5, 9, 6, 3, new Color32(48, 88, 42, 255));
                if (!depleted)
                {
                    Color32 berry = new Color32(226, 83, 76, 255);
                    FillRect(pixels, 4, 7, 2, 2, berry);
                    FillRect(pixels, 9, 6, 2, 2, berry);
                    FillRect(pixels, 7, 10, 2, 2, berry);
                }
            }

            sprite = CreateSprite("Resource_" + item + (depleted ? "_Empty" : ""), pixels, new Vector2(0.5f, 0.5f));
            ResourceCache[key] = sprite;
            return sprite;
        }

        public static Sprite GetAnimalSprite(AnimalSpecies species)
        {
            int key = 300 + (int)species;
            Sprite sprite;
            if (ResourceCache.TryGetValue(key, out sprite))
            {
                return sprite;
            }
            var pixels = new Color32[TileSize * TileSize];
            Fill(pixels, Transparent);
            bool rabbit = species == AnimalSpecies.Rabbit;
            Color32 coat = rabbit ? new Color32(221, 210, 179, 255) : new Color32(112, 77, 58, 255);
            FillRect(pixels, 3, 4, rabbit ? 8 : 10, 6, coat);
            FillRect(pixels, 9, 7, 4, 4, coat);
            FillRect(pixels, 4, 2, 2, 3, coat);
            FillRect(pixels, 10, 2, 2, 3, coat);
            if (rabbit)
            {
                FillRect(pixels, 9, 10, 2, 5, coat);
                FillRect(pixels, 12, 10, 1, 4, coat);
                FillRect(pixels, 2, 6, 2, 2, new Color32(244, 237, 219, 255));
            }
            else
            {
                FillRect(pixels, 12, 6, 3, 2, new Color32(169, 122, 93, 255));
                FillRect(pixels, 13, 5, 1, 2, new Color32(236, 223, 186, 255));
            }
            FillRect(pixels, 11, 9, 1, 1, new Color32(30, 31, 27, 255));
            sprite = CreateSprite("Animal_" + species, pixels, new Vector2(0.5f, 0.5f));
            ResourceCache[key] = sprite;
            return sprite;
        }

        public static Sprite GetCropSprite(CropStage stage, bool dry)
        {
            int key = 100 + (int)stage * 2 + (dry ? 1 : 0);
            Sprite sprite;
            if (ResourceCache.TryGetValue(key, out sprite))
            {
                return sprite;
            }

            var pixels = new Color32[TileSize * TileSize];
            Fill(pixels, Transparent);
            Color32 leaf = new Color32(126, 190, 74, 255);
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int x = 3 + col * 7;
                    int y = 3 + row * 6;
                    if (stage == CropStage.Mature)
                    {
                        FillRect(pixels, x, y, 4, 3, new Color32(239, 220, 170, 255));
                        FillRect(pixels, x + 1, y + 3, 2, 2, leaf);
                    }
                    else if (stage == CropStage.Growing)
                    {
                        FillRect(pixels, x, y + 1, 4, 2, leaf);
                        FillRect(pixels, x + 1, y, 1, 4, leaf);
                    }
                    else if (stage == CropStage.Seedling)
                    {
                        FillRect(pixels, x + 1, y, 1, 2, leaf);
                        FillRect(pixels, x, y + 1, 3, 1, leaf);
                    }
                }
            }

            if (dry && stage != CropStage.Empty)
            {
                FillRect(pixels, 12, 12, 3, 3, new Color32(244, 160, 51, 255));
            }

            sprite = CreateSprite("Crop_" + stage + (dry ? "_Dry" : ""), pixels, new Vector2(0.5f, 0.5f));
            ResourceCache[key] = sprite;
            return sprite;
        }

        public static Sprite GetStockpileSprite()
        {
            const int key = 200;
            Sprite sprite;
            if (ResourceCache.TryGetValue(key, out sprite))
            {
                return sprite;
            }

            var pixels = new Color32[TileSize * TileSize];
            Fill(pixels, Transparent);
            FillRect(pixels, 2, 2, 12, 10, new Color32(61, 40, 25, 255));
            FillRect(pixels, 3, 3, 10, 8, new Color32(179, 127, 66, 255));
            FillRect(pixels, 3, 5, 10, 1, new Color32(102, 68, 36, 255));
            FillRect(pixels, 3, 8, 10, 1, new Color32(102, 68, 36, 255));
            FillRect(pixels, 5, 3, 1, 8, new Color32(220, 175, 99, 255));
            FillRect(pixels, 10, 3, 1, 8, new Color32(220, 175, 99, 255));
            sprite = CreateSprite("SharedStockpile", pixels, new Vector2(0.5f, 0.5f));
            ResourceCache[key] = sprite;
            return sprite;
        }

        public static int VariantFor(int x, int y)
        {
            unchecked
            {
                int hash = (x * 73856093) ^ (y * 19349663) ^ 0x5f3759df;
                return hash & 1;
            }
        }

        private static Sprite CreateTileSprite(TileType type, int variant)
        {
            var pixels = new Color32[TileSize * TileSize];
            var random = new System.Random(((int)type + 1) * 7919 + variant * 104729);
            Color32 grass = new Color32(74, 118, 59, 255);
            Color32 grassLight = new Color32(88, 135, 68, 255);
            Color32 grassDark = new Color32(62, 101, 50, 255);

            switch (type)
            {
                case TileType.Water:
                    Fill(pixels, new Color32(54, 105, 132, 255));
                    for (int y = 1; y < TileSize; y += 4)
                    {
                        for (int x = 0; x < TileSize; x++)
                        {
                            if (((x + y + variant) % 5) < 3)
                            {
                                pixels[y * TileSize + x] = new Color32(82, 145, 166, 255);
                            }
                        }
                    }
                    break;

                case TileType.Dirt:
                case TileType.Path:
                    Color32 dirt = type == TileType.Path
                        ? new Color32(165, 132, 87, 255)
                        : new Color32(117, 84, 54, 255);
                    Color32 dirtDark = type == TileType.Path
                        ? new Color32(137, 104, 66, 255)
                        : new Color32(98, 68, 44, 255);
                    Fill(pixels, dirt);
                    for (int i = 0; i < 24; i++)
                    {
                        int x = random.Next(0, TileSize);
                        int y = random.Next(0, TileSize);
                        pixels[y * TileSize + x] = dirtDark;
                    }
                    break;

                case TileType.Rock:
                    FillGrass(pixels, grass, grassLight, grassDark, random);
                    DrawBoulder(pixels, random);
                    break;

                case TileType.House:
                    FillGrass(pixels, grass, grassLight, grassDark, random);
                    DrawHouse(pixels);
                    break;

                case TileType.Farmland:
                    DrawFarmland(pixels, random);
                    break;

                case TileType.Forest:
                    FillGrass(pixels, grass, grassLight, grassDark, random);
                    DrawTree(pixels, 4, 1, variant);
                    DrawTree(pixels, 10, 5, variant + 1);
                    break;

                default:
                    FillGrass(pixels, grass, grassLight, grassDark, random);
                    break;
            }

            return CreateSprite("RuntimeTile_" + type + "_" + variant, pixels, new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateNpcSprite(int id)
        {
            var pixels = new Color32[TileSize * TileSize];
            Fill(pixels, Transparent);

            Color32 outline = new Color32(39, 35, 32, 255);
            Color32 skin = new Color32(222, 179, 137, 255);
            Color32 hair = new Color32(65, 45, 32, 255);
            Color32 shirt = ShirtColors[Math.Abs(id) % ShirtColors.Length];
            Color32 pants = new Color32(64, 69, 80, 255);
            Color32 boot = new Color32(47, 38, 31, 255);

            FillRect(pixels, 5, 2, 6, 2, new Color32(20, 25, 25, 90));
            FillRect(pixels, 5, 4, 6, 6, outline);
            FillRect(pixels, 6, 5, 4, 4, shirt);
            FillRect(pixels, 5, 6, 1, 3, skin);
            FillRect(pixels, 10, 6, 1, 3, skin);
            FillRect(pixels, 6, 10, 4, 4, outline);
            FillRect(pixels, 7, 11, 2, 2, skin);
            FillRect(pixels, 7, 13, 2, 1, hair);
            FillRect(pixels, 6, 9, 4, 1, outline);
            FillRect(pixels, 6, 10, 1, 1, hair);
            FillRect(pixels, 9, 10, 1, 1, hair);
            FillRect(pixels, 6, 2, 2, 2, pants);
            FillRect(pixels, 9, 2, 2, 2, pants);
            FillRect(pixels, 5, 1, 3, 1, boot);
            FillRect(pixels, 9, 1, 3, 1, boot);

            return CreateSprite("RuntimeNpc_" + id, pixels, new Vector2(0.5f, 0.2f));
        }

        private static void FillGrass(
            Color32[] pixels,
            Color32 grass,
            Color32 grassLight,
            Color32 grassDark,
            System.Random random)
        {
            Fill(pixels, grass);
            for (int i = 0; i < 22; i++)
            {
                int x = random.Next(0, TileSize);
                int y = random.Next(0, TileSize);
                pixels[y * TileSize + x] = random.Next(0, 2) == 0 ? grassLight : grassDark;
            }

            for (int i = 0; i < 4; i++)
            {
                int x = random.Next(2, TileSize - 2);
                int y = random.Next(2, TileSize - 2);
                pixels[y * TileSize + x] = grassLight;
                pixels[y * TileSize + x + 1] = grassLight;
                pixels[(y + 1) * TileSize + x] = grassDark;
            }
        }

        private static void DrawTree(Color32[] pixels, int x, int y, int variant)
        {
            Color32 trunk = new Color32(87, 62, 40, 255);
            Color32 leafDark = new Color32(35, 75, 47, 255);
            Color32 leaf = new Color32(48, 98, 58, 255);
            Color32 leafLight = new Color32(67, 122, 69, 255);
            FillRect(pixels, x + 2, y, 2, 5, trunk);
            FillRect(pixels, x, y + 3, 6, 5, leafDark);
            FillRect(pixels, x + 1, y + 4, 4, 4, leaf);
            FillRect(pixels, x + 2, y + 6, 2, 2, leafLight);
            if ((variant & 1) == 0)
            {
                pixels[(y + 3) * TileSize + x] = leaf;
            }
        }


        private static void DrawHouse(Color32[] pixels)
        {
            Color32 wallDark = new Color32(87, 57, 38, 255);
            Color32 wall = new Color32(151, 101, 59, 255);
            Color32 roofDark = new Color32(92, 42, 38, 255);
            Color32 roof = new Color32(157, 68, 54, 255);
            Color32 window = new Color32(231, 190, 98, 255);
            Color32 door = new Color32(57, 37, 29, 255);

            FillRect(pixels, 2, 2, 12, 8, wallDark);
            FillRect(pixels, 3, 3, 10, 6, wall);
            FillRect(pixels, 1, 8, 14, 2, roofDark);
            FillRect(pixels, 2, 10, 12, 3, roof);
            FillRect(pixels, 3, 13, 10, 1, roofDark);
            FillRect(pixels, 7, 2, 3, 4, door);
            FillRect(pixels, 4, 5, 2, 2, window);
            FillRect(pixels, 10, 5, 2, 2, window);
            for (int x = 3; x < 14; x += 4)
            {
                pixels[10 * TileSize + x] = roofDark;
            }
        }

        private static void DrawFarmland(Color32[] pixels, System.Random random)
        {
            Color32 soil = new Color32(105, 73, 46, 255);
            Color32 soilDark = new Color32(80, 55, 36, 255);
            Fill(pixels, soil);
            for (int y = 2; y < TileSize; y += 4)
            {
                FillRect(pixels, 0, y, TileSize, 1, soilDark);
            }

        }

        private static void DrawBoulder(Color32[] pixels, System.Random random)
        {
            Color32 dark = new Color32(81, 85, 84, 255);
            Color32 stone = new Color32(119, 124, 119, 255);
            Color32 light = new Color32(151, 154, 144, 255);
            FillRect(pixels, 4, 5, 8, 6, dark);
            FillRect(pixels, 5, 6, 6, 5, stone);
            FillRect(pixels, 6, 8, 3, 2, light);
            for (int i = 0; i < 7; i++)
            {
                int x = random.Next(5, 11);
                int y = random.Next(6, 11);
                pixels[y * TileSize + x] = dark;
            }
        }

        private static Sprite CreateSprite(string name, Color32[] pixels, Vector2 pivot)
        {
            var texture = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.DontSave;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, TileSize, TileSize),
                pivot,
                PixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static void Fill(Color32[] pixels, Color32 color)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
        }

        private static void FillRect(Color32[] pixels, int x, int y, int width, int height, Color32 color)
        {
            for (int yy = y; yy < y + height; yy++)
            {
                if (yy < 0 || yy >= TileSize)
                {
                    continue;
                }

                for (int xx = x; xx < x + width; xx++)
                {
                    if (xx < 0 || xx >= TileSize)
                    {
                        continue;
                    }

                    pixels[yy * TileSize + xx] = color;
                }
            }
        }
    }
}
