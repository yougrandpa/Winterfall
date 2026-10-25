using System;
using System.Collections.Generic;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation.Atmosphere;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
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
        private readonly Dictionary<int, SpriteRenderer> _carcassViews = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _lootViews = new Dictionary<int, SpriteRenderer>();
        public int CarcassViewCount => _carcassViews.Count;
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
        private readonly Dictionary<long, ProjectileView> _projectileViews = new Dictionary<long, ProjectileView>();
        private readonly List<ProjectileView> _impactViews = new List<ProjectileView>();
        private readonly List<ScheduledCombatEvent> _combatEvents = new List<ScheduledCombatEvent>();
        private readonly Dictionary<long, AttackSnapshot> _knownAttacks = new Dictionary<long, AttackSnapshot>();
        private Transform _combatRoot;
        private float _combatClock;
        private float _lastAnimationTime;
        private float _combatPlaybackEnd;
        private long _lastCombatFrame = long.MinValue;

        private sealed class ScheduledCombatEvent
        {
            public CombatVisualEvent Event;
            public float At;
            public CombatAttackKind Kind;
            public int FacingX;
            public int FacingY;
        }

        private GameSession _session;
        private SimulationRunner _runner;
        private Transform _npcRoot;
        private EntityId? _selected;
        private AtmospherePresenter _atmosphere;
        private CanopyFader _canopyFader;
        private CampDressing _campDressing;

        public int NpcViewCount => _views.Count;
        public int AnimalViewCount => _animalViews.Count;
        public int ProjectileViewCount => _projectileViews.Count;
        public int CombatImpactViewCount => _impactViews.Count;
        public int ConsumedCombatFrameCount { get; private set; }
        public int ConsumedCombatTraceCount { get; private set; }
        public int PresentedCombatEventCount { get; private set; }
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
            BuildCombatViews();
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
            // Drain exactly once per rendered frame, including every catch-up tick.
            float elapsed = Mathf.Max(0f, _runner.AnimationTime - _lastAnimationTime);
            _lastAnimationTime = _runner.AnimationTime;
            float tickDuration = 1f / Mathf.Max(0.001f, (float)_session.Config.TicksPerSecondAt1x);
            // Keep up with the runner at 8x. ProjectileView guarantees a visible sample
            // for every recorded sweep, even when this clock passes a short contact.
            _combatClock += elapsed;
            ConsumeCombatFrames(_session.DrainCombatFrames(), model, tickDuration);
            DispatchCombatEvents();
            PresentSelectedHunt(model);
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
            foreach (AnimalView animal in _animalViews.Values) { animal.Present(_runner.TickAlpha, _runner.AnimationTime, model.Combat.GetAttack(animal.Id)); }
            for (int i = _fadingAnimals.Count - 1; i >= 0; i--)
            {
                if (_fadingAnimals[i].Present(_runner.TickAlpha, _runner.AnimationTime))
                {
                    Destroy(_fadingAnimals[i].gameObject); _fadingAnimals.RemoveAt(i);
                }
            }
            PresentProjectiles(model);
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
            foreach (string name in new[] { "WorldGrid", "NpcViews", "Resources", "CombatViews" })
            {
                Transform child = transform.Find(name);
                if (child != null) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            }
            foreach (Tile tile in _ownedTiles) { Destroy(tile); }
            _ownedTiles.Clear(); _views.Clear(); _viewList.Clear(); _animalViews.Clear(); _resourceViews.Clear(); _cropViews.Clear(); _fadingAnimals.Clear(); _facilityViews.Clear(); _groundViews.Clear(); _carcassViews.Clear(); _lootViews.Clear();
            _resourceTick = -1; _selected = null;
            if (_canopyFader != null) { _canopyFader.Clear(); }
            _campDressing = null;
            _restoringWorldViews = true;
            BuildMap(); BuildNpcViews(); BuildResourceViews(); BuildCombatViews();
            _restoringWorldViews = false;
        }

        public ProjectileView FindProjectileView(long id) => _projectileViews.TryGetValue(id, out ProjectileView view) ? view : null;
        public AnimalView FindAnimalView(EntityId id) => _animalViews.TryGetValue(id, out AnimalView view) ? view : null;

        private void BuildCombatViews()
        {
            _projectileViews.Clear(); _impactViews.Clear(); _combatEvents.Clear(); _knownAttacks.Clear();
            ConsumedCombatFrameCount = 0; ConsumedCombatTraceCount = 0; PresentedCombatEventCount = 0;
            _combatClock = 0f; _combatPlaybackEnd = 0f; _lastAnimationTime = _runner.AnimationTime;
            _lastCombatFrame = long.MinValue;
            var root = new GameObject("CombatViews"); root.transform.SetParent(transform, false); _combatRoot = root.transform;
            foreach (AttackSnapshot attack in _session.ReadModel.Combat.Attacks) { _knownAttacks[attack.Id] = attack; }
            foreach (ProjectileSnapshot snapshot in _session.ReadModel.Combat.Projectiles)
            {
                CreateProjectile(snapshot.Id).SetAuthoritative(snapshot, true);
            }
        }

        private ProjectileView CreateProjectile(long id)
        {
            if (_projectileViews.TryGetValue(id, out ProjectileView existing)) { return existing; }
            var go = new GameObject("Projectile_" + id); go.transform.SetParent(_combatRoot, false);
            var view = go.AddComponent<ProjectileView>(); view.Initialize(id, _art); _projectileViews.Add(id, view);
            return view;
        }

        private void ConsumeCombatFrames(IReadOnlyList<TickResult> frames, WorldReadModel model, float tickDuration)
        {
            foreach (AttackSnapshot attack in model.Combat.Attacks) { _knownAttacks[attack.Id] = attack; }
            var releases = new Dictionary<long, CombatVisualEvent>();
            var starts = new Dictionary<long, CombatVisualEvent>();
            var contacts = new Dictionary<long, CombatVisualEvent>();
            var firstTraces = new Dictionary<long, CombatTraceSegment>();
            foreach (TickResult frame in frames)
            {
                if (frame.Tick <= _lastCombatFrame) { continue; }
                foreach (CombatVisualEvent evt in frame.CombatEvents)
                {
                    if (evt.State == CombatEventKind.Released) { releases[evt.AttackId] = evt; }
                    if (evt.State == CombatEventKind.Started) { starts[evt.AttackId] = evt; }
                    if (evt.State == CombatEventKind.Contact && evt.ProjectileId == 0) { contacts[evt.AttackId] = evt; }
                }
                foreach (CombatTraceSegment trace in frame.CombatTraces)
                {
                    if (!firstTraces.ContainsKey(trace.ProjectileId)) { firstTraces.Add(trace.ProjectileId, trace); }
                }
            }
            foreach (TickResult frame in frames)
            {
                if (frame.Tick <= _lastCombatFrame) { continue; }
                _lastCombatFrame = frame.Tick; ConsumedCombatFrameCount++;
                if (frame.CombatTraces.Count == 0 && frame.CombatEvents.Count == 0) { continue; }
                float start = Mathf.Max(_combatClock, _combatPlaybackEnd);
                _combatPlaybackEnd = start + tickDuration;
                foreach (CombatTraceSegment trace in frame.CombatTraces)
                {
                    float from = start + tickDuration * Mathf.Clamp01((float)trace.FromTime / CombatState.TimeUnitsPerTick);
                    float to = start + tickDuration * Mathf.Clamp01((float)trace.ToTime / CombatState.TimeUnitsPerTick);
                    CreateProjectile(trace.ProjectileId).Enqueue(trace, from, to); ConsumedCombatTraceCount++;
                }
                foreach (CombatVisualEvent evt in frame.CombatEvents)
                {
                    _knownAttacks.TryGetValue(evt.AttackId, out AttackSnapshot attack);
                    CombatAttackKind kind = attack != null ? attack.Kind :
                        evt.ProjectileId != 0 ? CombatAttackKind.Bow : CombatAttackKind.Spear;
                    int facingX = attack != null ? attack.FacingX : 0, facingY = attack != null ? attack.FacingY : 0;
                    if (attack == null)
                    {
                        // Catch-up can remove a whole melee attack before the read model is sampled.
                        // Prefer the recorded start/contact, not a stale movement direction.
                        GridPos actor = ActorCell(model, evt.Shooter);
                        GridPos target = ActorCell(model, evt.HitId);
                        int fromX = actor.IsValid ? actor.X * CombatState.UnitsPerCell + CombatState.UnitsPerCell / 2 : evt.X;
                        int fromY = actor.IsValid ? actor.Y * CombatState.UnitsPerCell + CombatState.UnitsPerCell / 2 : evt.Y;
                        if (starts.TryGetValue(evt.AttackId, out CombatVisualEvent started))
                        {
                            fromX = started.X; fromY = started.Y;
                            target = ActorCell(model, started.HitId);
                        }
                        if (contacts.TryGetValue(evt.AttackId, out CombatVisualEvent contact))
                        {
                            facingX = contact.X - fromX; facingY = contact.Y - fromY;
                        }
                        else if (target.IsValid)
                        {
                            facingX = target.X * CombatState.UnitsPerCell + CombatState.UnitsPerCell / 2 - fromX;
                            facingY = target.Y * CombatState.UnitsPerCell + CombatState.UnitsPerCell / 2 - fromY;
                        }
                    }
                    if (releases.TryGetValue(evt.AttackId, out CombatVisualEvent release))
                    {
                        if (release.ProjectileId != 0)
                        {
                            kind = CombatAttackKind.Bow;
                            if (firstTraces.TryGetValue(release.ProjectileId, out CombatTraceSegment trace) && attack == null)
                            {
                                facingX = trace.ToX - trace.FromX; facingY = trace.ToY - trace.FromY;
                            }
                        }
                        else if (attack == null && evt.State == CombatEventKind.Contact)
                        {
                            facingX = evt.X - release.X; facingY = evt.Y - release.Y;
                        }
                    }
                    _combatEvents.Add(new ScheduledCombatEvent
                    {
                        Event = evt, At = start + tickDuration * Mathf.Clamp01((float)evt.Time / CombatState.TimeUnitsPerTick),
                        Kind = kind, FacingX = facingX, FacingY = facingY
                    });
                }
            }
            _combatEvents.Sort((a, b) =>
            {
                int order = a.At.CompareTo(b.At);
                if (order == 0) { order = a.Event.AttackId.CompareTo(b.Event.AttackId); }
                return order != 0 ? order : ((int)a.Event.State).CompareTo((int)b.Event.State);
            });
            // Scheduled events already own their pose facts; bound the lookup to current attacks.
            _knownAttacks.Clear();
            foreach (AttackSnapshot attack in model.Combat.Attacks) { _knownAttacks[attack.Id] = attack; }
        }

        private static GridPos ActorCell(WorldReadModel model, EntityId id)
        {
            foreach (NpcSnapshot npc in model.Npcs) { if (npc.Id == id) { return npc.Cell; } }
            foreach (AnimalSnapshot animal in model.EcologyEconomy.Animals)
            {
                if (animal.Id == id && !model.IsAnimalNonLocal(id)) { return animal.Cell; }
            }
            return GridPos.Invalid;
        }

        private void DispatchCombatEvents()
        {
            int consumed = 0;
            foreach (ScheduledCombatEvent scheduled in _combatEvents)
            {
                if (scheduled.At > _combatClock) { break; }
                CombatVisualEvent evt = scheduled.Event;
                if (_views.TryGetValue(evt.Shooter, out NpcView shooter))
                {
                    shooter.ReceiveCombatEvent(evt, scheduled.Kind, scheduled.FacingX, scheduled.FacingY, _runner.AnimationTime);
                }
                if (evt.HitId != evt.Shooter && _views.TryGetValue(evt.HitId, out NpcView victim))
                {
                    victim.ReceiveCombatEvent(evt, scheduled.Kind, scheduled.FacingX, scheduled.FacingY, _runner.AnimationTime);
                }
                if (_animalViews.TryGetValue(evt.Shooter, out AnimalView animal))
                {
                    animal.ReceiveCombatEvent(evt, scheduled.FacingX, scheduled.FacingY, _runner.AnimationTime);
                }
                if (evt.HitId != evt.Shooter && _animalViews.TryGetValue(evt.HitId, out AnimalView prey))
                {
                    prey.ReceiveCombatEvent(evt, scheduled.FacingX, scheduled.FacingY, _runner.AnimationTime);
                }
                if (evt.ProjectileId != 0 && evt.State == CombatEventKind.Released)
                {
                    CreateProjectile(evt.ProjectileId).ReceiveRelease(evt);
                }
                if (evt.ProjectileId == 0 && evt.State == CombatEventKind.Contact && evt.Damage > 0)
                {
                    var go = new GameObject("MeleeImpact_" + evt.AttackId); go.transform.SetParent(_combatRoot, false);
                    var impact = go.AddComponent<ProjectileView>(); impact.InitializeEffect(evt, _art, _combatClock); _impactViews.Add(impact);
                }
                consumed++; PresentedCombatEventCount++;
            }
            if (consumed > 0) { _combatEvents.RemoveRange(0, consumed); }
        }

        private void PresentProjectiles(WorldReadModel model)
        {
            var live = new Dictionary<long, ProjectileSnapshot>();
            foreach (ProjectileSnapshot snapshot in model.Combat.Projectiles)
            {
                live.Add(snapshot.Id, snapshot); CreateProjectile(snapshot.Id).SetAuthoritative(snapshot);
            }
            var removed = new List<long>();
            foreach (var pair in _projectileViews)
            {
                live.TryGetValue(pair.Key, out ProjectileSnapshot snapshot); pair.Value.SetAuthoritative(snapshot);
                if (pair.Value.Present(_combatClock))
                {
                    pair.Value.gameObject.SetActive(false); Destroy(pair.Value.gameObject); removed.Add(pair.Key); continue;
                }
                if (snapshot != null && snapshot.Phase == ProjectilePhase.Embedded)
                {
                    if (snapshot.EmbeddedTargetIsAnimal && _animalViews.TryGetValue(snapshot.EmbeddedTarget, out AnimalView animal))
                    {
                        pair.Value.Attach(animal.transform.position);
                    }
                    else if (!snapshot.EmbeddedTargetIsAnimal && _views.TryGetValue(snapshot.EmbeddedTarget, out NpcView npc))
                    {
                        pair.Value.Attach(npc.transform.position);
                    }
                }
            }
            foreach (long id in removed) { _projectileViews.Remove(id); }
            for (int i = _impactViews.Count - 1; i >= 0; i--)
            {
                if (_impactViews[i].Present(_combatClock)) { Destroy(_impactViews[i].gameObject); _impactViews.RemoveAt(i); }
            }
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

        private readonly List<SpriteRenderer> _chargeWarnings = new List<SpriteRenderer>();
        private void PresentSelectedHunt(WorldReadModel model)
        {
            HuntTaskSnapshot task = _selectedAnimal.HasValue ? model.Hunting.ForTarget(_selectedAnimal.Value) :
                _selected.HasValue ? model.Hunting.ForNpc(_selected.Value) : null;
            foreach (NpcView view in _views.Values)
            {
                HuntRole? role = null;
                if (task != null) { foreach (var member in task.Members) { if (member.NpcId == view.Id) { role = member.Role; break; } } }
                view.SetHuntRole(role);
            }
            EntityId? target = _selectedAnimal ?? task?.TargetId;
            foreach (AnimalView animal in _animalViews.Values) { animal.SetSelected(target.HasValue && target.Value == animal.Id); }
            foreach (SpriteRenderer warning in _chargeWarnings) { if (warning != null) { warning.enabled = false; } }
            if (!target.HasValue) { return; }
            AttackSnapshot attack = model.Combat.GetAttack(target.Value);
            AnimalView source = FindAnimalView(target.Value);
            if (source == null || attack == null || attack.Kind != CombatAttackKind.Charge || attack.Phase != CombatAttackPhase.Preparing) { return; }
            while (_chargeWarnings.Count < 3)
            {
                var go = new GameObject("ChargeWarning"); go.transform.SetParent(transform, false);
                var sprite = go.AddComponent<SpriteRenderer>(); sprite.sprite = _art.Get("hunt_charge_warning");
                sprite.color = new Color(1f, 0.55f, 0.2f, 0.8f); sprite.sortingOrder = -7997; _chargeWarnings.Add(sprite);
            }
            for (int i = 0; i < 3; i++)
            {
                GridPos cell = source.Cell.Offset(Math.Sign(attack.FacingX) * (i + 1), Math.Sign(attack.FacingY) * (i + 1));
                _chargeWarnings[i].enabled = _session.World.Map.IsWalkable(cell);
                _chargeWarnings[i].transform.position = CellCenter(cell);
                _chargeWarnings[i].transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(attack.FacingY, attack.FacingX) * Mathf.Rad2Deg - 90f);
            }
        }

        private EntityId? _selectedAnimal;
        public void SetSelectedAnimal(EntityId? id)
        {
            _selectedAnimal = id;
            foreach (AnimalView view in _animalViews.Values) { view.SetSelected(id.HasValue && view.Id == id.Value); }
        }
        public AnimalView PickAnimal(Vector2 point)
        {
            AnimalView best = null;
            foreach (AnimalView view in _animalViews.Values)
            {
                if (view.IsDying || view.IsDespawning || !view.HitBounds.Contains(point)) { continue; }
                if (best == null || view.SortingOrder > best.SortingOrder || view.SortingOrder == best.SortingOrder && view.Id.Value < best.Id.Value) { best = view; }
            }
            return best;
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
            view.Configure(_art, snapshot.Role, _runner.AnimationTime - (_restoringWorldViews || _session.ReadModel.Tick == _session.Config.StartTick ? 1f : 0f), snapshot.Health);
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

            var bodies = new HashSet<int>();
            foreach (var body in model.EcologyEconomy.Carcasses)
            {
                bodies.Add(body.Id.Value);
                if (!_carcassViews.TryGetValue(body.Id.Value, out SpriteRenderer corpse))
                { corpse = CreateResourceView("Carcass_" + body.Id.Value, body.Cell, _art.Get("carcass_" + (int)body.Species)); _carcassViews.Add(body.Id.Value, corpse); }
                corpse.color = Color.Lerp(new Color(0.55f, 0.53f, 0.48f), Color.white, body.Freshness / 10000f);
                int icon = -1;
                for (int item = body.Harvest.Count - 1; item >= 0; item--) { if (body.Harvest[item] > 0) { icon = item; break; } }
                if (!_lootViews.TryGetValue(body.Id.Value, out SpriteRenderer loot))
                { loot = CreateResourceView("Loot_" + body.Id.Value, body.Cell, null); _lootViews.Add(body.Id.Value, loot); loot.transform.position += new Vector3(0.35f, 0.12f, 0); loot.transform.localScale = Vector3.one * 0.45f; }
                loot.sprite = icon < 0 ? null : _art.Get("field_loot_" + icon);
                loot.enabled = icon >= 0;
            }
            var removedBodies = new List<int>();
            foreach (var pair in _carcassViews) { if (!bodies.Contains(pair.Key)) { Destroy(pair.Value.gameObject); Destroy(_lootViews[pair.Key].gameObject); removedBodies.Add(pair.Key); } }
            foreach (int id in removedBodies) { _carcassViews.Remove(id); _lootViews.Remove(id); }
            var living = new HashSet<EntityId>();
            foreach (AnimalSnapshot animal in model.EcologyEconomy.Animals)
            {
                if (model.IsAnimalNonLocal(animal.Id) || !animal.Cell.IsValid) { continue; }
                living.Add(animal.Id);
                AnimalView view;
                if (!_animalViews.TryGetValue(animal.Id, out view))
                {
                    var go = new GameObject("Animal_" + animal.Id.Value);
                    go.transform.SetParent(_resourceRoot, false); view = go.AddComponent<AnimalView>();
                    view.Initialize(animal, _art, model.Tick, _runner.AnimationTime, _restoringWorldViews || model.Tick == _session.Config.StartTick);
                    _animalViews.Add(animal.Id, view);
                }
                else { view.Receive(animal, model.Tick, _runner.AnimationTime); }
            }
            var removed = new List<EntityId>();
            foreach (var pair in _animalViews)
            {
                if (!living.Contains(pair.Key))
                {
                    if (model.IsAnimalNonLocal(pair.Key)) { pair.Value.BeginDespawn(_runner.AnimationTime); }
                    else { pair.Value.BeginDeath(_runner.AnimationTime); }
                    _fadingAnimals.Add(pair.Value); removed.Add(pair.Key);
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
