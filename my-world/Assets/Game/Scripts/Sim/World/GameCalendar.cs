using System;

namespace MyAiWorld.Sim.World
{
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }

    public readonly struct GameDateTime : IEquatable<GameDateTime>
    {
        public GameDateTime(long absoluteTick, int year, Season season, int dayOfSeason, int minuteOfDay)
        {
            AbsoluteTick = absoluteTick;
            Year = year;
            Season = season;
            DayOfSeason = dayOfSeason;
            MinuteOfDay = minuteOfDay;
        }

        public long AbsoluteTick { get; }
        public int Year { get; }
        public Season Season { get; }
        public int DayOfSeason { get; }
        public int MinuteOfDay { get; }
        public int Hour => MinuteOfDay / 60;
        public int Minute => MinuteOfDay % 60;
        public int AbsoluteDay => (int)(AbsoluteTick / GameCalendar.TicksPerDay);

        public string SeasonName
        {
            get
            {
                switch (Season)
                {
                    case Season.Spring: return "Spring";
                    case Season.Summer: return "Summer";
                    case Season.Autumn: return "Autumn";
                    default: return "Winter";
                }
            }
        }

        public string ClockText => Hour.ToString("00") + ":" + Minute.ToString("00");

        public string ShortText => SeasonName + " " + DayOfSeason + ", Y" + Year + "  " + ClockText;

        public bool Equals(GameDateTime other)
        {
            return AbsoluteTick == other.AbsoluteTick;
        }

        public override bool Equals(object obj) => obj is GameDateTime other && Equals(other);
        public override int GetHashCode() => AbsoluteTick.GetHashCode();
        public override string ToString() => ShortText;
    }

    public static class GameCalendar
    {
        public const int TicksPerMinute = 1;
        public const int MinutesPerHour = 60;
        public const int HoursPerDay = 24;
        public const int TicksPerDay = MinutesPerHour * HoursPerDay;
        public const int DaysPerSeason = 28;
        public const int SeasonsPerYear = 4;
        public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;
        public const long StartTick = 6 * MinutesPerHour;

        public static GameDateTime FromTick(long tick)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            long totalDays = tick / TicksPerDay;
            int minuteOfDay = (int)(tick % TicksPerDay);
            int year = (int)(totalDays / DaysPerYear) + 1;
            int dayOfYear = (int)(totalDays % DaysPerYear);
            Season season = (Season)(dayOfYear / DaysPerSeason);
            int dayOfSeason = dayOfYear % DaysPerSeason + 1;
            return new GameDateTime(tick, year, season, dayOfSeason, minuteOfDay);
        }

        public static bool IsDaytime(long tick)
        {
            int minute = (int)(tick % TicksPerDay);
            return minute >= 6 * 60 && minute < 19 * 60;
        }
    }
}
