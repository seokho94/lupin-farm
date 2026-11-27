using System.Collections.Generic;
using LupinFarm.Core;
using LupinFarm.Data;

namespace LupinFarm.Tests
{
    public static class ClockTests
    {
        public static void TestDayAndTime()
        {
            Assert.Equal(1, GameClock.Day(0), "day 1 at minute 0");
            Assert.Equal("06:00", GameClock.Time(0), "06:00 start");
            Assert.Equal("14:00", GameClock.Time(8 * 60), "8h later");
            Assert.Equal("05:59", GameClock.Time(1439), "just before next day");
            Assert.Equal(2, GameClock.Day(1440), "day 2");
            Assert.True(!GameClock.IsNight(0), "06:00 is day");
            Assert.True(GameClock.IsNight(16 * 60), "22:00 is night");
        }

        public static void TestSeasons()
        {
            Assert.Equal(0, GameClock.Season(0), "spring");
            Assert.Equal(1, GameClock.Season(7 * 1440), "day 8 is summer");
            Assert.Equal(1, GameClock.SeasonDay(7 * 1440), "summer 1");
            Assert.Equal(0, GameClock.Season(28 * 1440), "day 29 is spring again");
        }

        public static void TestTimescale()
        {
            Assert.Equal(1.0, GameClock.GameMinutesFromReal(1, 60), "1s = 1 game minute at 60");
            Assert.Equal(6.0, GameClock.GameMinutesFromReal(1, 10), "1s = 6 game minutes at 10");
            Assert.Equal(1440.0, GameClock.RealSecondsFromGame(1440, 60), "a game day = 24 real minutes");
        }
    }

    public static class NumberFormatTests
    {
        public static void TestFormats()
        {
            Assert.Equal("1,240", NumberFormat.Num(1240), "comma");
            Assert.Equal("9,999", NumberFormat.Num(9999), "below 10K");
            Assert.Equal("10.0K", NumberFormat.Num(10000), "10K");
            Assert.Equal("12.4K", NumberFormat.Num(12400), "12.4K");
            Assert.Equal("310K", NumberFormat.Num(310000), "310K");
            Assert.Equal("2.10M", NumberFormat.Num(2100000), "2.10M");
            Assert.Equal("1.00M", NumberFormat.Num(999999), "rounds up to M");
            Assert.Equal("1.2e36", NumberFormat.Num(1.2e36), "exponent past Dc");
            Assert.Equal("1,240G", NumberFormat.Gold(1240), "gold");
            Assert.Equal("200K\u00A0G", NumberFormat.Gold(200000), "abbreviated gold is spaced (no-break space)");
        }
    }

    public static class RangeTests
    {
        static Target Parse(string s, int view = 0, int fields = 2, bool ready = false)
        {
            Target t;
            Msg e;
            Assert.True(RangeParser.TryParse(s, view, fields, ready, out t, out e), "parse " + s);
            return t;
        }

        static void Bad(string s, int fields = 2)
        {
            Target t;
            Msg e;
            Assert.True(!RangeParser.TryParse(s, 0, fields, false, out t, out e), "should fail: " + s);
        }

        public static void TestCells()
        {
            Assert.Equal(0, Parse("A1").Cells[0], "A1");
            Assert.Equal(29, Parse("c10").Cells[0], "C10");
            Assert.Equal(4, Parse("A1:A4").Cells.Count, "A1:A4");
            Assert.Equal(9, Parse("A1:C3").Cells.Count, "rect");
            Assert.Equal(10, Parse("B").Cells.Count, "row");
            Assert.Equal(30, Parse("all").Cells.Count, "all");
            Assert.Equal(1, Parse("f2.A1").Field, "field prefix");
            Assert.True(Parse("f2.ready", 0, 2, true).Ready, "ready");
        }

        public static void TestBad()
        {
            Bad("D1");
            Bad("A11");
            Bad("A0");
            Bad("f3.A1");
            Bad("f0.A1");
            Bad("x");
            Bad("ready");
        }

        public static void TestToolLevels()
        {
            Assert.Equal(1, RangeParser.ToolLevelNeeded(Parse("A1")), "one plot");
            Assert.Equal(2, RangeParser.ToolLevelNeeded(Parse("A1:A4")), "within a row");
            Assert.Equal(2, RangeParser.ToolLevelNeeded(Parse("B")), "row");
            Assert.Equal(3, RangeParser.ToolLevelNeeded(Parse("A1:B2")), "two rows");
            Assert.Equal(3, RangeParser.ToolLevelNeeded(Parse("all")), "all");
        }

        public static void TestCompress()
        {
            Assert.Equal("A1:A4", RangeParser.Compress(new List<int> { 0, 1, 2, 3 }), "contiguous");
            Assert.Equal("B3", RangeParser.Compress(new List<int> { 12 }), "single");
            Assert.Equal(null, RangeParser.Compress(new List<int> { 0, 2 }), "gap");
        }
    }

    public static class KeyComboTests
    {
        public static void TestParse()
        {
            KeyCombo k;
            Assert.True(KeyCombo.TryParse("Ctrl+Alt+H", out k), "default");
            Assert.Equal(KeyCombo.ModCtrl | KeyCombo.ModAlt, k.Mods, "mods");
            Assert.Equal((uint)'H', k.Vk, "vk");
            Assert.True(KeyCombo.TryParse("alt+shift+f12", out k), "F12");
            Assert.Equal("Alt+Shift+F12", k.Display, "display");
            Assert.True(!KeyCombo.TryParse("Shift+H", out k), "shift alone is not enough");
            Assert.True(!KeyCombo.TryParse("H", out k), "no modifier");
        }
    }
}
