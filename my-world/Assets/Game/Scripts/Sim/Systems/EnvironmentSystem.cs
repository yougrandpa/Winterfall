using System;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;

namespace MyAiWorld.Sim.Systems
{
    public static class EnvironmentSystem
    {
        public static void Update(WorldState world)
        {
            EnvironmentState environment = world.Environment;
            GameDateTime date = world.Date;
            bool initializing = environment.NextWeatherTick == 0;
            if (world.Tick >= environment.NextWeatherTick)
            {
                ChooseWeather(world, environment);
            }

            environment.Daylight = SolarDaylight(date);
            int target = SeasonalTemperature(date) - 400 + environment.Daylight * 800 / 10000;
            if (environment.Weather == WeatherKind.Snow)
            {
                target -= 200;
            }
            else if (environment.Weather == WeatherKind.Blizzard)
            {
                target -= 700;
            }
            else if (environment.Weather == WeatherKind.ColdSnap)
            {
                target -= 1200;
            }

            target = Math.Max(EnvironmentState.MinTemperature, Math.Min(EnvironmentState.MaxTemperature, target));
            environment.Temperature = initializing ? target : environment.Temperature +
                Math.Sign(target - environment.Temperature) * Math.Min(25, Math.Abs(target - environment.Temperature));
            if (initializing && date.Season == Season.Winter)
            {
                environment.SnowDepth = 400;
            }

            int snowfall = environment.Temperature <= 0 ?
                (environment.Weather == WeatherKind.Blizzard ? 6 : environment.Weather == WeatherKind.Snow ? 2 : 0) : 0;
            int melt = environment.Temperature > 0 ? Math.Max(1, environment.Temperature / 200) : 0;
            environment.SnowDepth = Math.Max(0, Math.Min(EnvironmentState.MaxSnowDepth, environment.SnowDepth + snowfall - melt));
        }

        internal static int SolarDaylight(GameDateTime date)
        {
            int length = 540 + WinterDistance(date) * 420 / (GameCalendar.DaysPerYear / 2);
            int sunrise = (GameCalendar.TicksPerDay - length) / 2;
            int elapsed = date.MinuteOfDay - sunrise;
            if (elapsed <= 0 || elapsed >= length)
            {
                return 0;
            }

            return (int)(40000L * elapsed * (length - elapsed) / ((long)length * length));
        }

        private static int WinterDistance(GameDateTime date)
        {
            int dayOfYear = (int)date.Season * GameCalendar.DaysPerSeason + date.DayOfSeason - 1;
            int winterMidpoint = (int)Season.Winter * GameCalendar.DaysPerSeason + GameCalendar.DaysPerSeason / 2 - 1;
            int phase = (dayOfYear + GameCalendar.DaysPerYear - winterMidpoint) % GameCalendar.DaysPerYear;
            return Math.Min(phase, GameCalendar.DaysPerYear - phase);
        }

        private static int SeasonalTemperature(GameDateTime date)
        {
            return -1100 + WinterDistance(date) * 3200 / (GameCalendar.DaysPerYear / 2);
        }

        private static void ChooseWeather(WorldState world, EnvironmentState environment)
        {
            var rng = new DeterministicRng(environment.RngState);
            int roll = rng.NextInt(0, 100);
            int duration;
            if (world.Date.Season == Season.Winter)
            {
                environment.Weather = roll < 45 ? WeatherKind.Clear : roll < 70 ? WeatherKind.Snow :
                    roll < 90 ? WeatherKind.Blizzard : WeatherKind.ColdSnap;
            }
            else if (SeasonalTemperature(world.Date) < 500)
            {
                environment.Weather = roll < 70 ? WeatherKind.Clear : roll < 85 ? WeatherKind.Snow :
                    roll < 90 ? WeatherKind.Blizzard : WeatherKind.ColdSnap;
            }
            else
            {
                environment.Weather = roll < 95 ? WeatherKind.Clear : WeatherKind.ColdSnap;
            }

            switch (environment.Weather)
            {
                case WeatherKind.Snow:
                    environment.Wind = rng.NextInt(20, 56);
                    duration = rng.NextInt(240, 481);
                    break;
                case WeatherKind.Blizzard:
                    environment.Wind = rng.NextInt(65, 101);
                    duration = rng.NextInt(120, 301);
                    break;
                case WeatherKind.ColdSnap:
                    environment.Wind = rng.NextInt(10, 66);
                    duration = rng.NextInt(480, 961);
                    break;
                default:
                    environment.Wind = rng.NextInt(0, 31);
                    duration = rng.NextInt(360, 721);
                    break;
            }

            environment.NextWeatherTick = world.Tick + duration;
            environment.RngState = rng.State;
        }
    }
}
