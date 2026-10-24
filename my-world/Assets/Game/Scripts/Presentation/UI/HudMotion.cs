using System;
using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    public static class HudMotion
    {
        public const string PreferenceKey = "MyAiWorld.Hud.ReducedMotion";
        public const float EntranceDuration = 0.18f;
        public const float EntranceStagger = 0.06f;
        public const float ButtonDuration = 0.1f;
        public const float SummaryDuration = 0.14f;
        public const float WeatherDuration = 0.22f;
        public const float ResultDuration = 0.22f;
        public const float BackdropDuration = 0.16f;
        public const float ValueDuration = 0.24f;
        public const float WarningDuration = 0.6f;
        public const float DayDuration = 0.32f;
        public const float ReducedDuration = 0.08f;
        public const float ExhaustedPeriod = 2.4f;
        public const float SupplyCooldown = 1f;
        public const float DetailDuration = 0.18f;
        public const float CloseDuration = 0.12f;
        public const float MeterDuration = 0.16f;
        public const float EcologyDuration = 0.36f;
        public const float EcologyCooldown = 5f;
        public static event Action PreferenceChanged;
        private static bool? _reduced;

        public static bool Reduced
        {
            get
            {
                if (!_reduced.HasValue) { _reduced = PlayerPrefs.GetInt(PreferenceKey, 0) != 0; }
                return _reduced.Value;
            }
            set
            {
                if (Reduced == value) { return; }
                _reduced = value;
                PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0);
                PlayerPrefs.Save();
                PreferenceChanged?.Invoke();
            }
        }

        public static float Duration(float duration) => Reduced ? Mathf.Min(duration, ReducedDuration) : duration;
        public static float Ease(float value) => 1f - Mathf.Pow(1f - Mathf.Clamp01(value), 3f);
    }
}
