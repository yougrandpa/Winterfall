using System;
using System.Collections.Generic;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation.Atmosphere;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MyAiWorld.Presentation.Rendering
{
    public sealed class WorldPresenter : MonoBehaviour
    {
        private readonly Dictionary<EntityId, NpcView> _views = new Dictionary<EntityId, NpcView>();
        private readonly List<NpcView> _viewList = new List<NpcView>();

        private readonly Dictionary<int, SpriteRenderer> _resourceViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<EntityId, AnimalView> _animalViews = new Dictionary<EntityId, AnimalView>();
        private readonly Dictionary<int, SpriteRenderer> _cropViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _facilityViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _groundViews = new Dictionary<int, SpriteRenderer>();
        private Transform _resourceRoot;
        private PastoralArtConfig _art;
        private readonly List<AnimalView> _fadingAnimals = new List<AnimalView>();
        private readonly List<Tile> _ownedTiles = new List<Tile>();
        public PastoralArtConfig Art => _art;
        private int _worldRevision;
        private bool _restoringWorldViews;
        private long _resourceTick = -1;
        private Tilemap _ground;
        private readonly Tile[,] _waterTiles = new Tile[4, 4];
        private int _waterFrame;


        private GameSession _session;
        private SimulationRunner _runner;
        private Transform _npcRoot;
        private EntityId? _selected;
        private AtmospherePresenter _atmosphere;
        private CanopyFader _canopyFader;
        private CampDressing _campDressing;

        public int NpcViewCount => _views.Count;
        public int AnimalViewCount => _animalViews.Count;
        public int ResourceNodeViewCount => _resourceViews.Count;
        public int FarmPlotViewCount => _cropViews.Count;
        public int MapWidth => _session != null ? _session.ReadModel.Width : 0;
        public int MapHeight => _session != null ? _session.ReadModel.Height : 0;
        public EntityId? Selected => _selected;
        public AtmospherePresenter Atmosphere => _atmosphere;
        public CanopyFader Canopy => _canopyFader;
        public CampDressing Camp => _campDressing;

        public void Initialize(GameSession session, SimulationRunner runner)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));

            _worldRevision = session.WorldRevision;
            _art = PastoralArtConfig.Load();
            BuildAtmosphere();
            BuildMap();
            BuildNpcViews();
            BuildResourceViews();
        }

        private void BuildAtmosphere()
        {
            AtmosphereProfile profile = AtmosphereProfile.Load();
            var atmosphereObject = new GameObject("Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            _atmosphere = atmosphereObject.AddComponent<AtmospherePresenter>();
            _atmosphere.Initialize(_session, _runner, _art, profile);

            var canopyObject = new GameObject("CanopyFader");
            canopyObject.transform.SetParent(transform, false);
            _canopyFader = canopyObject.AddComponent<CanopyFader>();
            _canopyFader.Initialize(profile);
        }

        public void SetSelected(EntityId? selected)
        {
            _selected = selected;
            foreach (KeyValuePair<EntityId, NpcView> pair in _views)
            {
                bool isSelected = selected.HasValue && pair.Key == selected.Value;
                pair.Value.SetSelected(isSelected);
            }
        }

        public Vector3 CellCenter(GridPos cell)
        {
            return new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0f);
        }

        private void LateUpdate()
        {
            if (_session == null || _runner == null)
            {
                return;
            }

            if (_worldRevision != _session.WorldRevision) { ResetWorldViews(); }
            WorldReadModel model = _session.ReadModel;
            IReadOnlyList<NpcSnapshot> npcs = model.Npcs;
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcSnapshot snapshot = npcs[i];
                NpcView view;
                if (!_views.TryGetValue(snapshot.Id, out view))
                {
                    view = CreateView(snapshot);
                    view.SetCell(snapshot.Cell, true, model.Tick);
                }
                view.Present(snapshot, model.Tick, _runner.TickAlpha, _runner.AnimationTime);
            }


            RefreshResourceViews(model);
            foreach (AnimalView animal in _animalViews.Values) { animal.Present(_runner.TickAlpha, _runner.AnimationTime); }
            for (int i = _fadingAnimals.Count - 1; i >= 0; i--)
            {
                if (_fadingAnimals[i].Present(_runner.TickAlpha, _runner.AnimationTime))
                {
                    Destroy(_fadingAnimals[i].gameObject); _fadingAnimals.RemoveAt(i);
                }
            }
            UpdateWater();
            if (_atmosphere != null) { _atmosphere.Present(model.Date, _runner.AnimationTime); }
            if (_canopyFader != null)
            {
                _canopyFader.Present(_runner.AnimationTime, _viewList, _selected);
                bool silhouette = _selected.HasValue && _canopyFader.SelectedOccluded;
                for (int i = 0; i < _viewList.Count; i++)
                {
                    NpcView view = _viewList[i];
                    view.SetSilhouetteVisible(silhouette && _selected.HasValue && view.Id == _selected.Value);
                }
            }
        }

        private void ResetWorldViews()
        {
            _worldRevision = _session.WorldRevision;
            foreach (string name in new[] { "WorldGrid", "NpcViews", "Resources" })
            {
                Transform child = transform.Find(name);
                if (child != null) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            }
            foreach (Tile tile in _ownedTiles) { Destroy(tile); }
            _ownedTiles.Clear(); _views.Clear(); _viewList.Clear(); _animalViews.Clear(); _resourceViews.Clear(); _cropViews.Clear(); _fadingAnimals.Clear(); _facilityViews.Clear(); _groundViews.Clear();
            _resourceTick = -1; _selected = null;
            if (_canopyFader != null) { _canopyFader.Clear(); }
            if (_atmosphere != null) { _atmosphere.SetCampfire(null); }
            _campDressing = null;
            _restoringWorldViews = true;
            BuildMap(); BuildNpcViews(); BuildResourceViews();
            _restoringWorldViews = false;
        }

        private void BuildMap()
        {
            if (_session == null)
            {
                return;
            }

            WorldReadModel model = _session.ReadModel;
            if (_canopyFader != null) { _canopyFader.Clear(); }
            var gridObject = new GameObject("WorldGrid");
            gridObject.transform.SetParent(transform, false);
            var grid = gridObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var groundObject = new GameObject("Ground");
            groundObject.transform.SetParent(gridObject.transform, false);
            var tilemap = groundObject.AddComponent<Tilemap>();
            var renderer = groundObject.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = -10000;
            renderer.mode = TilemapRenderer.Mode.Chunk;

            var edgeObject = new GameObject("Edges", typeof(Tilemap), typeof(TilemapRenderer));
            edgeObject.transform.SetParent(gridObject.transform, false);
            edgeObject.GetComponent<TilemapRenderer>().sortingOrder = -9999;
            Tilemap edges = edgeObject.GetComponent<Tilemap>();
            var objects = new GameObject("Landmarks").transform;
            objects.SetParent(gridObject.transform, false);
            var decoration = new GameObject("Decorations").transform;
            decoration.SetParent(gridObject.transform, false);
            var cache = new Dictionary<string, Tile>();
            _ground = tilemap;
            _waterFrame = 0;
            for (int variant = 0; variant < 4; variant++)
            for (int frame = 0; frame < 4; frame++)
            {
                _waterTiles[variant, frame] = ArtTile("water_" + variant + "_" + frame, cache);
            }
            int width = model.Width, height = model.Height;
            var tiles = new TileBase[width * height];
            var edgeTiles = new TileBase[width * height];
            int[] dx = { 0, 1, 0, -1 }, dy = { 1, 0, -1, 0 };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                TileType type = model.MapTiles[index];
                tiles[index] = type == TileType.Water ? _waterTiles[_art.Variant(x, y, 4), 0] :
                    ArtTile("ground_" + (int)type + "_" + _art.Variant(x, y, 4), cache);
                int mask = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) { continue; }
                    TileType neighbor = model.MapTiles[ny * width + nx];
                    if (type == TileType.Water ? neighbor != TileType.Water :
                        neighbor == TileType.Grass || neighbor == TileType.Forest) { mask |= 1 << d; }
                }
                if (mask != 0 && (type == TileType.Path || type == TileType.Dirt || type == TileType.Water))
                {
                    edgeTiles[index] = ArtTile("edge_" + (type == TileType.Water ? 2 : type == TileType.Path ? 1 : 0) + "_" + mask, cache);
                }
                int treeVariant = _art.Variant(x, y, 6);
                string prop = type == TileType.Forest ? (_art.Variant(x + 5, y + 93, 17) == 0
                        ? "snag_" + _art.Variant(x + 2, y + 7, 2) : "tree_" + treeVariant) :
                    type == TileType.Rock ? "rock_" + _art.Variant(x, y, 2) :
                    type == TileType.House ? "cottage_" + _art.Variant(x, y, 3) : null;
                foreach (var facility in model.Survival.Facilities) { if (facility.Cell == new GridPos(x, y)) { prop = null; break; } }
                if (prop != null)
                {
                    // Thin the canopy with a second visual seed so all tree variants
                    // remain in play; every simulated forest tile stays intact.
                    if (type != TileType.Forest || _art.Variant(x + 73, y + 19, 3) != 0)
                    {
                        SpriteRenderer landmark = Landmark(objects, prop, new GridPos(x, y));
                        if (type == TileType.Forest)
                        {
                            TintDepth(landmark, x, y, width, height);
                            if (_canopyFader != null) { _canopyFader.Register(landmark); }
                        }
                    }
                    else if (type == TileType.Forest)
                    {
                        var undergrowth = Landmark(decoration, new[] { "fern", "dead_log", "stump", "heather" }[_art.Variant(x + 41, y + 13, 4)], new GridPos(x, y));
                        undergrowth.sortingOrder = -9000;
                    }
                }
                else if (type == TileType.Water && _art.Variant(x + 31, y, 9) == 0)
                {
                    var decor = Landmark(decoration, mask != 0 ? "reeds" : "lilies", new GridPos(x, y));
                    decor.sortingOrder = -9000;
                }
                else if (type == TileType.Water && mask != 0 && _art.Variant(x + 5, y + 7, 7) == 0)
                {
                    var pebbles = Landmark(decoration, "shore_pebbles", new GridPos(x, y));
                    pebbles.sortingOrder = -9000;
                }
                else if (type == TileType.Grass && _art.Variant(x, y, 13) == 0)
                {
                    var decor = Landmark(decoration, new[] { "flower", "tuft", "pebbles", "cold_flower", "heather" }[_art.Variant(x + 17, y, 5)], new GridPos(x, y));
                    decor.sortingOrder = -9000;
                }
            }
            var bounds = new BoundsInt(0, 0, 0, width, height, 1);
            tilemap.SetTilesBlock(bounds, tiles); edges.SetTilesBlock(bounds, edgeTiles);
            BuildCamp(gridObject.transform);
        }

        private void TintDepth(SpriteRenderer renderer, int x, int y, int width, int height)
        {
            float nx = (x + 0.5f) / width * 2f - 1f;
            float ny = (y + 0.5f) / height * 2f - 1f;
            float distance = Mathf.Sqrt(nx * nx + ny * ny);
            float tint = Mathf.Clamp01((distance - 0.42f) / 0.58f) * 0.55f;
            renderer.color = Color.Lerp(Color.white, new Color(0.62f, 0.72f, 0.79f, 1f), tint);
        }

        private void BuildCamp(Transform grid)
        {
            var campObject = new GameObject("CampProps");
            campObject.transform.SetParent(grid, false);
            _campDressing = campObject.AddComponent<CampDressing>();
            _campDressing.Initialize(_session, _runner, _art, _canopyFader);
            if (_atmosphere != null) { _atmosphere.SetCampfire(_campDressing.CampfirePosition); }
        }

        private void UpdateWater()
        {
            int frame = Mathf.FloorToInt(_runner.AnimationTime * 3f) % 4;
            if (frame == _waterFrame) { return; }
            for (int variant = 0; variant < 4; variant++)
            {
                _ground.SwapTile(_waterTiles[variant, _waterFrame], _waterTiles[variant, frame]);
            }
            _waterFrame = frame;
        }

        public NpcView FindNpcView(EntityId id)
        {
            return _views.TryGetValue(id, out NpcView view) ? view : null;
        }

        public NpcView PickNpc(Vector2 worldPoint)
        {
            NpcView best = null;
            foreach (NpcView view in _views.Values)
            {
                if (!view.Visible || !view.HitBounds.Contains(worldPoint)) { continue; }
                if (best == null || view.SortingOrder > best.SortingOrder ||
                    (view.SortingOrder == best.SortingOrder && view.Id.Value < best.Id.Value))
                {
                    best = view;
                }
            }
            return best;
        }

        private Tile ArtTile(string key, Dictionary<string, Tile> cache)
        {
            Tile tile;
            if (!cache.TryGetValue(key, out tile))
            {
                tile = ScriptableObject.CreateInstance<Tile>(); tile.sprite = _art.Get(key);
                tile.colliderType = Tile.ColliderType.None; cache.Add(key, tile); _ownedTiles.Add(tile);
            }
            return tile;
        }
        private SpriteRenderer Landmark(Transform root, string key, GridPos cell)
        {
            var go = new GameObject(key, typeof(SpriteRenderer)); go.transform.SetParent(root, false);
            go.transform.position = CellCenter(cell);
            var sprite = go.GetComponent<SpriteRenderer>(); sprite.sprite = _art.Get(key);
            sprite.sortingOrder = CreatureMotion.SortOrder(go.transform.position.y);
            return sprite;
        }
        private void OnDestroy()
        {
            foreach (Tile tile in _ownedTiles) { Destroy(tile); }
        }

        private void BuildNpcViews()
        {
            var root = new GameObject("NpcViews");
            root.transform.SetParent(transform, false);
            _npcRoot = root.transform;

            IReadOnlyList<NpcSnapshot> npcs = _session.ReadModel.Npcs;
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcSnapshot snapshot = npcs[i];
                NpcView view = CreateView(snapshot);
                view.SetCell(snapshot.Cell, true, _session.ReadModel.Tick);
                view.ApplyAlpha(1f);
            }
        }

        private NpcView CreateView(NpcSnapshot snapshot)
        {
            var viewObject = new GameObject("Npc_" + snapshot.Id.Value);
            viewObject.transform.SetParent(_npcRoot, false);
            var view = viewObject.AddComponent<NpcView>();
            int role = NpcView.RoleIndex(snapshot.Role);
            int look = NpcView.LookIndex(snapshot.Id);
            view.Initialize(snapshot.Id, _art.Get("npc_" + role + "_" + look + "_0_0_0"), _npcRoot, look);
            view.Configure(_art, snapshot.Role, _runner.AnimationTime - (_restoringWorldViews || _session.ReadModel.Tick == GameCalendar.StartTick ? 1f : 0f), snapshot.Health);
            _views[snapshot.Id] = view;
            _viewList.Add(view);
            return view;
        }

        private void BuildResourceViews()
        {
            var root = new GameObject("Resources");
            root.transform.SetParent(transform, false);
            _resourceRoot = root.transform;
            CreateResourceView("SharedStockpile", _session.ReadModel.StockpileCell, _art.Get("stock"));
            RefreshResourceViews(_session.ReadModel);
        }

        private SpriteRenderer CreateResourceView(string label, GridPos cell, Sprite sprite)
        {
            var marker = new GameObject(label, typeof(SpriteRenderer));
            marker.transform.SetParent(_resourceRoot, false);
            marker.transform.position = CellCenter(cell);
            var renderer = marker.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = CreatureMotion.SortOrder(marker.transform.position.y);
            return renderer;
        }

        private void RefreshResourceViews(WorldReadModel model)
        {
            if (_resourceRoot == null || _resourceTick == model.Tick)
            {
                return;
            }

            _resourceTick = model.Tick;
            foreach (var facility in model.Survival.Facilities)
            {
                if (!_facilityViews.TryGetValue(facility.Id, out SpriteRenderer view)) { view = CreateResourceView("Facility_" + facility.Id, facility.Cell, null); _facilityViews.Add(facility.Id, view); }
                view.sprite = _art.Get("facility_" + (int)facility.Kind + "_" + (facility.Complete ? 1 : 0));
                view.color = facility.Condition <= 30 ? new Color(1f, 0.7f, 0.55f) : Color.white;
            }
            var remaining = new HashSet<int>();
            foreach (var ground in model.Survival.Ground)
            {
                remaining.Add(ground.Id);
                if (!_groundViews.TryGetValue(ground.Id, out SpriteRenderer view)) { view = CreateResourceView("GroundStock_" + ground.Id, ground.Cell, _art.Get("ground_stock")); _groundViews.Add(ground.Id, view); }
            }
            var removedGround = new List<int>();
            foreach (var pair in _groundViews) { if (!remaining.Contains(pair.Key)) { Destroy(pair.Value.gameObject); removedGround.Add(pair.Key); } }
            foreach (int id in removedGround) { _groundViews.Remove(id); }

            var living = new HashSet<EntityId>();
            foreach (AnimalSnapshot animal in model.EcologyEconomy.Animals)
            {
                living.Add(animal.Id);
                AnimalView view;
                if (!_animalViews.TryGetValue(animal.Id, out view))
                {
                    var go = new GameObject("Animal_" + animal.Id.Value);
                    go.transform.SetParent(_resourceRoot, false); view = go.AddComponent<AnimalView>();
                    view.Initialize(animal, _art, model.Tick, _runner.AnimationTime, _restoringWorldViews || model.Tick == GameCalendar.StartTick);
                    _animalViews.Add(animal.Id, view);
                }
                else { view.Receive(animal, model.Tick, _runner.AnimationTime); }
            }
            var removed = new List<EntityId>();
            foreach (var pair in _animalViews)
            {
                if (!living.Contains(pair.Key))
                {
                    pair.Value.BeginDeath(_runner.AnimationTime); _fadingAnimals.Add(pair.Value); removed.Add(pair.Key);
                }
            }
            foreach (EntityId id in removed) { _animalViews.Remove(id); }
            for (int i = 0; i < model.ResourceNodes.Count; i++)
            {
                ResourceNodeSnapshot node = model.ResourceNodes[i];
                string key = node.Item == ItemKind.Water ? "well" :
                    node.Amount <= 0 ? "berries_empty" : "berries_" + _art.Variant(node.Id, 0, 2);
                Sprite sprite = _art.Get(key);
                SpriteRenderer renderer;
                if (!_resourceViews.TryGetValue(node.Id, out renderer))
                {
                    renderer = CreateResourceView("Resource_" + node.Id + "_" + node.Item, node.Cell, sprite);
                    _resourceViews.Add(node.Id, renderer);
                }

                if (node.Item >= ItemKind.RawWater && node.Item <= ItemKind.Herb) { sprite = _art.Get("resource_" + (int)node.Item); }
                renderer.sprite = sprite;
                renderer.color = node.ReservedBy.IsNone ? Color.white : new Color(1f, 0.9f, 0.7f);
            }

            for (int i = 0; i < model.FarmPlots.Count; i++)
            {
                FarmPlotSnapshot plot = model.FarmPlots[i];
                Sprite sprite = _art.Get((plot.Crop == CropType.Wheat || plot.Crop == CropType.Bean ? "crop_type_" + (int)plot.Crop + "_" : "crop_") + (int)plot.Stage + "_" + (plot.Moisture <= 0 ? 1 : 0));
                SpriteRenderer renderer;
                if (!_cropViews.TryGetValue(plot.Id, out renderer))
                {
                    renderer = CreateResourceView("Crop_" + plot.Id, plot.Cell, sprite);
                    _cropViews.Add(plot.Id, renderer);
                }

                renderer.sortingOrder = -8000;
                renderer.sprite = sprite;
                renderer.color = plot.ReservedBy.IsNone ? Color.white : new Color(1f, 0.9f, 0.7f);
            }
        }

    }
}
