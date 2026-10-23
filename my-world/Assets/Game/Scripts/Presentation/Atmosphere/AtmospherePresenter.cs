using System.Collections.Generic;
using MyAiWorld.App;
using MyAiWorld.App.Query;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.Atmosphere
{
    // Presentation-only weather and lighting. Driven by AnimationTime so pausing,
    // stepping and fast-forward keep the same visual semantics as the simulation.
    public sealed class AtmospherePresenter : MonoBehaviour
    {
        private const int LowFogTarget = 14;
        private const int HighFogTarget = 4;
        private const int ShaftTarget = 7;
        private const int ParticlePool = 160;

        private sealed class Drift
        {
            public SpriteRenderer Renderer;
            public float X;
            public float Y;
            public float Speed;
            public float Sway;
            public float Phase;
            public float Scale;
        }

        private sealed class Particle
        {
            public SpriteRenderer Renderer;
            public float X;
            public float Y;
            public float Speed;
            public float Sway;
            public float Phase;
            public float Scale;
            public float Threshold;
        }

        private sealed class FireSource
        {
            public SpriteRenderer Flame;
            public SpriteRenderer Pool;
            public SpriteRenderer Glow;
        }

        private readonly List<Drift> _lowFog = new List<Drift>();
        private readonly List<Drift> _highFog = new List<Drift>();
        private readonly List<Drift> _shafts = new List<Drift>();
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Dictionary<int, FireSource> _fires = new Dictionary<int, FireSource>();
        private readonly bool[] _layerEnabled = { true, true, true, true, true };

        private GameSession _session;
        private PastoralArtConfig _art;
        private AtmosphereProfile _profile;
        private Camera _camera;
        private Material _additive;
        private Image _dayNight;
        private float _snowIntensity;
        private int _worldRevision = -1;

        public int LowFogCount => _lowFog.Count;
        public int HighFogCount => _highFog.Count;
        public int ShaftCount => _shafts.Count;
        public int ParticlePoolCount => _particles.Count;
        public int VisibleParticleCount { get; private set; }
        public bool IsSnowing => _snowIntensity > 0.05f;
        public float SnowIntensity => _snowIntensity;
        public float CampfireIntensity { get; private set; }
        public float NightAmount { get; private set; }
        public int ActiveFireCount { get; private set; }
        public bool HasCampfire => ActiveFireCount > 0;
        public AtmosphereProfile Profile => _profile;

        public void Initialize(GameSession session, SimulationRunner runner, PastoralArtConfig art, AtmosphereProfile profile)
        {
            _session = session;
            _worldRevision = session.WorldRevision;
            _art = art;
            _profile = profile != null ? profile : AtmosphereProfile.Load();
            _camera = Camera.main;
            _layerEnabled[(int)AtmosphereLayer.Fog] = _profile.fogDensity > 0f;
            _layerEnabled[(int)AtmosphereLayer.LightShafts] = _profile.lightShafts;
            _layerEnabled[(int)AtmosphereLayer.Particles] = _profile.particles;
            _layerEnabled[(int)AtmosphereLayer.Canopy] = _profile.canopyFade;
            _layerEnabled[(int)AtmosphereLayer.DayNight] = _profile.dayNightStrength > 0f;
            _additive = CreateAdditiveMaterial();
            BuildFog();
            BuildShafts();
            BuildParticles();
            BuildDayNight();
            Present(session.ReadModel.Date, runner != null ? runner.AnimationTime : 0f);
        }

        private static Material CreateAdditiveMaterial()
        {
            Shader shader = Resources.Load<Shader>("Atmosphere/AtmosphereAdditive");
            if (shader == null) { shader = Shader.Find("MyAiWorld/AtmosphereAdditive"); }
            if (shader == null) { return null; }
            return new Material(shader)
            {
                name = "AtmosphereAdditive (runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void BuildFog()
        {
            for (int i = 0; i < LowFogTarget; i++)
            {
                SpriteRenderer renderer = CreateSprite("FogLow_" + i, "fog_puff_" + (i % 4), -8500, false);
                if (renderer == null) { continue; }
                float scale = 1.6f + ((i * 37) % 11) * 0.1f;
                renderer.transform.localScale = new Vector3(scale, 1.1f + (i % 3) * 0.22f, 1f);
                renderer.color = new Color(0.82f, 0.88f, 0.9f, 0.1f);
                _lowFog.Add(new Drift
                {
                    Renderer = renderer,
                    X = 0.06f + (i * 0.137f) % 0.9f,
                    Y = -0.44f + (i * 0.213f) % 0.88f,
                    Speed = 0.0042f + (i % 5) * 0.0011f,
                    Sway = 0.12f + (i % 4) * 0.06f,
                    Phase = i * 1.71f,
                    Scale = scale
                });
            }

            for (int i = 0; i < HighFogTarget; i++)
            {
                SpriteRenderer renderer = CreateSprite("FogHigh_" + i, "mist_band_" + (i % 2), 190, false);
                if (renderer == null) { continue; }
                float scale = 2.1f + (i % 3) * 0.35f;
                renderer.transform.localScale = new Vector3(scale, 1.4f + i * 0.2f, 1f);
                renderer.color = new Color(0.8f, 0.87f, 0.9f, 0.05f);
                _highFog.Add(new Drift
                {
                    Renderer = renderer,
                    X = (i * 0.27f) % 1f,
                    Y = -0.5f + i * 0.31f,
                    Speed = 0.006f + i * 0.0018f,
                    Sway = 0.2f,
                    Phase = i * 2.4f,
                    Scale = scale
                });
            }
        }

        private void BuildShafts()
        {
            WorldReadModel model = _session != null ? _session.ReadModel : null;
            if (model == null) { return; }
            var candidates = new List<Vector3>();
            float cx = model.Width * 0.5f, cy = model.Height * 0.5f;
            int width = model.Width, height = model.Height;
            IReadOnlyList<TileType> tiles = model.MapTiles;
            for (int y = 2; y < height - 2; y++)
            for (int x = 2; x < width - 2; x++)
            {
                if (tiles[y * width + x] != TileType.Forest) { continue; }
                bool edge = tiles[y * width + x + 1] != TileType.Forest ||
                            tiles[y * width + x - 1] != TileType.Forest ||
                            tiles[(y + 1) * width + x] != TileType.Forest ||
                            tiles[(y - 1) * width + x] != TileType.Forest;
                if (!edge) { continue; }
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy < 36f) { continue; }
                candidates.Add(new Vector3(x + 0.5f, y + 1.1f, 0f));
            }
            if (candidates.Count == 0) { return; }
            candidates.Sort(delegate(Vector3 a, Vector3 b)
            {
                return _art.Variant(Mathf.RoundToInt(a.x * 7f), Mathf.RoundToInt(a.y * 13f), 1000)
                    .CompareTo(_art.Variant(Mathf.RoundToInt(b.x * 7f), Mathf.RoundToInt(b.y * 13f), 1000));
            });

            int stride = Mathf.Max(1, candidates.Count / ShaftTarget);
            for (int i = 0; i < ShaftTarget && i * stride < candidates.Count; i++)
            {
                Vector3 position = candidates[i * stride];
                SpriteRenderer renderer = CreateSprite("LightShaft_" + i, "light_shaft_" + (i % 3), 180, true);
                if (renderer == null) { continue; }
                renderer.transform.position = position;
                renderer.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                float scale = 0.9f + (i % 3) * 0.22f;
                renderer.transform.localScale = new Vector3(scale, 1f + (i % 2) * 0.25f, 1f);
                renderer.color = new Color(1f, 0.95f, 0.84f, 0.08f);
                _shafts.Add(new Drift
                {
                    Renderer = renderer,
                    X = position.x,
                    Y = position.y,
                    Speed = 0f,
                    Sway = 0.14f,
                    Phase = i * 1.37f,
                    Scale = scale
                });
            }
        }

        private void BuildParticles()
        {
            for (int i = 0; i < ParticlePool; i++)
            {
                SpriteRenderer renderer = CreateSprite("AtmosphereMote_" + i, "mote", 210, false);
                if (renderer == null) { continue; }
                renderer.transform.localScale = Vector3.one * 0.7f;
                renderer.color = new Color(0.82f, 0.9f, 0.94f, 0.5f);
                _particles.Add(new Particle
                {
                    Renderer = renderer,
                    X = (i * 0.6180339f) % 1f,
                    Y = (i * 0.381966f) % 1f,
                    Speed = 0.05f + (i % 7) * 0.012f,
                    Sway = 0.1f + (i % 5) * 0.05f,
                    Phase = i * 0.91f,
                    Scale = 0.5f + (i % 4) * 0.12f,
                    Threshold = i / (float)ParticlePool
                });
                renderer.enabled = false;
            }
        }

        private SpriteRenderer CreateSprite(string name, string key, int sortingOrder, bool additive)
        {
            Sprite sprite = _art != null ? _art.Get(key) : null;
            if (sprite == null) { return null; }
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            if (additive && _additive != null) { renderer.sharedMaterial = _additive; }
            return renderer;
        }

        private void BuildDayNight()
        {
            var canvasObject = new GameObject("DayNightTintCanvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -20;
            var imageObject = new GameObject("Tint");
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _dayNight = imageObject.AddComponent<Image>();
            _dayNight.color = new Color(0f, 0f, 0f, 0f);
            _dayNight.raycastTarget = false;
        }

        private Sprite FireFrame(int frame)
        {
            if (_art == null) { return null; }
            // Development atlases may not yet have the appended real-source slices.
            return _art.Get("fire_source_" + frame) ?? _art.Get("campfire_" + frame);
        }

        private FireSource BuildFire(int id)
        {
            var fire = new FireSource
            {
                Flame = CreateSprite("ShelterFire_" + id, "fire_source_0", 0, false),
                Pool = CreateSprite("ShelterFirePool_" + id, "glow_radial", -7800, true),
                Glow = CreateSprite("ShelterFireGlow_" + id, "glow_radial", 185, true)
            };
            if (fire.Flame == null)
            {
                fire.Flame = CreateSprite("ShelterFire_" + id, "campfire_0", 0, false);
            }
            if (fire.Pool != null) { fire.Pool.transform.localScale = new Vector3(0.9f, 0.66f, 1f); }
            if (fire.Glow != null) { fire.Glow.transform.localScale = new Vector3(1.15f, 0.95f, 1f); }
            return fire;
        }

        private static void RemoveSprite(SpriteRenderer renderer)
        {
            if (renderer == null) { return; }
            // Detach immediately: deferred destruction must not leave duplicate lights in this view.
            renderer.gameObject.SetActive(false);
            renderer.transform.SetParent(null, false);
            Destroy(renderer.gameObject);
        }

        private void ResetWorldEffects()
        {
            _worldRevision = _session.WorldRevision;
            foreach (FireSource fire in _fires.Values)
            {
                RemoveSprite(fire.Flame);
                RemoveSprite(fire.Pool);
                RemoveSprite(fire.Glow);
            }
            _fires.Clear();
            ActiveFireCount = 0;
            CampfireIntensity = 0f;
            foreach (Drift shaft in _shafts) { RemoveSprite(shaft.Renderer); }
            _shafts.Clear();
            BuildShafts();
        }

        public bool IsLayerEnabled(AtmosphereLayer layer)
        {
            return _profile.masterEnabled && _layerEnabled[(int)layer];
        }

        public void SetLayerEnabled(AtmosphereLayer layer, bool enabled)
        {
            _layerEnabled[(int)layer] = enabled;
            if (layer == AtmosphereLayer.Fog)
            {
                foreach (Drift fog in _lowFog) { fog.Renderer.enabled = enabled; }
                foreach (Drift fog in _highFog) { fog.Renderer.enabled = enabled; }
            }
            else if (layer == AtmosphereLayer.LightShafts)
            {
                foreach (Drift shaft in _shafts) { shaft.Renderer.enabled = enabled; }
            }
            else if (layer == AtmosphereLayer.Particles)
            {
                if (!enabled) { foreach (Particle particle in _particles) { particle.Renderer.enabled = false; } }
            }
            else if (layer == AtmosphereLayer.DayNight)
            {
                if (_dayNight != null) { _dayNight.enabled = enabled; }
            }
        }

        public void Present(GameDateTime date, float animationTime)
        {
            if (_session == null || _session.ReadModel.Environment == null) { return; }
            if (_camera == null) { _camera = Camera.main; }
            if (_worldRevision != _session.WorldRevision) { ResetWorldEffects(); }
            EnvironmentSnapshot environment = _session.ReadModel.Environment;
            float daylight = Mathf.Clamp01(environment.Daylight / 10000f);
            float nightAmount = Smooth01(1f - daylight);
            // Date selects sunset rather than sunrise warmth, never a second solar curve.
            float duskAmount = date.MinuteOfDay >= 720 ? 4f * daylight * (1f - daylight) : 0f;
            NightAmount = nightAmount;
            _snowIntensity = environment.Weather == WeatherKind.Blizzard ? 1f :
                environment.Weather == WeatherKind.Snow ? 0.65f : 0f;
            UpdateDayNight(nightAmount, duskAmount);
            UpdateFog(environment, animationTime, nightAmount, duskAmount);
            UpdateShafts(environment, animationTime, duskAmount);
            UpdateFires(animationTime, nightAmount, duskAmount);
            UpdateParticles(environment, animationTime, nightAmount);
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void UpdateDayNight(float nightAmount, float duskAmount)
        {
            if (_dayNight == null) { return; }
            if (!IsLayerEnabled(AtmosphereLayer.DayNight))
            {
                _dayNight.color = new Color(0f, 0f, 0f, 0f);
                return;
            }
            var night = new Color(0.035f, 0.062f, 0.12f, 0.8f);
            var dusk = new Color(0.24f, 0.2f, 0.25f, 0.28f);
            Color color = Color.Lerp(new Color(0f, 0f, 0f, 0f), dusk, duskAmount * 0.85f);
            color = Color.Lerp(color, night, nightAmount);
            color.a = Mathf.Max(dusk.a * duskAmount * 0.8f, night.a * nightAmount) * _profile.dayNightStrength;
            _dayNight.color = color;
        }

        private void UpdateFog(EnvironmentSnapshot environment, float animationTime, float nightAmount, float duskAmount)
        {
            bool enabled = IsLayerEnabled(AtmosphereLayer.Fog);
            if (!enabled)
            {
                foreach (Drift fog in _lowFog) { fog.Renderer.enabled = false; }
                foreach (Drift fog in _highFog) { fog.Renderer.enabled = false; }
                return;
            }
            float timeFactor = 0.62f + 0.38f * Mathf.Max(nightAmount, duskAmount);
            float weatherFactor = environment.Weather == WeatherKind.Blizzard ? 2.4f :
                environment.Weather == WeatherKind.Snow ? 1.5f : 1f;
            float opacity = Mathf.Clamp01(_profile.fogDensity * _profile.fogOpacity * timeFactor * weatherFactor);
            float windSpeed = 0.5f + Mathf.Clamp01(environment.Wind / 100f) * 3f;
            Vector2 view = ViewSize();
            Vector3 center = ViewCenter();
            float t = animationTime;
            foreach (Drift fog in _lowFog)
            {
                fog.Renderer.enabled = true;
                float nx = Wrap01(fog.X + t * fog.Speed * windSpeed);
                float x = (nx - 0.5f) * view.x * 1.35f;
                float y = fog.Y * view.y * 1.1f + Mathf.Sin(t * 0.21f + fog.Phase) * fog.Sway;
                fog.Renderer.transform.position = new Vector3(center.x + x, center.y + y, 0f);
                fog.Renderer.color = new Color(0.83f, 0.89f, 0.91f, opacity * 0.65f);
            }
            foreach (Drift fog in _highFog)
            {
                fog.Renderer.enabled = true;
                float nx = Wrap01(fog.X + t * fog.Speed * windSpeed);
                float x = (nx - 0.5f) * view.x * 1.5f;
                float y = fog.Y * view.y * 0.95f;
                fog.Renderer.transform.position = new Vector3(center.x + x, center.y + y, 0f);
                fog.Renderer.color = new Color(0.8f, 0.87f, 0.9f, opacity * 0.42f + _snowIntensity * 0.04f);
            }
        }

        private void UpdateShafts(EnvironmentSnapshot environment, float animationTime, float duskAmount)
        {
            bool enabled = IsLayerEnabled(AtmosphereLayer.LightShafts);
            float daylight = Mathf.Clamp01(environment.Daylight / 10000f);
            float visibility = environment.Weather == WeatherKind.Blizzard ? 0f :
                environment.Weather == WeatherKind.Snow ? 0.3f : 1f;
            float alpha = enabled ? _profile.lightStrength * 0.19f * daylight * visibility * (1f - duskAmount * 0.35f) : 0f;
            foreach (Drift shaft in _shafts)
            {
                shaft.Renderer.enabled = alpha > 0.004f;
                if (!shaft.Renderer.enabled) { continue; }
                float sway = Mathf.Sin(animationTime * 0.16f + shaft.Phase) * shaft.Sway;
                shaft.Renderer.transform.localPosition = new Vector3(shaft.X + sway, shaft.Y, 0f);
                shaft.Renderer.color = new Color(1f, 0.95f, 0.84f, alpha);
            }
        }

        private static void SetFireVisible(FireSource fire, bool visible)
        {
            if (fire.Flame != null) { fire.Flame.enabled = visible; }
            if (fire.Pool != null) { fire.Pool.enabled = visible; }
            if (fire.Glow != null) { fire.Glow.enabled = visible; }
        }

        private void UpdateFires(float animationTime, float nightAmount, float duskAmount)
        {
            ActiveFireCount = 0;
            CampfireIntensity = 0f;
            foreach (FireSource fire in _fires.Values) { SetFireVisible(fire, false); }
            IReadOnlyList<ShelterSnapshot> shelters = _session.ReadModel.Survival.Shelters;
            int frame = Mathf.FloorToInt(animationTime * 8f) & 3;
            foreach (ShelterSnapshot shelter in shelters)
            {
                if (!shelter.Burning) { continue; }
                ActiveFireCount++;
                if (!_fires.TryGetValue(shelter.Id, out FireSource fire))
                {
                    fire = BuildFire(shelter.Id);
                    _fires.Add(shelter.Id, fire);
                }
                bool visible = _profile.masterEnabled;
                SetFireVisible(fire, visible);
                float warmth = Mathf.Max(nightAmount, duskAmount * 0.65f);
                float phase = shelter.Id * 1.7f;
                float flicker = 0.9f + 0.06f * Mathf.Sin(animationTime * 7.3f + phase) +
                    0.04f * Mathf.Sin(animationTime * 11.7f + phase + 1.7f);
                float intensity = visible ? (0.16f + 0.84f * warmth) * flicker * _profile.campLightStrength : 0f;
                CampfireIntensity = Mathf.Max(CampfireIntensity, intensity);
                // Show the stove at the real dwelling entrance, never at a decorative prop.
                var position = new Vector3(shelter.Entrance.X + 0.5f, shelter.Entrance.Y + 0.5f, 0f);
                if (fire.Flame != null)
                {
                    fire.Flame.transform.position = position;
                    fire.Flame.sortingOrder = CreatureMotion.SortOrder(position.y);
                    fire.Flame.sprite = FireFrame(frame);
                }
                if (fire.Pool != null)
                {
                    fire.Pool.transform.position = position + new Vector3(0f, -0.1f, 0f);
                    fire.Pool.color = new Color(1f, 0.66f, 0.32f, Mathf.Clamp01(0.8f * intensity));
                }
                if (fire.Glow != null)
                {
                    fire.Glow.transform.position = position + new Vector3(0f, 0.35f, 0f);
                    fire.Glow.color = new Color(1f, 0.7f, 0.36f, Mathf.Clamp01(0.42f * intensity));
                }
            }
        }

        private void UpdateParticles(EnvironmentSnapshot environment, float animationTime, float nightAmount)
        {
            bool snowy = environment.Weather == WeatherKind.Snow || environment.Weather == WeatherKind.Blizzard;
            float wind = Mathf.Clamp01(environment.Wind / 100f);
            float target = snowy ? _snowIntensity : 0.26f;
            Sprite sprite = _art != null ? _art.Get(snowy ? "snow_0" : "mote") : null;
            if (!IsLayerEnabled(AtmosphereLayer.Particles) || sprite == null)
            {
                foreach (Particle particle in _particles) { particle.Renderer.enabled = false; }
                VisibleParticleCount = 0;
                return;
            }
            int visible = Mathf.RoundToInt(_particles.Count * target);
            VisibleParticleCount = visible;
            Vector2 view = ViewSize();
            Vector3 center = ViewCenter();
            float t = animationTime;
            var color = snowy
                ? new Color(0.9f, 0.95f, 0.98f, 0.72f)
                : new Color(0.78f, 0.87f, 0.9f, 0.3f + nightAmount * 0.1f);
            for (int i = 0; i < _particles.Count; i++)
            {
                Particle particle = _particles[i];
                bool on = i < visible;
                particle.Renderer.enabled = on;
                if (!on) { continue; }
                particle.Renderer.sprite = snowy ? (_art.Get("snow_" + (i & 1)) ?? sprite) : sprite;
                float fall = snowy ? particle.Speed * (0.75f + wind) : -particle.Speed * 0.3f;
                float nx = Wrap01(particle.X + t * wind * (snowy ? 0.08f : 0.015f) +
                    Mathf.Sin(t * 0.35f + particle.Phase) * particle.Sway * 0.08f);
                float ny = Wrap01(particle.Y - t * fall);
                float x = (nx - 0.5f) * view.x * 1.08f;
                float y = (ny - 0.5f) * view.y * 1.08f;
                particle.Renderer.transform.position = new Vector3(center.x + x, center.y + y, 0f);
                particle.Renderer.transform.localScale = Vector3.one * (particle.Scale * (snowy ? 0.9f : 0.55f));
                particle.Renderer.color = new Color(color.r, color.g, color.b, color.a * (0.55f + 0.45f * particle.Threshold));
            }
        }

        private Vector3 ViewCenter()
        {
            return _camera != null ? new Vector3(_camera.transform.position.x, _camera.transform.position.y, 0f) : Vector3.zero;
        }

        private Vector2 ViewSize()
        {
            if (_camera == null) { return new Vector2(32f, 18f); }
            float height = _camera.orthographicSize * 2f;
            return new Vector2(height * _camera.aspect, height);
        }

        private static float Wrap01(float value)
        {
            value -= Mathf.Floor(value);
            return value;
        }

        private void OnDestroy()
        {
            if (_additive != null) { Destroy(_additive); }
        }
    }
}
