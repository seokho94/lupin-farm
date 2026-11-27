using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LupinFarm.Core;
using LupinFarm.Data;
using LupinFarm.Storage;

namespace LupinFarm.Tests
{
    static class Game
    {
        public static GameState State;
        public static Profile Profile;

        public static void New()
        {
            State = GameState.NewRun(1, 42);
            Profile = Profile.New();
            Profile.Settings.Confirm = false;
        }

        public static CommandResult Run(string line, int view = 0)
        {
            var c = new CommandContext();
            c.State = State;
            c.Profile = Profile;
            c.ViewField = view;
            return Commands.Execute(c, line);
        }

        public static LogKind Kind(CommandResult r)
        {
            Assert.True(r.Logs.Count > 0, "has log");
            return r.Logs[r.Logs.Count - 1].Kind;
        }

        public static void Ok(string line)
        {
            Assert.Equal(LogKind.Ok, Kind(Run(line)), line);
        }

        public static void Err(string line)
        {
            Assert.Equal(LogKind.Error, Kind(Run(line)), line);
        }

        public static PlotState Plot(int field, string cell)
        {
            int i;
            RangeParser.TryParseCell(cell, out i);
            return State.Fields[field].Plots[i];
        }
    }

    public static class EngineTests
    {
        public static void TestGrowsOnlyWhileWet()
        {
            Game.New();
            Game.Ok("/buy seed turnip 1");
            Game.Ok("/plant turnip A1");
            var p = Game.Plot(0, "A1");
            Engine.Advance(Game.State, 600);
            Assert.Equal(0, p.Growth, "dry soil: no growth");

            Game.Ok("/water A1");
            var rep = Engine.Advance(Game.State, 1440 + 300);
            Assert.Equal(1440, p.Growth, "grows 24h then stops");
            Assert.Equal(1, rep.DriedTotal(), "reported dry once");
            Assert.Equal(Engine.StageSprout, Engine.Stage(p), "50% is sprout");

            Engine.Advance(Game.State, 5000);
            Assert.Equal(1440, p.Growth, "does not wither or grow without water");

            Game.Ok("/water A1");
            rep = Engine.Advance(Game.State, 1440);
            Assert.Equal(Engine.StageRipe, Engine.Stage(p), "ripe after 2 wet days");
            Assert.Equal(1, rep.ReadyTotal()["turnip"], "ready event");
        }

        public static void TestWetSoilBeforePlanting()
        {
            Game.New();
            Game.Ok("/water A2");
            Game.Ok("/buy seed turnip 1");
            Game.Ok("/plant turnip A2");
            Engine.Advance(Game.State, 600);
            Assert.Equal(600, Game.Plot(0, "A2").Growth, "planting keeps the water");
        }

        public static void TestOfflineCap()
        {
            double applied;
            long gm = Engine.OfflineGameMinutes(64 * 3600, 2, 60, out applied);
            Assert.Equal(7200.0, applied, "cap 2h");
            Assert.Equal(7200L, gm, "2 real hours = 5 game days");
            gm = Engine.OfflineGameMinutes(-50, 2, 60, out applied);
            Assert.Equal(0L, gm, "clock went back");
        }

        public static void TestStatsNext()
        {
            Game.New();
            Game.Ok("/buy seed turnip 2");
            Game.Ok("/plant turnip A1");
            Game.Ok("/plant turnip A2");
            Game.Ok("/water A1");
            var st = Engine.Stats(Game.State, 0);
            Assert.Equal(2, st.Growing, "growing");
            Assert.Equal(1, st.Dry, "A2 is dry");
            Assert.Equal(-1L, st.NextMinutes, "A1 needs more water before it can finish");
            Engine.Advance(Game.State, 1440);
            Game.Ok("/water A1");
            st = Engine.Stats(Game.State, 0);
            Assert.Equal(1440L, st.NextMinutes, "A1 finishes in 1 day");
            Assert.Equal("turnip", st.NextCrop, "next crop");
        }
    }

    public static class CommandTests
    {
        public static void TestFarmLoop()
        {
            Game.New();
            Game.Ok("/buy seed turnip 6");
            Assert.Equal(40.0, Game.State.Gold, "100 - 60");
            Game.Err("/plant turnip A1:A6");
            for (int i = 1; i <= 6; i++) Game.Ok("/plant turnip A" + i);
            for (int day = 0; day < 2; day++)
            {
                for (int i = 1; i <= 6; i++) Game.Ok("/water A" + i);
                Engine.Advance(Game.State, 1440);
            }
            for (int i = 0; i < 6; i++) Game.Ok("/harvest ready");
            Game.Err("/harvest ready");
            Assert.Equal(6, GameState.Count(Game.State.Storage, "turnip"), "storage");
            double expected = 40 + Market.Quote(Game.State, "turnip", 6).Total;
            Assert.True(expected > 100 && expected < 400, "about 6 x 30G after two market days");
            Game.Ok("/sell all");
            Assert.Equal(expected, Game.State.Gold, "sold at the quoted price");
        }

        public static void TestToolUpgrade()
        {
            Game.New();
            Game.State.Gold = 1000;
            Game.Ok("/upgrade bag");
            Assert.Equal(2, Game.State.ToolLevel("bag"), "bag Lv2");
            Assert.Equal(800.0, Game.State.Gold, "Lv2 costs 200");
            Assert.Equal(200.0, Game.State.Invested, "invested");
            Game.Ok("/buy seed turnip 6");
            Game.Ok("/plant turnip A1:A6");
            Game.Err("/plant turnip all");
            Game.Err("/upgrade bag");
        }

        public static void TestHarvestReadyBySickle()
        {
            Game.New();
            Game.State.Gold = 10000;
            Game.Ok("/expand 14");
            Assert.Equal(20, Game.State.Fields[0].Open, "A and B rows open");
            foreach (var cell in new[] { "A1", "A2", "B1" })
            {
                var p = Game.Plot(0, cell);
                p.Crop = "turnip";
                p.Growth = 2880;
            }
            Game.Ok("/upgrade sickle");
            var r = Game.Run("/harvest ready");
            Assert.Equal(2, GameState.Count(Game.State.Storage, "turnip"), "Lv2 takes the first row with ripe plots");
            Assert.Equal(2, r.Logs[0].Lines.Count, "tells there is more");
        }

        public static void TestStorageFull()
        {
            Game.New();
            Game.State.Storage["corn"] = 39;
            Game.State.Tools["sickle"] = 2;
            for (int i = 1; i <= 3; i++)
            {
                var p = Game.Plot(0, "A" + i);
                p.Crop = "turnip";
                p.Growth = 2880;
            }
            Game.Ok("/harvest A1:A3");
            Assert.Equal(40, Game.State.StorageUsed, "fills to 40");
            Assert.Equal("turnip", Game.Plot(0, "A2").Crop, "rest stays");
        }

        public static void TestExpandCosts()
        {
            Game.New();
            Game.State.Gold = 100000;
            Game.Ok("/expand");
            Assert.Equal(7, Game.State.Fields[0].Open, "one plot");
            Assert.Equal(99950.0, Game.State.Gold, "first plot 50G");
            Game.Ok("/expand 23");
            Game.Err("/expand");
            Game.Ok("/expand field");
            Assert.Equal(2, Game.State.Fields.Count, "new field");
            Assert.Equal(6, Game.State.Fields[1].Open, "new field opens 6 plots");
            Game.Ok("/expand f2");
            Assert.Equal(7, Game.State.Fields[1].Open, "expand field 2");
        }

        public static void TestConfirmPrompt()
        {
            Game.New();
            Game.Profile.Settings.Confirm = true;
            Game.State.Gold = 300;
            var r = Game.Run("/upgrade can");
            Assert.True(r.Prompt != null, "asks before a big purchase");
            Assert.Equal(1, Game.State.ToolLevel("can"), "not yet bought");
            var after = r.Prompt.OnSelect(1);
            Assert.Equal(1, Game.State.ToolLevel("can"), "No keeps gold");
            after = r.Prompt.OnSelect(0);
            Assert.Equal(2, Game.State.ToolLevel("can"), "Yes buys");
            Assert.True(Game.Run("/buy seed turnip 20").Prompt == null, "seeds never ask");
        }

        public static void TestErrorsKeepState()
        {
            Game.New();
            Game.Err("/plant potato A1");
            Game.Err("/plant banana A1");
            Game.Err("/water D1");
            Game.Err("/sell turnip");
            Game.Err("/buy seed turnip 99999999");
            Game.Err("/remove A1");
            Game.Err("/field 3");
            Game.Err("/nope");
            Game.Err("/records weekly");
            Game.Err("/prestige");
            Game.Err("/perks buy nope");
            Game.Err("/perks buy rich-soil");
            Game.Err("/auto nope");
            Game.Err("/unlock turnip");
            Game.Err("/upgrade storage");
            Game.Err("hello");
            Assert.Equal(Content.StartGold, Game.State.Gold, "gold unchanged");
        }

        public static void TestConfig()
        {
            Game.New();
            Game.Ok("/config lang ko");
            Assert.Equal("ko", Game.Profile.Settings.Lang, "lang");
            Game.Err("/config lang jp");
            Game.Ok("/config timescale 10");
            Assert.Equal(10, Game.Profile.Settings.Timescale, "timescale");
            Game.Err("/config timescale 1");
            var r = Game.Run("/config bosskey ctrl+shift+f9");
            Assert.Equal(UiAction.BossKeyChanged, r.Action, "rebind");
            Assert.Equal("Ctrl+Shift+F9", Game.Profile.Settings.BossKey, "normalized");
        }
    }

    public static class SaveTests
    {
        public static void TestRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(dir);
                Game.New();
                Game.Ok("/buy seed turnip 3");
                Game.Ok("/plant turnip A1");
                Game.Ok("/water A1");
                Engine.Advance(Game.State, 700);
                Game.Profile.AddHistory("/plant turnip A1");
                store.Save(Game.State);
                store.Save(Game.State);
                store.SaveProfile(Game.Profile);
                Assert.True(File.Exists(Path.Combine(dir, "save.json.bak")), "keeps .bak");

                bool bak;
                var s = store.LoadSave(out bak);
                Assert.True(!bak, "main file");
                Assert.Equal(700L, s.GameMinute, "clock");
                Assert.Equal("turnip", s.Fields[0].Plots[0].Crop, "crop");
                Assert.Equal(700, s.Fields[0].Plots[0].Growth, "growth");
                Assert.Equal(2, GameState.Count(s.Seeds, "turnip"), "seeds");
                Assert.Equal(1, store.LoadProfile().History.Count, "history");

                File.WriteAllText(Path.Combine(dir, "save.json"), "{ broken");
                s = store.LoadSave(out bak);
                Assert.True(bak && s != null, "falls back to .bak");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }

    public static class StringsTests
    {
        static int MaxArg(string s)
        {
            int max = -1;
            foreach (Match m in Regex.Matches(s, @"\{(\d+)[^}]*\}")) max = Math.Max(max, int.Parse(m.Groups[1].Value));
            return max;
        }

        public static void TestSameKeys()
        {
            foreach (var k in Strings.En.Keys) Assert.True(Strings.Ko.ContainsKey(k), "ko missing " + k);
            foreach (var k in Strings.Ko.Keys) Assert.True(Strings.En.ContainsKey(k), "en missing " + k);
        }

        public static void TestSameArgs()
        {
            foreach (var kv in Strings.En)
                Assert.Equal(MaxArg(kv.Value), MaxArg(Strings.Ko[kv.Key]), "args of " + kv.Key);
        }

        public static void TestDynamicKeys()
        {
            var keys = new List<string>();
            foreach (var c in Content.Crops) keys.Add("crop." + c.Id);
            foreach (var t in Content.Tools) keys.Add("tool." + t);
            for (int i = 0; i < 4; i++) keys.Add("season." + i);
            for (int i = 1; i <= 3; i++) keys.Add("range." + i);
            foreach (var n in Commands.Names)
            {
                keys.Add("cmd." + n);
                keys.Add("usage." + n);
                keys.Add("help." + n);
                keys.Add("ex." + n);
            }
            foreach (var k in keys) Assert.True(Strings.En.ContainsKey(k), "missing " + k);
        }
    }
}
