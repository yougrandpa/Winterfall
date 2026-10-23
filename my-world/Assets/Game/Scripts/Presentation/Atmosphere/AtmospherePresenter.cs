using System;
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

        private readonly List<Drift> _lowFog = new List<Drift>();
        private readonly List<Drift> _highFog = new List<Drift>();
        private readonly List<Drift> _shafts = new List<Drift>();
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly bool[] _layerEnabled = { true, true, true, true, true };

        private GameSession _session;
        private SimulationRunner _runner;
        private PastoralArtConfig _art;
        private AtmosphereProfile _profile;
        private Camera _camera;
        private Material _additive;
        private Image _dayNight;
        private SpriteRenderer _firePool;
        private SpriteRenderer _fireGlow;
        private Vector3? _campfire;
        private float _snowIntensity;
        private bool _isSnowDay;
        private bool? _snowOverride;
        private int _lastWeatherDay = -1;

        public int LowFogCount => _lowFog.Count;
        public int HighFogCount => _highFog.Count;
        public int ShaftCount => _shafts.Count;
        public int ParticlePoolCount => _particles.Count;
        public int VisibleParticleCount { get; private set; }
        public bool IsSnowing => _snowIntensity > 0.05f;
        public float SnowIntensity => _snowIntensity;
        public float CampfireIntensity { get; private set; }
        public float NightAmount { get; private set; }
        public bool HasCampfire => _campfire.HasValue;
        public bool IsSnowDay => _isSnowDay;
        public AtmosphereProfile Profile => _profile;

        public void Initialize(GameSession session, SimulationRunner runner, PastoralArtConfig art, AtmosphereProfile profile)
        {
            _session = session;
            _runner = runner;
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
            BuildCampLight();
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

        private void BuildCampLight()
        {
            _firePool = CreateSprite("CampfirePool", "glow_radial", -7800, true);
            if (_firePool != null)
            {
                _firePool.transform.localScale = new Vector3(0.9f, 0.66f, 1f);
                _firePool.color = new Color(1f, 0.7f, 0.36f, 0f);
                _firePool.enabled = false;
            }
            _fireGlow = CreateSprite("CampfireGlow", "glow_radial", 185, true);
            if (_fireGlow != null)
            {
                _fireGlow.transform.localScale = new Vector3(1.15f, 0.95f, 1f);
                _fireGlow.color = new Color(1f, 0.68f, 0.34f, 0f);
                _fireGlow.enabled = false;
            }
        }

        public void SetCampfire(Vector3? position)
        {
            _campfire = position;
            bool on = position.HasValue;
            if (_firePool != null) { _firePool.enabled = on; }
            if (_fireGlow != null) { _fireGlow.enabled = on; }
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

        public void SetSnowOverride(bool? snowy)
        {
            _snowOverride = snowy;
            _lastWeatherDay = -1;
        }

        public void Present(GameDateTime date, float animationTime)
        {
            if (_camera == null) { _camera = Camera.main; }
            UpdateWeather(date);
            float nightAmount = NightCurve(date.MinuteOfDay);
            float duskAmount = DuskCurve(date.MinuteOfDay);
            NightAmount = nightAmount;
            UpdateDayNight(nightAmount, duskAmount);
            UpdateFog(date, animationTime, nightAmount, duskAmount);
            UpdateShafts(animationTime, nightAmount, duskAmount);
            UpdateCampLight(animationTime, nightAmount, duskAmount);
            UpdateParticles(date, animationTime, nightAmount);
        }

        private void UpdateWeather(GameDateTime date)
        {
            if (_lastWeatherDay == date.AbsoluteDay) { return; }
            _lastWeatherDay = date.AbsoluteDay;
            _isSnowDay = _snowOverride ?? SnowDay(_session != null ? _session.ReadModel.Seed : 0, date.AbsoluteDay,
                (int)date.Season, _profile.snowChance);
        }

        private static float NightCurve(int minuteOfDay)
        {
            float hour = minuteOfDay / 60f;
            float dusk = DuskCurve(minuteOfDay);
            if (hour < 5f) { return 1f; }
            if (hour < 7f) { return 1f - Smooth01((hour - 5f) / 2f); }
            if (hour < 17f) { return 0f; }
            if (hour < 22f) { return Mathf.Max(dusk * 0.55f, Smooth01((hour - 17f) / 5f)); }
            return 1f;
        }

        private static float DuskCurve(int minuteOfDay)
        {
            float hour = minuteOfDay / 60f;
            if (hour < 15f || hour >= 21.5f) { return 0f; }
            return Mathf.Sin((hour - 15f) / 6.5f * Mathf.PI);
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

        private void UpdateFog(GameDateTime date, float animationTime, float nightAmount, float duskAmount)
        {
            bool enabled = IsLayerEnabled(AtmosphereLayer.Fog);
            if (!enabled)
            {
                foreach (Drift fog in _lowFog) { fog.Renderer.enabled = false; }
                foreach (Drift fog in _highFog) { fog.Renderer.enabled = false; }
                return;
            }
            float timeFactor = 0.62f + 0.38f * Mathf.Max(nightAmount, duskAmount);
            float opacity = _profile.fogDensity * _profile.fogOpacity * timeFactor;
            Vector2 view = ViewSize();
            Vector3 center = ViewCenter();
            float t = animationTime;
            foreach (Drift fog in _lowFog)
            {
                float nx = Wrap01(fog.X + t * fog.Speed);
                float x = (nx - 0.5f) * view.x * 1.35f;
                float y = fog.Y * view.y * 1.1f + Mathf.Sin(t * 0.21f + fog.Phase) * fog.Sway;
                fog.Renderer.transform.position = new Vector3(center.x + x, center.y + y, 0f);
                fog.Renderer.color = new Color(0.83f, 0.89f, 0.91f, opacity * 0.65f);
            }
            foreach (Drift fog in _highFog)
            {
                float nx = Wrap01(fog.X + t * fog.Speed);
                float x = (nx - 0.5f) * view.x * 1.5f;
                float y = fog.Y * view.y * 0.95f;
                fog.Renderer.transform.position = new Vector3(center.x + x, center.y + y, 0f);
                fog.Renderer.color = new Color(0.8f, 0.87f, 0.9f, opacity * 0.42f + (_isSnowDay ? 0.03f : 0f));
            }
        }

        private void UpdateShafts(float animationTime, float nightAmount, float duskAmount)
        {
            bool enabled = IsLayerEnabled(AtmosphereLayer.LightShafts);
            float daylight = 1f - nightAmount;
            float alpha = enabled ? _profile.lightStrength * (0.02f + 0.17f * daylight * (1f - duskAmount * 0.35f)) : 0f;
            foreach (Drift shaft in _shafts)
            {
                shaft.Renderer.enabled = alpha > 0.004f;
                if (!shaft.Renderer.enabled) { continue; }
                float sway = Mathf.Sin(animationTime * 0.16f + shaft.Phase) * shaft.Sway;
                shaft.Renderer.transform.localPosition = new Vector3(shaft.X + sway, shaft.Y, 0f);
                shaft.Renderer.color = new Color(1f, 0.95f, 0.84f, alpha);
            }
        }

        private void UpdateCampLight(float animationTime, float nightAmount, float duskAmount)
        {
            if (!_campfire.HasValue || _firePool == null || _fireGlow == null) { return; }
            float warmth = Mathf.Max(nightAmount, duskAmount * 0.65f);
            float flicker = 0.9f + 0.06f * Mathf.Sin(animationTime * 7.3f) + 0.04f * Mathf.Sin(animationTime * 11.7f + 1.7f);
            CampfireIntensity = (0.16f + 0.84f * warmth) * flicker * _profile.campLightStrength;
            var position = _campfire.Value;
            _firePool.transform.position = position + new Vector3(0f, -0.1f, 0f);
            _fireGlow.transform.position = position + new Vector3(0f, 0.35f, 0f);
            _firePool.color = new Color(1f, 0.66f, 0.32f, Mathf.Clamp01(0.8f * CampfireIntensity));
            _fireGlow.color = new Color(1f, 0.7f, 0.36f, Mathf.Clamp01(0.42f * CampfireIntensity));
        }

        private void UpdateParticles(GameDateTime date, float animationTime, float nightAmount)
        {
            float minute = date.MinuteOfDay;
            float daylight = minute > 360f && minute < 1200f ? 1f : 0.55f;
            float target = _isSnowDay ? 0.55f + 0.45f * daylight : 0.26f;
            _snowIntensity = _isSnowDay ? target : 0f;
            if (!IsLayerEnabled(AtmosphereLayer.Particles))
            {
                VisibleParticleCount = 0;
                return;
            }
            Sprite snow = _art != null ? _art.Get(_isSnowDay ? (_lastWeatherDay % 2 == 0 ? "snow_0" : "snow_1") : "mote") : null;
            if (snow == null)
            {
                VisibleParticleCount = 0;
                return;
            }
            int visible = Mathf.RoundToInt(ParticlePool * target);
            VisibleParticleCount = visible;
            Vector2 view = ViewSize();
            Vector3 center = ViewCenter();
            float t = animationTime;
            var color = _isSnowDay
                ? new Color(0.9f, 0.95f, 0.98f, 0.72f)
                : new Color(0.78f, 0.87f, 0.9f, 0.3f + nightAmount * 0.1f);
            for (int i = 0; i < _particles.Count; i++)
            {
                Particle particle = _particles[i];
                bool on = i < visible;
                particle.Renderer.enabled = on;
                if (!on) { continue; }
                particle.Renderer.sprite = snow;
                float fall = _isSnowDay ? particle.Speed : -particle.Speed * 0.3f;
                float nx = Wrap01(particle.X + Mathf.Sin(t * 0.35f + particle.Phase) * particle.Sway * 0.08f);
                float ny = Wrap01(particle.Y - t * fall);
                float x = (nx - 0.5f) * view.x * 1.08f;
                float y = (ny - 0.5f) * view.y * 1.08f;
                particle.Renderer.transform.position = new Vector3(center.x + x, center.y + y, 0f);
                particle.Renderer.transform.localScale = Vector3.one * (particle.Scale * (_isSnowDay ? 0.9f : 0.55f));
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

        public static bool SnowDay(int seed, int absoluteDay, int season, float chance = 0.22f)
        {
            unchecked
            {
                uint hash = (uint)(seed * 73856093) ^ (uint)(absoluteDay * 19349663) ^ (uint)(season * 83492791);
                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
                return hash % 1000u < Mathf.Clamp(chance, 0f, 1f) * 1000f;
            }
        }

        private void OnDestroy()
        {
            if (_additive != null) { Destroy(_additive); }
        }
    }
}
