using UnityEngine;

namespace MyAiWorld.Presentation.Atmosphere
{
    public enum AtmosphereLayer
    {
        Fog = 0,
        LightShafts = 1,
        Particles = 2,
        Canopy = 3,
        DayNight = 4
    }

    [CreateAssetMenu(menuName = "My AI World/Atmosphere Profile")]
    public sealed class AtmosphereProfile : ScriptableObject
    {
        public bool masterEnabled = true;
        [Range(0f, 2f)] public float fogDensity = 1f;
        [Range(0f, 1f)] public float fogOpacity = 0.34f;
        public bool lightShafts = true;
        [Range(0f, 2f)] public float lightStrength = 1f;
        public bool particles = true;
        [Range(0f, 1f)] public float snowChance = 0.22f;
        public bool canopyFade = true;
        [Range(0.15f, 1f)] public float canopyOpacity = 0.62f;
        [Range(0f, 1.5f)] public float dayNightStrength = 1f;
        [Range(0f, 2f)] public float campLightStrength = 1f;

        public static AtmosphereProfile Load()
        {
            var profile = Resources.Load<AtmosphereProfile>("Atmosphere/AtmosphereProfile");
            if (profile != null) { return profile; }
            profile = CreateInstance<AtmosphereProfile>();
            profile.name = "AtmosphereProfile (runtime default)";
            return profile;
        }

        public bool LayerEnabled(AtmosphereLayer layer)
        {
            if (!masterEnabled) { return false; }
            switch (layer)
            {
                case AtmosphereLayer.LightShafts: return lightShafts;
                case AtmosphereLayer.Particles: return particles;
                case AtmosphereLayer.Canopy: return canopyFade;
                default: return true;
            }
        }
    }
}
