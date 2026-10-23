using MyAiWorld.Sim.Core;

namespace MyAiWorld.Sim.World
{
    public enum WeatherKind
    {
        Clear,
        Snow,
        Blizzard,
        ColdSnap
    }

    public sealed class EnvironmentState
    {
        public const int MinTemperature = -6000;
        public const int MaxTemperature = 3500;
        public const int MaxSnowDepth = 20000;

        public EnvironmentState(ulong seed)
        {
            RngState = DeterministicRng.Mix(seed ^ 0xE17A0B5EEDUL);
        }

        public int Temperature { get; internal set; }
        public int Wind { get; internal set; }
        public int Daylight { get; internal set; }
        public int SnowDepth { get; internal set; }
        public WeatherKind Weather { get; internal set; }
        public long NextWeatherTick { get; internal set; }
        public ulong RngState { get; internal set; }
        public bool IsDaytime => Daylight >= 2000;
        public bool WaterFrozen => Temperature < 0;
    }
}
