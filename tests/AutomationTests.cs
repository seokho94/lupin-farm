using System;
using System.Collections.Generic;
using System.IO;
using LupinFarm.Core;
using LupinFarm.Data;
using LupinFarm.Storage;

namespace LupinFarm.Tests
{
    static class Auto
    {
        public static GameState S
        {
            get { return Game.State; }
        }

        /// <summary>골드 넉넉, 구역 1 전체 열림.</summary>
        public static void Setup()
        {
            Game.New();
            S.Gold = 200000;
            S.Fields[0].Open = 30;
        }

        public static void Ripe(string cell, string crop)
        {
            var p = Game.Plot(0, cell);
            p.Crop = crop;
            p.Growth = Content.Crop(crop).GrowMinutes;
        }

        public static bool Wet(string cell)
        {
            return Engine.IsWet(Game.Plot(0, cell), S.GameMinute);
        }

        public static void Rule(string text)
        {
            Game.Ok("/auto add " + text);
        }
    }

    public static class SprinklerTests
    {
        static int Cell(string name)
        {
            int i;
            Assert.True(RangeParser.TryParseCell(name, out i), "cell " + name);
            return i;
        }

        public static void TestRanges()
        {
            Assert.Equal(4, Content.SprinklerOffsets(1).Count, "Lv1 cross");
            Assert.Equal(8, Content.SprinklerOffsets(2).Count, "Lv2 ring");
            Assert.Equal(24, Content.SprinklerOffsets(3).Count, "Lv3 5x5");
            // 구역은 3줄이라 실제로 닿는 칸은 줄어든다 (M4 K23)
            Assert.Equal(4, Farming.Reach(Cell("B5"), 1).Count, "Lv1 middle");
            Assert.Equal(2, Farming.Reach(Cell("A1"), 1).Count, "Lv1 corner");
            Assert.Equal(5, Farming.Reach(Cell("A5"), 2).Count, "Lv2 top row");
            Assert.Equal(14, Farming.Reach(Cell("B5"), 3).Count, "Lv3 at most 14");
            Assert.Equal(14, Farming.Reach(Cell("A5"), 3).Count, "Lv3 reaches all 3 rows from A");
            Assert.Equal(8, Farming.Reach(Cell("C1"), 3).Count, "Lv3 corner");

            Auto.Setup();
            Game.Ok("/buy sprinkler 3");
            Assert.Equal(3, Auto.S.BoxSprinklers, "three in the box");
            Assert.Equal(3 * Content.EquipmentCost("sprinkler"), Auto.S.Invested, "counts as invested");
            Game.Ok("/place sprinkler B2");
            Assert.Equal(2, Auto.S.BoxSprinklers, "took one");
            foreach (var c in new[] { "A2", "C2", "B1", "B3" }) Assert.True(Auto.Wet(c), "Lv1 waters " + c);
            Assert.True(!Auto.Wet("A1"), "Lv1 skips corners");

            Game.Ok("/upgrade sprinkler B2");
            Assert.Equal(2, Game.Plot(0, "B2").Sprinkler, "Lv2");
            Assert.True(Auto.Wet("A1") && Auto.Wet("C3"), "Lv2 waters corners at once");
            Game.Ok("/upgrade sprinkler B2");
            Assert.True(Auto.Wet("A4") && !Auto.Wet("A5"), "Lv3 reaches two plots");
            Game.Err("/upgrade sprinkler B2");
            Game.Err("/upgrade sprinkler B5");
        }

        public static void TestPlaceAndRemove()
        {
            Game.New();
            Game.State.Gold = 10000;
            Game.Err("/place sprinkler A1");
            Game.Ok("/buy sprinkler");
            Game.Ok("/buy seed turnip 2");
            Game.Ok("/plant turnip A1");
            Game.Err("/place sprinkler A1");
            Game.Err("/place sprinkler A7");
            Game.Err("/place sprinkler A2:A3");
            Game.Err("/place harvester A2");
            Game.Ok("/place sprinkler A2");
            Assert.True(Auto.Wet("A1") && Auto.Wet("A3"), "waters neighbours");
            Assert.True(!Engine.IsWet(Game.Plot(0, "B2"), Game.State.GameMinute), "locked plots get nothing");
            Game.Err("/plant turnip A2");
            Assert.Equal(1, GameState.Count(Game.State.Seeds, "turnip"), "seed not used");
            Game.Ok("/remove A2");
            Assert.Equal(0, Game.Plot(0, "A2").Sprinkler, "removed");
            Assert.Equal(1, Game.State.BoxSprinklers, "back in the box");
        }

        public static void TestDailyWater()
        {
            Game.New();
            Game.State.Gold = 10000;
            Game.Ok("/buy sprinkler");
            Game.Ok("/place sprinkler A2");
            Game.Ok("/buy seed turnip 2");
            Game.Ok("/plant turnip A1");
            Game.Ok("/plant turnip A3");
            var rep = Engine.Advance(Game.State, 3 * 1440);
            Assert.Equal(Engine.StageRipe, Engine.Stage(Game.Plot(0, "A1")), "A1 grew without manual water");
            Assert.Equal(Engine.StageRipe, Engine.Stage(Game.Plot(0, "A3")), "A3 too");
            Assert.Equal(0, rep.DriedTotal(), "the 06:00 watering is not a dry spell");
        }

        public static void TestBoxKeepsLevel()
        {
            Auto.Setup();
            Game.Ok("/buy sprinkler");
            Game.Ok("/place sprinkler B2");
            Game.Ok("/upgrade sprinkler B2");
            Game.Ok("/remove B2");
            Assert.Equal(1, GameState.Count(Auto.S.EquipmentBox, GameState.BoxKey(2)), "Lv2 in the box");
            Game.Ok("/buy sprinkler");
            Game.Ok("/place sprinkler B5");
            Assert.Equal(2, Game.Plot(0, "B5").Sprinkler, "places the best one first");
        }
    }

    public static class RuleParseTests
    {
        static Rule Ok(string text)
        {
            Msg e;
            var r = Rules.Compile(text, out e);
            Assert.True(r != null, "should parse: " + text + (e == null ? "" : " (" + e.Key + ")"));
            return r;
        }

        static Msg Bad(string text)
        {
            Msg e;
            var r = Rules.Compile(text, out e);
            Assert.True(r == null && e != null, "should fail: " + text);
            return e;
        }

        public static void TestValid()
        {
            var r = Ok("WHEN  f1 Ready do HARVEST");
            Assert.Equal("when f1 ready do harvest", r.Text, "normalized");
            Assert.Equal(1, r.ActField, "field from the condition");
            Assert.Equal("harvest f1", r.ActionText, "title");

            r = Ok("when price turnip>=35 do sell turnip");
            Assert.Equal("when price turnip >= 35 do sell turnip", r.Text, "operator split");
            Assert.Equal(CondKind.Price, r.Cond, "price");

            r = Ok("when stock all >= 40 do sell all");
            Assert.Equal(null, r.CondCrop, "stock all");
            Assert.Equal(null, r.ActCrop, "sell all");

            r = Ok("when seed turnip < 10 do buy seed turnip 50");
            Assert.Equal(50, r.ActQty, "buy qty");

            r = Ok("when all empty do plant turnip");
            Assert.Equal(0, r.ActField, "all fields");
            Assert.Equal("plant turnip all", r.ActionText, "title");

            r = Ok("when f1 ready do harvest f2");
            Assert.Equal(2, r.ActField, "explicit field");

            r = Ok("when stock corn = 5 do sell corn 3");
            Assert.Equal(3, r.ActQty, "sell qty");
        }

        public static void TestErrors()
        {
            var e = Bad("when f1 redy do harvest");
            Assert.Equal("rule.err.at", e.Key, "position error");
            Assert.Equal("redy", (string)e.Args[0], "points at the word");
            Assert.Equal("rule.err.end", Bad("when f1 ready").Key, "ends early");
            Assert.Equal("rule.err.crop", Bad("when price turnp >= 3 do sell turnip").Key, "unknown crop");
            Assert.Equal("rule.err.field", Bad("when price turnip >= 35 do harvest").Key, "harvest needs a field");
            Assert.Equal("rule.err.at", Bad("when f1 ready do harvest now").Key, "bad field");
            Assert.Equal("rule.err.extra", Bad("when f1 ready do sell all 10").Key, "no qty after sell all");
            Assert.Equal("rule.err.at", Bad("if f1 ready do harvest").Key, "must start with when");
            Assert.Equal("rule.err.end", Bad("when seed turnip < 10 do buy seed turnip").Key, "buy needs qty");
            Assert.Equal("rule.err.at", Bad("when f0 ready do harvest").Key, "f0");
            Assert.Equal("rule.err.at", Bad("when price turnip => 3 do sell turnip").Key, "bad operator");
        }
    }

    public static class RuleRunTests
    {
        public static void TestNeedsControllerAndEquipment()
        {
            Auto.Setup();
            Auto.Rule("when f1 ready do harvest");
            Auto.Ripe("A1", "turnip");
            Engine.Advance(Auto.S, 120);
            Assert.Equal("turnip", Game.Plot(0, "A1").Crop, "no controller, no run");

            Game.Ok("/buy controller");
            Game.Err("/buy controller");
            var rep = Engine.Advance(Auto.S, 60);
            Assert.Equal(1, rep.Runs.Count, "reports the missing harvester");
            Assert.Equal("needs", rep.Runs[0].Fail, "needs");
            rep = Engine.Advance(Auto.S, 60);
            Assert.Equal(0, rep.Runs.Count, "same reason only once");

            Game.Ok("/buy harvester");
            Game.Err("/buy harvester f1");
            rep = Engine.Advance(Auto.S, 60);
            Assert.Equal(1, rep.Runs.Count, "runs");
            Assert.Equal(null, rep.Runs[0].Fail, "success");
            Assert.Equal(1, GameState.Count(Auto.S.Storage, "turnip"), "harvested");
            Assert.Equal(1, Auto.S.Rules[0].Runs, "counted");
            Assert.Equal(null, Auto.S.Rules[0].Fail, "reason cleared");
        }

        public static void TestNoReadyNoiseWithHarvester()
        {
            Auto.Setup();
            var p = Game.Plot(0, "A1");
            p.Crop = "turnip";
            p.Growth = 2880 - 1;
            p.WaterUntil = 1440;
            Auto.S.GameMinute = 5;
            var rep = Engine.Advance(Auto.S, 1);
            Assert.Equal(1, Engine.ReportLogs(Auto.S, rep).Count, "Ready event by hand");

            p.Growth = 2880 - 1;
            Auto.S.Fields[0].Harvester = true;
            Auto.S.Farm.Controller = 1;
            Auto.Rule("when f1 ready do harvest");
            rep = Engine.Advance(Auto.S, 1);
            Assert.Equal(0, Engine.ReportLogs(Auto.S, rep).Count, "the harvester will take it: no Ready event");
        }

        public static void TestSlotsAndOrder()
        {
            Auto.Setup();
            Auto.S.Farm.Controller = 1;
            Auto.S.Farm.ShippingBin = true;
            Auto.S.Fields[0].Harvester = true;
            Auto.Rule("when f1 ready do harvest");
            Auto.Rule("when stock turnip >= 1 do sell turnip");
            Auto.Rule("when f1 empty do plant turnip");
            Assert.Equal(2, Rules.Active(Auto.S).Count, "Lv1 has two slots");
            Assert.True(!Rules.IsActive(Auto.S, 2), "third waits");

            Auto.Ripe("A1", "turnip");
            double gold0 = Auto.S.Gold;
            Engine.Advance(Auto.S, 60);
            Assert.Equal(0, Auto.S.StorageUsed, "harvested and sold in the same check");
            Assert.True(Auto.S.Gold > gold0, "earned");

            Game.Ok("/auto move 3 1");
            Assert.Equal("when f1 empty do plant turnip", Auto.S.Rules[0].Text, "moved to the top");
            Assert.True(Rules.IsActive(Auto.S, 0) && !Rules.IsActive(Auto.S, 2), "the sell rule waits now");
            Game.Ok("/auto off 1");
            Assert.True(Rules.IsActive(Auto.S, 2), "off frees the slot");
            Game.Ok("/auto del 1");
            Assert.Equal(2, Auto.S.Rules.Count, "deleted");
            Game.Err("/auto del 5");
            Game.Err("/auto on x");
            Game.Err("/auto add when f1 ready do harvest");
            Game.Err("/auto add when f1 redy do harvest");
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/auto list")), "list");
        }

        public static void TestFailOnceThenRecover()
        {
            Auto.Setup();
            Auto.S.Farm.Controller = 3;
            Auto.S.Fields[0].Planter = true;
            Auto.Rule("when f1 empty do plant turnip");
            var rep = Engine.Advance(Auto.S, 10);
            Assert.Equal("no_seed", rep.Runs[0].Fail, "no seeds");
            rep = Engine.Advance(Auto.S, 10);
            Assert.Equal(0, rep.Runs.Count, "quiet while the reason is the same");
            Auto.S.Seeds["turnip"] = 5;
            rep = Engine.Advance(Auto.S, 10);
            Assert.Equal(5, rep.Runs[0].Counts["turnip"], "planted what it had");
            Assert.Equal(null, Auto.S.Rules[0].Fail, "recovered");
        }

        public static void TestBuyAndSellLimits()
        {
            Auto.Setup();
            Auto.S.Farm.Controller = 3;
            Auto.S.Farm.AutoBuyer = true;
            Auto.S.Farm.ShippingBin = true;
            Auto.S.Gold = 100;
            Auto.Rule("when seed turnip < 5 do buy seed turnip 20");
            var rep = Engine.Advance(Auto.S, 10);
            Assert.Equal("no_gold", rep.Runs[0].Fail, "200G needed");
            Assert.Equal(100.0, Auto.S.Gold, "bought nothing");
            Auto.S.Gold = 1000;
            Engine.Advance(Auto.S, 10);
            Assert.Equal(20, GameState.Count(Auto.S.Seeds, "turnip"), "bought exactly 20");
            Assert.Equal(800.0, Auto.S.Gold, "paid 200");

            Auto.S.Storage["turnip"] = 3;
            Auto.Rule("when stock turnip >= 1 do sell turnip 10");
            Engine.Advance(Auto.S, 10);
            Assert.Equal(0, GameState.Count(Auto.S.Storage, "turnip"), "sold what it had (3 of 10)");
        }

        public static void TestGreenhouse()
        {
            Auto.Setup();
            Game.Ok("/buy seed corn 2");
            Game.Err("/plant corn A1");
            Game.Ok("/buy greenhouse");
            Game.Ok("/plant corn A1");

            Game.Ok("/unlock strawberry");
            Auto.S.Tools["sickle"] = 3;
            Engine.Advance(Auto.S, 7 * 1440);
            Assert.Equal(1, GameClock.Season(Auto.S.GameMinute), "summer");
            Auto.Ripe("A2", "strawberry");
            Game.Ok("/harvest A2");
            Assert.Equal("strawberry", Game.Plot(0, "A2").Crop, "regrows off-season in a greenhouse");
        }

        public static void TestCaretakerAndController()
        {
            Auto.Setup();
            Assert.Equal(2.0, Engine.OfflineCapHours(Auto.S), "base 2h");
            Game.Ok("/upgrade caretaker");
            Assert.Equal(4.0, Engine.OfflineCapHours(Auto.S), "Lv1 4h");
            Game.Err("/buy caretaker");
            Auto.S.Farm.Caretaker = Content.MaxCaretakerLevel;
            Assert.Equal(64.0, Engine.OfflineCapHours(Auto.S), "Lv4 64h");
            Game.Err("/upgrade caretaker");

            Game.Ok("/upgrade controller");
            Assert.Equal(1, Auto.S.Farm.Controller, "Lv1 from upgrade");
            Assert.Equal(2, Rules.Slots(Auto.S), "2 slots");
            Assert.Equal(60, Content.ControllerInterval(1), "Lv1 every hour");
            Assert.Equal(10, Content.ControllerInterval(3), "Lv3 every 10 min");
            Assert.Equal(200000.0, Content.ControllerCost(3), "Lv4 = 40,000 x 5");
        }

        public static void TestFullAutoOffline()
        {
            Auto.Setup();
            Game.Ok("/buy sprinkler 2");
            foreach (var cell in new[] { "B3", "B8" })
            {
                Game.Ok("/place sprinkler " + cell);
                Game.Ok("/upgrade sprinkler " + cell);
                Game.Ok("/upgrade sprinkler " + cell);
            }
            foreach (var eq in new[] { "harvester", "planter", "shipping-bin", "auto-buyer", "controller" }) Game.Ok("/buy " + eq);
            Game.Ok("/upgrade controller");
            Game.Ok("/upgrade controller");
            double gear = 2 * (Content.EquipmentCost("sprinkler") + Content.SprinklerUpgradeCost(1) + Content.SprinklerUpgradeCost(2));
            foreach (var eq in new[] { "harvester", "planter", "shipping-bin", "auto-buyer" }) gear += Content.EquipmentCost(eq);
            gear += Content.ControllerCost(0) + Content.ControllerCost(1) + Content.ControllerCost(2);
            Assert.Equal(gear, Auto.S.Invested, "gear counts as invested");
            Auto.Rule("when f1 ready do harvest");
            Auto.Rule("when f1 empty do plant turnip");
            Auto.Rule("when stock all >= 1 do sell all");
            Auto.Rule("when seed turnip < 28 do buy seed turnip 30");

            double gold0 = Auto.S.Gold;
            var rep = Engine.Advance(Auto.S, 5 * 1440);
            var harvested = rep.AutoCounts(ActKind.Harvest);
            Assert.Equal(56, GameState.Count(harvested, "turnip"), "two full rounds of 28 plots");
            Assert.Equal(0, rep.DriedTotal(), "sprinklers cover the field");
            Assert.True(Auto.S.Gold > gold0, "profit");
            RuleRun harvest = null;
            foreach (var run in rep.Runs) if (run.Rule.Act == ActKind.Harvest && run.Fail == null) harvest = run;
            Assert.Equal(2, harvest.Times, "harvest runs merged");
            Assert.Equal(56, harvest.Counts["turnip"], "merged counts");

            bool ready = false;
            foreach (var e in Engine.ReportLogs(Auto.S, rep)) if (e.Title.Key == "t.ready") ready = true;
            Assert.True(!ready, "no Ready event for crops the harvester took");

            var keys = new List<string>();
            foreach (var o in Engine.OfflineLines(Auto.S, 7200, 7200, 2, rep)) keys.Add(((Msg)o).Key);
            Assert.True(keys.Contains("welcome.auto_harvest") && keys.Contains("welcome.auto_sell") && keys.Contains("welcome.auto_buy"), "offline summary");
        }

        static GameState FullAutoFarm()
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
            s.Unlocked.Add("strawberry");
            foreach (var t in new[] { "when f1 ready do harvest", "when f1 empty do plant strawberry", "when price strawberry >= 50 do sell strawberry 7",
                "when seed strawberry < 3 do buy seed strawberry 4", "when stock all >= 30 do sell all" })
            {
                var r = new RuleState();
                r.Text = t;
                r.On = true;
                s.Rules.Add(r);
            }
            return s;
        }

        /// <summary>바뀐 것이 없을 때 검사를 건너뛰어도(M3 이슈) 잘게 나눠 진행한 것과 결과가 같아야 한다.</summary>
        public static void TestBigStepEqualsSmallSteps()
        {
            var a = FullAutoFarm();
            var b = FullAutoFarm();
            Engine.Advance(a, 12 * 1440);
            for (int i = 0; i < 12 * 144; i++) Engine.Advance(b, 10);
            Assert.Equal(b.GameMinute, a.GameMinute, "clock");
            Assert.Equal(b.Gold, a.Gold, "gold");
            Assert.Equal(b.StorageUsed, a.StorageUsed, "storage");
            Assert.Equal(GameState.Count(b.Seeds, "strawberry"), GameState.Count(a.Seeds, "strawberry"), "seeds");
            for (int i = 0; i < a.Rules.Count; i++) Assert.Equal(b.Rules[i].Runs, a.Rules[i].Runs, "runs of #" + (i + 1));
            for (int i = 0; i < 30; i++) Assert.Equal(b.Fields[0].Plots[i].Growth, a.Fields[0].Plots[i].Growth, "growth " + i);
            Assert.True(a.Rules[0].Runs > 2, "harvested several times (regrows, then summer)");
        }

        public static void TestShopAndInfoCommands()
        {
            Auto.Setup();
            Auto.S.Farm.Controller = 1;
            Auto.Rule("when f1 ready do harvest");
            foreach (var cmd in new[] { "/shop", "/inv", "/map", "/status", "/auto", "/help auto", "/help place" })
                Assert.Equal(LogKind.Info, Game.Kind(Game.Run(cmd)), cmd);
            Game.Err("/buy sprinkler 0");
            Game.Err("/buy harvester f9");
            Game.Err("/upgrade harvester");
        }
    }

    public static class GearSaveTests
    {
        public static void TestRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(dir);
                Auto.Setup();
                Game.Ok("/buy sprinkler 2");
                Game.Ok("/place sprinkler B2");
                Game.Ok("/buy harvester");
                Game.Ok("/buy greenhouse");
                Game.Ok("/buy shipping-bin");
                Game.Ok("/buy controller");
                Game.Ok("/upgrade caretaker");
                Auto.Rule("when f1 ready do harvest");
                Game.Ok("/auto off 1");
                store.Save(Auto.S);

                bool bak;
                var s = store.LoadSave(out bak);
                Assert.Equal(1, s.Fields[0].Plots[11].Sprinkler, "placed sprinkler");
                Assert.Equal(1, s.BoxSprinklers, "box");
                Assert.True(s.Fields[0].Harvester && s.Fields[0].Greenhouse && !s.Fields[0].Planter, "field gear");
                Assert.True(s.Farm.ShippingBin && !s.Farm.AutoBuyer, "farm gear");
                Assert.Equal(1, s.Farm.Controller, "controller");
                Assert.Equal(1, s.Farm.Caretaker, "caretaker");
                Assert.Equal(1, s.Rules.Count, "rules");
                Assert.Equal("when f1 ready do harvest", s.Rules[0].Text, "rule text");
                Assert.True(!s.Rules[0].On, "rule off");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        public static void TestOldSaveLoads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "save.json"),
                    "{\"Version\":1,\"Run\":1,\"GameMinute\":100,\"Gold\":50,\"Fields\":[{\"Open\":6,\"Plots\":[]}]}");
                bool bak;
                var s = new SaveStore(dir).LoadSave(out bak);
                Assert.True(s != null && s.Farm != null && s.Rules != null && s.EquipmentBox != null, "M2 save gets M3 defaults");
                Assert.Equal(0, s.Farm.Controller, "no controller");
                Assert.Equal(2.0, Engine.OfflineCapHours(s), "base cap");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
