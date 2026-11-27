using System;
using System.Collections.Generic;
using System.IO;
using LupinFarm.Core;
using LupinFarm.Data;
using LupinFarm.Storage;

namespace LupinFarm.Tests
{
    static class Rec
    {
        public static RunRecord Past(int run, double peak, params double[] checkpoints)
        {
            var r = new RunRecord();
            r.Run = run;
            r.StartedAt = "2026-11-01";
            r.EndedAt = "2026-11-02";
            r.Peak = peak;
            r.LastDay = 30;
            r.Normalize();
            long day = Content.FirstCheckpointDay;
            foreach (var v in checkpoints)
            {
                r.Checkpoints[Records.Key(day)] = v;
                day *= 2;
            }
            return r;
        }

        public static bool HasTitle(List<LogEntry> logs, string key)
        {
            foreach (var l in logs) if (l.Title != null && l.Title.Key == key) return true;
            return false;
        }

        public static bool HasAch(List<LogEntry> logs, string id)
        {
            foreach (var l in logs)
                if (l.Title != null && l.Title.Key == "t.achievement" && ((Msg)l.Title.Args[0]).Key == "ach." + id) return true;
            return false;
        }

        /// <summary>전자동 농장 (plant, harvest, sell 규칙).</summary>
        public static GameState AutoFarm()
        {
            var s = GameState.NewRun(1, 77);
            s.Gold = 5000;
            s.Fields[0].Open = 30;
            s.Fields[0].Harvester = true;
            s.Fields[0].Planter = true;
            s.Fields[0].Plots[12].Sprinkler = 3;
            s.Fields[0].Plots[17].Sprinkler = 3;
            Farming.SprinklerDay(s);
            s.Farm.Controller = 3;
            s.Farm.ShippingBin = true;
            s.Farm.AutoBuyer = true;
            s.Seeds["turnip"] = 40;
            foreach (var t in new[] { "when f1 ready do harvest", "when f1 empty do plant turnip", "when stock all >= 10 do sell all",
                "when seed turnip < 10 do buy seed turnip 30" })
            {
                var r = new RuleState();
                r.Text = t;
                r.On = true;
                s.Rules.Add(r);
            }
            return s;
        }
    }

    public static class RecordsTests
    {
        public static void TestNetWorth()
        {
            var s = GameState.NewRun(1, 3);
            s.Gold = 100;
            s.Storage["turnip"] = 10;
            s.Seeds["corn"] = 5;
            s.Invested = 300;
            double expect = 100 + 10 * Market.Price(s, "turnip") + 5 * 60 + 300;
            Assert.Equal(expect, Records.NetWorth(s), "gold + storage at today's price + seeds at seed cost + invested");
        }

        public static void TestCheckpointDays()
        {
            foreach (var d in new long[] { 7, 14, 28, 56, 112, 224 }) Assert.True(Content.IsCheckpointDay(d), "checkpoint " + d);
            foreach (var d in new long[] { 0, 1, 6, 21, 35, 42, 100 }) Assert.True(!Content.IsCheckpointDay(d), "not checkpoint " + d);
            Assert.Equal(7L, Content.NextCheckpointDay(0), "first");
            Assert.Equal(14L, Content.NextCheckpointDay(7), "after 7");
            Assert.Equal(28L, Content.NextCheckpointDay(20), "after 20");
        }

        public static void TestCheckpointAtDayEnd()
        {
            var s = GameState.NewRun(1, 3);
            s.Gold = 1000;
            var rep = Engine.Advance(s, 7 * 1440 - 1);
            Assert.Equal(0, rep.Checkpoints.Count, "Day 7 not over yet");
            Assert.True(!s.Checkpoints.ContainsKey("7"), "no record yet");
            rep = Engine.Advance(s, 1);
            Assert.Equal(1, rep.Checkpoints.Count, "Day 7 ends at Day 8 06:00");
            Assert.Equal(7, rep.Checkpoints[0], "day");
            Assert.Equal(1000.0, s.Checkpoints["7"], "net worth at that moment");

            // 꺼져 있던 동안(한 번에 큰 진행)에도 지나간 체크포인트를 모두 기록한다
            s.Gold = 4000;
            rep = Engine.Advance(s, 21 * 1440);
            Assert.Equal(2, rep.Checkpoints.Count, "14 and 28 in one step");
            Assert.Equal(4000.0, s.Checkpoints["28"], "28");
            Assert.Equal(4000.0, s.PeakNetWorth, "peak follows");
            Assert.Equal(8, s.PeakDay, "peak first reached at the end of Day 8");
        }

        public static void TestCheckpointCompare()
        {
            var p = Profile.New();
            var s = GameState.NewRun(2, 3);
            s.Checkpoints["7"] = 6000;
            bool best;
            Assert.Equal("checkpoint.first", Records.CheckpointLine(s, p, 7, out best).Key, "no past run: first record");
            Assert.True(!best, "first is not NEW BEST");

            p.Runs.Add(Rec.Past(1, 9000, 5000));
            Assert.Equal("checkpoint.best", Records.CheckpointLine(s, p, 7, out best).Key, "beats run 1");
            Assert.True(best, "NEW BEST");
            s.Checkpoints["7"] = 4000;
            Assert.Equal("checkpoint.behind", Records.CheckpointLine(s, p, 7, out best).Key, "behind run 1");

            // 현재 판의 기록은 비교 대상이 아니다
            var cur = Records.Current(s, p, "2026-12-01");
            cur.Checkpoints["7"] = 99999;
            s.Checkpoints["7"] = 6000;
            Records.CheckpointLine(s, p, 7, out best);
            Assert.True(best, "own record ignored");

            var rep = new AdvanceReport();
            rep.Checkpoints.Add(7);
            var summary = Records.CheckpointSummary(s, p, rep, "2026-12-01", new List<LogEntry>());
            Assert.Equal("welcome.checkpoint", ((Msg)summary[0]).Key, "offline summary line");
            bool hasLine = false;
            foreach (var o in Engine.OfflineLines(s, 3600, 3600, 2, rep, summary)) if (o is Msg && ((Msg)o).Key == "welcome.checkpoint") hasLine = true;
            Assert.True(hasLine, "welcome box lists the checkpoint");
            p.Achievements.Remove("new-best");
            var logs = Records.CheckpointLogs(s, p, rep, "2026-12-01");
            Assert.True(Rec.HasTitle(logs, "t.checkpoint"), "checkpoint event");
            Assert.True(Rec.HasAch(logs, "new-best"), "new-best achievement");
            Assert.True(!Rec.HasAch(Records.CheckpointLogs(s, p, rep, "2026-12-01"), "new-best"), "only once");
        }

        public static void TestDailyEarned()
        {
            Game.New();
            var s = Game.State;
            var p = Game.Profile;
            Records.Sync(s, p, "2026-12-01");
            Assert.Equal(1, p.Daily.Count, "today's row");
            s.Storage["turnip"] = 10;
            Game.Ok("/sell turnip");
            double first = s.Earned;
            Assert.True(first > 0, "earned counts sales");
            Records.Sync(s, p, "2026-12-01");
            Assert.Equal(first, p.Daily[0].Earned, "earned today");
            Assert.Equal(Records.NetWorth(s), p.Daily[0].NetWorth, "last net worth");

            Records.Sync(s, p, "2026-12-02");
            Assert.Equal(2, p.Daily.Count, "new day, new row");
            Assert.Equal(0.0, p.Daily[1].Earned, "nothing yet");
            s.Storage["corn"] = 2;
            Game.Ok("/sell all");
            Records.Sync(s, p, "2026-12-02");
            Assert.Equal(s.Earned - first, p.Daily[1].Earned, "only that day's sales");
            Assert.Equal(first, p.Daily[0].Earned, "yesterday unchanged");
        }

        public static void TestRunRecord()
        {
            Game.New();
            var s = Game.State;
            var p = Game.Profile;
            s.Gold = 5000;
            Records.Sync(s, p, "2026-12-01");
            var rr = p.RunOf(1);
            Assert.True(rr != null && rr.StartedAt == "2026-12-01" && rr.EndedAt == null, "current run record");
            Assert.Equal(5000.0, rr.Peak, "peak");
            s.Gold = 10;
            Records.Sync(s, p, "2026-12-01");
            Assert.Equal(5000.0, rr.Peak, "peak stays");
            Assert.Equal(5000.0, s.PeakNetWorth, "state peak stays");
        }

        public static void TestPrestige()
        {
            Game.New();
            var s = Game.State;
            var p = Game.Profile;
            Game.Err("/prestige");
            s.Gold = 410000;
            s.Fields[0].Open = 20;
            s.Storage["turnip"] = 5;
            s.Farm.Controller = 2;
            s.Unlocked.Add("melon");
            Game.Ok("/auto add when f1 ready do harvest");
            Game.Ok("/auto add when f1 empty do plant turnip");
            Game.Ok("/auto off 2");
            s.Rules[0].Runs = 9;
            Assert.Equal(2L, Prestige.Gain(s), "floor(sqrt(4.1))");

            var res = Game.Run("/prestige");
            Assert.True(res.Prompt != null && res.Prompt.Details.Count == 4, "box with details");
            var second = res.Prompt.OnSelect(0);
            Assert.True(second.Prompt != null && second.Prompt.Default == 1, "asks again, default No");
            Assert.Equal(UiAction.None, second.Prompt.OnSelect(1).Action, "No cancels");
            Assert.Equal(UiAction.Prestige, second.Prompt.OnSelect(0).Action, "Yes blooms");

            var logs = new List<LogEntry>();
            var n = Prestige.Bloom(s, p, "2026-12-05", 99, logs);
            Assert.Equal(2, n.Run, "next run");
            Assert.Equal(0L, n.GameMinute, "Day 1 spring 06:00");
            Assert.Equal(Content.StartGold, n.Gold, "gold reset");
            Assert.Equal(Content.StartOpenPlots, n.Fields[0].Open, "land reset");
            Assert.Equal(0, n.StorageUsed, "storage reset");
            Assert.Equal(0, n.Farm.Controller, "gear reset");
            Assert.True(!n.IsUnlocked("melon"), "unlocks reset");
            Assert.Equal(2, n.Rules.Count, "rules kept");
            Assert.True(n.Rules[0].On && !n.Rules[1].On, "on/off kept");
            Assert.Equal(0, n.Rules[0].Runs, "run count cleared");
            Assert.Equal(2L, p.Fertility, "Fertility added");
            Assert.Equal(2, p.CurrentRun, "profile run");
            var done = p.RunOf(1);
            Assert.True(done.EndedAt == "2026-12-05" && done.Fertility == 2, "run 1 closed");
            Assert.True(p.RunOf(2) != null && p.RunOf(2).EndedAt == null, "run 2 open");
            Assert.True(Rec.HasAch(logs, "first-bloom"), "first-bloom");
            Assert.Equal("t.bloom", logs[logs.Count - 1].Title.Key, "Lupin Bloom event last");
        }

        public static void TestPerks()
        {
            Assert.Equal(2619, Content.GrowNeed(2880, 2), "rich-soil Lv2: ceil(2880 / 1.1)");
            Assert.Equal(2400, Content.GrowNeed(2880, 4), "exact division stays exact");
            Assert.Equal(2880, Content.GrowNeed(2880, 0), "no bonus");

            Game.New();
            var s = Game.State;
            var p = Game.Profile;
            p.Fertility = 3;
            Game.Ok("/perks buy rich-soil");
            Assert.Equal(1, p.PerkLevel("rich-soil"), "Lv1");
            Assert.Equal(2L, p.Fertility, "cost 1");
            Assert.Equal(1, s.PerkLevel("rich-soil"), "state copy updated");
            Game.Ok("/perks buy rich-soil");
            Assert.Equal(0L, p.Fertility, "cost 2");
            Game.Err("/perks buy rich-soil");
            p.Fertility = 100;
            p.Perks["rule-memory"] = 4;
            Game.Err("/perks buy rule-memory");
            Assert.Equal(100L, p.Fertility, "max level costs nothing");

            // rich-soil: 이후 심는 작물부터
            s.Seeds["turnip"] = 2;
            Game.Ok("/plant turnip A1");
            var plot = Game.Plot(0, "A1");
            Assert.Equal(Content.GrowNeed(2880, 2), plot.Need, "need fixed at planting");
            plot.WaterUntil = 100000;
            Engine.Advance(s, plot.Need - 1);
            Assert.True(Engine.Stage(plot) != Engine.StageRipe, "not yet");
            Engine.Advance(s, 1);
            Assert.Equal(Engine.StageRipe, Engine.Stage(plot), "ripe 10% sooner");

            // market-sense: 단가 +3%/레벨
            var m = GameState.NewRun(1, 5);
            double before = Market.Price(m, "corn");
            m.Perks["market-sense"] = 2;
            Assert.Equal(Math.Round(before * 1.06), Market.Price(m, "corn"), "price +6%");

            // night-shift, rule-memory
            m.Perks["night-shift"] = 2;
            Assert.Equal(6.0, Engine.OfflineCapHours(m), "2h + 2 x 2h");
            m.Perks["rule-memory"] = 3;
            Assert.Equal(0, Rules.Slots(m), "needs a controller");
            m.Farm.Controller = 1;
            Assert.Equal(5, Rules.Slots(m), "2 + 3");

            // 다음 판부터: head-start, old-fields, seasoned-hands
            var perks = new Dictionary<string, int>();
            perks["head-start"] = 2;
            perks["old-fields"] = 3;
            perks["seasoned-hands"] = 2;
            var n = GameState.NewRun(2, 5, perks);
            Assert.Equal(1100.0, n.Gold, "100 + 2 x 500");
            Assert.Equal(12, n.Fields[0].Open, "6 + 3 x 2");
            Assert.Equal(3, n.ToolLevel("sickle"), "Lv1 + 2");
        }

        public static void TestDexAndAchievements()
        {
            Game.New();
            var s = Game.State;
            var p = Game.Profile;
            var p0 = Game.Plot(0, "A1");
            p0.Crop = "turnip";
            p0.Growth = Content.Crop("turnip").GrowMinutes;
            Game.Ok("/harvest A1");
            var logs = Records.Sync(s, p, "2026-12-01");
            Assert.Equal(1L, p.Dex["turnip"].Total, "dex total");
            Assert.True(p.Dex["turnip"].First == "2026-12-01" && p.Dex["turnip"].FirstRun == 1, "first harvest");
            Assert.True(Rec.HasTitle(logs, "t.dex_crop"), "dex event");
            Assert.True(Rec.HasAch(logs, "first-harvest"), "first-harvest");
            Assert.Equal(0, Records.Sync(s, p, "2026-12-01").Count, "nothing new");

            GameState.Add(s.Harvested, "turnip", 99);
            logs = Records.Sync(s, p, "2026-12-01");
            Assert.Equal(100L, p.Dex["turnip"].Total, "100");
            Assert.Equal(2, Content.DexStars(p.Dex["turnip"].Total), "two stars");
            Assert.True(Rec.HasTitle(logs, "t.dex_crop"), "star event");

            // 새 판이 되어도 도감은 이어진다
            var n = Prestige.Bloom(s, p, "2026-12-02", 1, new List<LogEntry>());
            GameState.Add(n.Harvested, "turnip", 5);
            Records.Sync(n, p, "2026-12-02");
            Assert.Equal(105L, p.Dex["turnip"].Total, "across runs");

            s = n;
            s.Gold = 1500;
            s.Fields.Add(FieldState.Create(6));
            s.Fields[0].Plots[3].Sprinkler = 1;
            logs = Records.Sync(s, p, "2026-12-02");
            Assert.True(Rec.HasAch(logs, "gold-1k") && Rec.HasAch(logs, "field-2") && Rec.HasAch(logs, "first-sprinkler"), "simple achievements");

            Assert.True(!Records.FourSeasons(p), "spring only");
            foreach (var c in new[] { "corn", "mushroom" })
            {
                var e = new DexEntry();
                e.Total = 1;
                p.Dex[c] = e;
            }
            Assert.True(Records.FourSeasons(p), "spring, summer·fall (corn), winter");
        }

        public static void TestFullAutoAchievement()
        {
            var s = Rec.AutoFarm();
            var p = Profile.New();
            Engine.Advance(s, 4 * 1440);
            Assert.True(s.FullAuto, "plant, harvest and sell ran on the same game day");
            var logs = Records.Sync(s, p, "2026-12-01");
            Assert.True(Rec.HasAch(logs, "full-auto") && Rec.HasAch(logs, "first-rule"), "rule achievements");
        }

        /// <summary>체크포인트·최고 기록·판매 수입·수확 수도 한 번에 진행한 것과 잘게 나눈 것이 같아야 한다.</summary>
        public static void TestBigStepRecords()
        {
            var a = Rec.AutoFarm();
            var b = Rec.AutoFarm();
            Engine.Advance(a, 15 * 1440);
            for (int i = 0; i < 15 * 144; i++) Engine.Advance(b, 10);
            Assert.Equal(b.Checkpoints.Count, a.Checkpoints.Count, "checkpoint count");
            Assert.Equal(b.Checkpoints["7"], a.Checkpoints["7"], "Day 7");
            Assert.Equal(b.Checkpoints["14"], a.Checkpoints["14"], "Day 14");
            Assert.Equal(b.PeakNetWorth, a.PeakNetWorth, "peak");
            Assert.Equal(b.Earned, a.Earned, "earned");
            Assert.Equal(GameState.Count(b.Harvested, "turnip"), GameState.Count(a.Harvested, "turnip"), "harvested");
            Assert.True(a.Earned > 0 && GameState.Count(a.Harvested, "turnip") > 0, "the farm worked");
        }

        public static void TestCropsCalendar()
        {
            var corn = Content.Crop("corn");
            var turnip = Content.Crop("turnip");
            Assert.Equal(14, Content.SeasonDaysLeft(corn, 8), "summer 1: summer + fall");
            Assert.Equal(1, Content.SeasonDaysLeft(corn, 21), "fall 7: last day");
            Assert.Equal(0, Content.SeasonDaysLeft(turnip, 8), "turnip not in summer");
            Assert.Equal(21, Content.DaysUntilSeason(turnip, 8), "next spring = Day 29");
            Assert.Equal(0, Content.SeasonOfDay(29), "Day 29 is spring");
            Assert.Equal(21, Content.DaysUntilSeason(Content.Crop("mushroom"), 1), "winter starts Day 22");
            Assert.Equal(0, Content.DaysUntilSeason(turnip, 3), "in season now");

            Game.New();
            var r = Game.Run("/crops");
            Assert.Equal(LogKind.Info, Game.Kind(r), "table");
            Assert.Equal(1 + Content.Crops.Length + 1, r.Logs[0].Lines.Count, "header + crops + hint");
            Game.State.Fields[0].Greenhouse = true;
            Assert.Equal(1 + Content.Crops.Length + 2, Game.Run("/crops").Logs[0].Lines.Count, "greenhouse line");

            Game.State.Fields[0].Greenhouse = false;
            var err = Game.Run("/plant corn A1");
            Assert.Equal(LogKind.Error, Game.Kind(err), "corn is not a spring crop");
            Assert.Equal("err.season_when", ((Msg)err.Logs[0].Lines[1]).Key, "says when and points to /crops");
        }

        static PlotState Put(string cell, string crop, int growth, long waterUntil)
        {
            var p = Game.Plot(0, cell);
            p.Crop = crop;
            p.Growth = growth;
            p.WaterUntil = waterUntil;
            return p;
        }

        static string Cells(object line)
        {
            return (string)((Pad)((Joined)line).Items[1]).Inner;
        }

        static object Note(object line)
        {
            var items = ((Joined)line).Items;
            return items[items.Count - 1];
        }

        /// <summary>/growing: 묶음과 순서, 마름·물 부족 표시, 스프링클러 칸은 물이 계속 있음 (사용자 요청).</summary>
        public static void TestGrowing()
        {
            Assert.Equal("A1:A3, A6, B1:B2", RangeParser.Runs(new List<int> { 0, 1, 2, 5, 10, 11 }), "runs per row");

            Game.New();
            var s = Game.State;
            s.Fields[0].Open = 30;
            foreach (var c in new[] { "A1", "A2", "A3" }) Put(c, "turnip", 1000, 3000);
            Put("A5", "turnip", 2880, 0);
            Put("B1", "corn", 0, 0);
            Put("C1", "corn", 0, 600);
            Game.Plot(0, "B9").Sprinkler = 1;
            Put("B10", "corn", 0, 1440);

            var r = Game.Run("/growing");
            Assert.Equal(LogKind.Info, Game.Kind(r), "table");
            var lines = r.Logs[0].Lines;
            Assert.Equal(1 + 5, lines.Count, "header + 5 groups");
            Assert.Equal("A5", Cells(lines[1]), "ripe first");
            Assert.Equal("grow.note_harvest", ((Msg)Note(lines[1])).Key, "harvest hint");
            Assert.Equal("A1:A3", Cells(lines[2]), "same crop and time grouped, soonest next");
            Assert.Equal("B10", Cells(lines[3]), "sprinkler plot counts as watered");
            Assert.Equal("", Note(lines[3]), "no water warning under a sprinkler");
            Assert.Equal("C1", Cells(lines[4]), "water runs out first");
            Assert.Equal("grow.note_short", ((Msg)Note(lines[4])).Key, "short water note");
            Assert.Equal("B1", Cells(lines[5]), "dry last");
            Assert.Equal("grow.note_dry", ((Msg)Note(lines[5])).Key, "dry note");

            Assert.Equal(1 + 3, Game.Run("/growing corn").Logs[0].Lines.Count, "crop filter");
            Game.Err("/growing f2");
            Game.Err("/growing nope");

            // 정보 칸·스피너의 "다음"도 스프링클러 칸을 끝까지 자라는 것으로 본다
            foreach (var c in new[] { "A1", "A2", "A3", "A5" }) Game.Plot(0, c).Crop = null;
            var st = Engine.Stats(s, 0);
            Assert.Equal((long)Content.Crop("corn").GrowMinutes, st.NextMinutes, "next = corn under the sprinkler");
            Assert.True(st.NextCells.Count == 1 && st.NextCells[0] == 19, "B10");

            Game.New();
            Assert.Equal("grow.none", ((Msg)Game.Run("/growing").Logs[0].Lines[0]).Key, "nothing growing");
        }

        public static void TestInfoCommands()
        {
            Game.New();
            var p = Game.Profile;
            Records.Sync(Game.State, p, "2026-12-01");
            p.Runs.Add(Rec.Past(5, 2000000, 10000, 60000, 300000, 2000000));
            foreach (var cmd in new[] { "/records", "/records daily", "/rank", "/dex", "/perks", "/status", "/help" })
                Assert.Equal(LogKind.Info, Game.Kind(Game.Run(cmd)), cmd);
            var rank = Game.Run("/rank").Logs[0];
            Assert.Equal(3, rank.Lines.Count, "header + 2 runs");
            var rec = Game.Run("/records").Logs[0];
            Assert.True(rec.Lines.Count >= 1 + 4, "rows up to the last checkpoint of past runs (7, 14, 28, 56)");
            Game.Err("/records weekly");
            Game.Err("/perks sell rich-soil");
        }

        /// <summary>/help &lt;장비&gt; (M4 K24): 모든 농기구·장비에 설명·예시가 있고, 규칙 장비는 컨트롤러가 없으면 알린다.</summary>
        public static void TestItemHelp()
        {
            Game.New();
            foreach (var id in Commands.Items)
            {
                Assert.True(Array.IndexOf(Commands.Names, id) < 0, "not a command name: " + id);
                Assert.True(Strings.En.ContainsKey("item." + id) && Strings.En.ContainsKey("item_ex." + id), "strings for " + id);
                var r = Game.Run("/help " + id);
                Assert.Equal(LogKind.Info, Game.Kind(r), "/help " + id);
                var lines = r.Logs[0].Lines;
                Assert.Equal("item.head", ((Msg)lines[0]).Key, "head " + id);
                Assert.Equal("item_ex." + id, ((Msg)lines[2]).Key, "example " + id);
            }
            var h = Game.Run("/help harvester").Logs[0].Lines;
            Assert.Equal("item.needs_controller", ((Msg)h[h.Count - 1]).Key, "warns when there is no controller");
            Assert.Equal("item.now_none", ((Msg)h[3]).Key, "no harvester yet");

            Game.State.Gold = 100000;
            Game.Ok("/buy harvester");
            Game.Ok("/buy controller");
            h = Game.Run("/help harvester").Logs[0].Lines;
            Assert.Equal(4, h.Count, "no warning once the controller is there");
            Assert.Equal("item.now_fields", ((Msg)h[3]).Key, "lists the fields that have it");
            Assert.Equal("item.now_controller", ((Msg)Game.Run("/help controller").Logs[0].Lines[3]).Key, "controller level");
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/help /plant")), "commands still work");
            Game.Err("/help nope");
        }

        public static void TestProfileRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(dir);
                Game.New();
                var s = Game.State;
                var p = Game.Profile;
                s.Gold = 3000;
                s.Checkpoints["7"] = 2500;
                GameState.Add(s.Harvested, "turnip", 3);
                s.Fields[0].Plots[0].Crop = "turnip";
                s.Fields[0].Plots[0].Need = 2600;
                p.Fertility = 7;
                p.Perks["rich-soil"] = 2;
                Records.Sync(s, p, "2026-12-01");
                p.Achievements["new-best"] = "2026-12-01";
                store.Save(s);
                store.SaveProfile(p);

                bool bak;
                var s2 = store.LoadSave(out bak);
                var p2 = store.LoadProfile();
                Assert.Equal(2500.0, s2.Checkpoints["7"], "state checkpoint");
                Assert.Equal(3, GameState.Count(s2.Harvested, "turnip"), "state harvested");
                Assert.Equal(2600, s2.Fields[0].Plots[0].Need, "plot need");
                Assert.Equal(7L, p2.Fertility, "Fertility");
                Assert.Equal(2, p2.PerkLevel("rich-soil"), "perk");
                Assert.Equal(1, p2.Runs.Count, "run record");
                Assert.Equal(2500.0, p2.Runs[0].Checkpoints["7"], "run checkpoint");
                Assert.Equal(1, p2.Daily.Count, "daily");
                Assert.Equal(3L, p2.Dex["turnip"].Total, "dex");
                Assert.True(p2.Achievements.ContainsKey("first-harvest") && p2.Achievements.ContainsKey("new-best"), "achievements");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        public static void TestOldProfileLoads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "profile.json"),
                    "{\"Version\":1,\"CurrentRun\":1,\"Settings\":{\"Lang\":\"ko\",\"Timescale\":60,\"Confirm\":true,\"BossKey\":\"Ctrl+Alt+H\"},\"History\":[\"/status\"]}");
                File.WriteAllText(Path.Combine(dir, "save.json"),
                    "{\"Version\":1,\"Run\":1,\"GameMinute\":100,\"Gold\":50,\"Fields\":[{\"Open\":6,\"Plots\":[{\"Crop\":\"turnip\",\"Growth\":10}]}]}");
                var store = new SaveStore(dir);
                var p = store.LoadProfile();
                bool bak;
                var s = store.LoadSave(out bak);
                Assert.True(p.Runs != null && p.Daily != null && p.Dex != null && p.Achievements != null && p.Perks != null, "M3 profile gets M4 defaults");
                Assert.Equal("ko", p.Settings.Lang, "settings kept");
                Assert.True(s.Checkpoints != null && s.Harvested != null && s.Perks != null, "M3 save gets M4 defaults");
                Assert.Equal(Content.Crop("turnip").GrowMinutes, Engine.Need(s.Fields[0].Plots[0], Content.Crop("turnip")), "old plot uses crop time");
                Records.Sync(s, p, "2026-12-01");
                Assert.True(p.RunOf(1) != null && p.RunOf(1).StartedAt == "2026-12-01", "old run gets a record");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
