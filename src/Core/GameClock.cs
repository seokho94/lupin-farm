using System.Globalization;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    /// <summary>게임 분(0 = Day 1 봄 06:00) 계산 (02 2절).</summary>
    public static class GameClock
    {
        public const int MinutesPerDay = 1440;
        public const int DayStartMinute = 360;

        public static int Day(long gameMinute)
        {
            return (int)(gameMinute / MinutesPerDay) + 1;
        }

        public static int MinuteOfDay(long gameMinute)
        {
            return (int)((DayStartMinute + gameMinute % MinutesPerDay) % MinutesPerDay);
        }

        public static string Time(long gameMinute)
        {
            int m = MinuteOfDay(gameMinute);
            return (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>0 봄, 1 여름, 2 가을, 3 겨울.</summary>
        public static int Season(long gameMinute)
        {
            return ((Day(gameMinute) - 1) / Content.SeasonDays) % 4;
        }

        public static int SeasonDay(long gameMinute)
        {
            return (Day(gameMinute) - 1) % Content.SeasonDays + 1;
        }

        public static bool IsNight(long gameMinute)
        {
            int m = MinuteOfDay(gameMinute);
            return m >= 22 * 60 || m < 6 * 60;
        }

        /// <summary>실제 초 → 게임 분 (배속 = 게임 1시간당 실제 초).</summary>
        public static double GameMinutesFromReal(double realSeconds, int timescale)
        {
            return realSeconds * 60.0 / timescale;
        }

        /// <summary>게임 분 → 실제 초.</summary>
        public static double RealSecondsFromGame(double gameMinutes, int timescale)
        {
            return gameMinutes * timescale / 60.0;
        }
    }
}
