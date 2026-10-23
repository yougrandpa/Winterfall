using System.Collections.Generic;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.Atmosphere
{
    // Presentation-only camp props around the shared stockpile. Simulation state,
    // walkability and facility placement are untouched; occupied cells simply hide
    // a prop instead of destroying it so renderer counts stay stable across loads.
    public sealed class CampDressing : MonoBehaviour
    {
        private sealed class Placed
        {
            public GameObject Object;
            public GridPos Cell;
        }

        private readonly List<Placed> _placed = new List<Placed>();
        private readonly HashSet<int> _occupied = new HashSet<int>();
        private PastoralArtConfig _art;
        private GameSession _session;
        private Sprite _coldStove;
        private long _checkedTick = -1;
        private int _checkedRevision = -1;

        public int PropCount => _placed.Count;

        public void Initialize(GameSession session, SimulationRunner runner, PastoralArtConfig art, CanopyFader canopy)
        {
            _session = session;
            _art = art;
            Build(canopy);
        }

        private void Build(CanopyFader canopy)
        {
            WorldReadModel model = _session.ReadModel;
            int width = model.Width, height = model.Height;
            GridPos center = model.StockpileCell;
            var candidates = new List<GridPos>();
            for (int dy = -6; dy <= 6; dy++)
            for (int dx = -6; dx <= 6; dx++)
            {
                int x = center.X + dx, y = center.Y + dy;
                if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1) { continue; }
                if (dx * dx + dy * dy < 4 || dx * dx + dy * dy > 36) { continue; }
                if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) { continue; }
                TileType tile = model.MapTiles[y * width + x];
                if (tile != TileType.Grass && tile != TileType.Dirt) { continue; }
                if (IsReserved(model, new GridPos(x, y))) { continue; }
                candidates.Add(new GridPos(x, y));
            }
            candidates.Sort(delegate(GridPos a, GridPos b)
            {
                int da = Mathf.Abs(a.X - center.X) + Mathf.Abs(a.Y - center.Y);
                int db = Mathf.Abs(b.X - center.X) + Mathf.Abs(b.Y - center.Y);
                if (da != db) { return da.CompareTo(db); }
                int ha = _art.Variant(a.X * 3 + 11, a.Y * 5 + 7, 10000);
                int hb = _art.Variant(b.X * 3 + 11, b.Y * 5 + 7, 10000);
                if (ha != hb) { return ha.CompareTo(hb); }
                return a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
            });

            // Only retain the stone/log base of the decorative fire. No flame, sparks or light.
            Sprite source = _art.Get("campfire_0");
            if (source != null)
            {
                Rect rect = source.rect;
                rect.height *= 17f / 64f;
                _coldStove = Sprite.Create(source.texture, rect,
                    new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                _coldStove.name = "CampColdStove (runtime)";
            }
            var used = new List<GridPos>();
            Place("campfire_0", "cold_stove", candidates, used, 1.6f, canopy, false);
            Place("tent_0", "tent", candidates, used, 1.8f, canopy, true);
            Place("tent_1", "tent", candidates, used, 1.8f, canopy, true);
            Place("lean_to", "lean_to", candidates, used, 1.8f, canopy, true);
            Place("woodpile", "woodpile", candidates, used, 1.6f, canopy, false);
            Place("crate_0", "crate", candidates, used, 1.6f, canopy, false);
            Place("crate_1", "crate", candidates, used, 1.6f, canopy, false);
            Place("barrel", "barrel", candidates, used, 1.6f, canopy, false);
            Place("fish_rack", "fish_rack", candidates, used, 1.6f, canopy, false);
        }

        private void Place(string key, string label, List<GridPos> candidates, List<GridPos> used,
            float spacing, CanopyFader canopy, bool shelter)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                GridPos cell = candidates[i];
                bool taken = false;
                for (int j = 0; j < used.Count; j++)
                {
                    float dx = used[j].X - cell.X, dy = used[j].Y - cell.Y;
                    if (dx * dx + dy * dy < spacing * spacing) { taken = true; break; }
                }
                if (taken) { continue; }
                Sprite sprite = label == "cold_stove" ? _coldStove : _art.Get(key);
                if (sprite == null) { return; }
                var go = new GameObject(label + "_" + _placed.Count, typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0f);
                var renderer = go.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = CreatureMotion.SortOrder(go.transform.position.y);
                _placed.Add(new Placed { Object = go, Cell = cell });
                used.Add(cell);
                if (shelter && canopy != null) { canopy.Register(renderer); }
                return;
            }
        }

        private void Update()
        {
            RefreshConflicts();
        }

        private void OnDestroy()
        {
            if (_coldStove != null) { Destroy(_coldStove); }
        }

        private void RefreshConflicts()
        {
            if (_session == null) { return; }
            WorldReadModel model = _session.ReadModel;
            if (model.Tick == _checkedTick && _checkedRevision == _session.WorldRevision) { return; }
            _checkedTick = model.Tick;
            _checkedRevision = _session.WorldRevision;
            _occupied.Clear();
            foreach (ShelterSnapshot shelter in model.Survival.Shelters)
            {
                _occupied.Add(shelter.Cell.Y * model.Width + shelter.Cell.X);
                _occupied.Add(shelter.Entrance.Y * model.Width + shelter.Entrance.X);
            }
            for (int i = 0; i < model.Survival.Facilities.Count; i++)
            {
                FacilitySnapshot facility = model.Survival.Facilities[i];
                _occupied.Add(facility.Cell.Y * model.Width + facility.Cell.X);
            }
            for (int i = 0; i < model.ResourceNodes.Count; i++)
            {
                ResourceNodeSnapshot node = model.ResourceNodes[i];
                _occupied.Add(node.Cell.Y * model.Width + node.Cell.X);
            }
            for (int i = 0; i < model.FarmPlots.Count; i++)
            {
                FarmPlotSnapshot plot = model.FarmPlots[i];
                _occupied.Add(plot.Cell.Y * model.Width + plot.Cell.X);
            }
            for (int i = 0; i < _placed.Count; i++)
            {
                Placed placed = _placed[i];
                if (placed.Object == null) { continue; }
                bool conflict = _occupied.Contains(placed.Cell.Y * model.Width + placed.Cell.X);
                if (placed.Object.activeSelf == conflict) { placed.Object.SetActive(!conflict); }
            }
        }

        private static bool IsReserved(WorldReadModel model, GridPos cell)
        {
            if (cell == model.StockpileCell) { return true; }
            foreach (ShelterSnapshot shelter in model.Survival.Shelters)
            {
                if (shelter.Cell == cell || shelter.Entrance == cell) { return true; }
            }
            for (int i = 0; i < model.Survival.Facilities.Count; i++)
            {
                if (model.Survival.Facilities[i].Cell == cell) { return true; }
            }
            for (int i = 0; i < model.ResourceNodes.Count; i++)
            {
                if (model.ResourceNodes[i].Cell == cell) { return true; }
            }
            for (int i = 0; i < model.FarmPlots.Count; i++)
            {
                if (model.FarmPlots[i].Cell == cell) { return true; }
            }
            for (int i = 0; i < model.Npcs.Count; i++)
            {
                if (model.Npcs[i].Cell == cell) { return true; }
            }
            return false;
        }
    }
}
