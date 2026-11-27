using System;
using LupinFarm.Core;
using LupinFarm.Data;

namespace LupinFarm.Tests
{
    public static class MarketTests
    {
        public static void TestStartsAtBase()
        {
            var s = GameState.NewRun(1, 7);
            Assert.Equal(30.0, Market.Price(s, "turnip"), "turnip base in spring");
            Assert.Equal(195.0, Market.Price(s, "corn"), "corn is off-season in spring: 150 x 1.3");
        }

        public static void TestDailyUpdateIsReproducible()
        {
            var a = GameState.NewRun(1, 12345);
            var b = GameState.NewRun(1, 12345);
            Engine.Advance(a, 10 * 1440);
            for (int i = 0; i < 10; i++) Engine.Advance(b, 1440);
            foreach (var c in Content.Crops)
                Assert.Equal(a.MarketOf(c.Id).F, b.MarketOf(c.Id).F, "same seed, same prices: " + c.Id);
            Assert.True(Math.Abs(a.MarketOf("turnip").F - 1) > 1e-9, "prices moved");
        }

        public static void TestDailyUpdateStaysInRange()
        {
            var s = GameState.NewRun(1, 99);
            for (int d = 0; d < 500; d++)
            {
                Market.DailyUpdate(s);
                foreach (var c in Content.Crops)
                {
                    double f = s.MarketOf(c.Id).F;
                    Assert.True(f >= Content.MarketMin && f <= Content.MarketMax, "factor in range");
                }
            }
        }

        public static void TestSalePressure()
        {
            var s = GameState.NewRun(1, 1);
            var q = Market.Sell(s, "turnip", 100);
            Assert.True(Math.Abs(s.MarketOf("turnip").F - Math.Pow(0.998, 100)) < 1e-9, "factor x 0.998^100");
            double loop = 0;
            for (int i = 0; i < 100; i++) loop += 30 * Math.Pow(0.998, i);
            Assert.Equal(Math.Round(loop), q.Total, "closed form = per-unit sum");
            Assert.Equal(30.0, q.UnitBefore, "unit before");
            Assert.Equal(25.0, q.UnitAfter, "unit after (30 x 0.819)");
        }

        public static void TestPriceFloor()
        {
            var s = GameState.NewRun(1, 1);
            var q = Market.Sell(s, "turnip", 100000);
            Assert.Equal(Content.MarketMin, s.MarketOf("turnip").F, "stops at 0.5");
            Assert.True(q.Total > 100000 * 30 * 0.5 && q.Total < 100000 * 30 * 0.51, "most units at the floor");
            q = Market.Quote(s, "turnip", 10);
            Assert.Equal(150.0, q.Total, "at the floor: 10 x 15");
        }
    }

    public static class SeasonTests
    {
        public static void TestPlantOnlyInSeason()
        {
            Game.New();
            Game.State.Gold = 1000;
            Game.Ok("/buy seed corn 2");
            Game.Err("/plant corn A1");
            Engine.Advance(Game.State, 7 * 1440);
            Assert.Equal(1, GameClock.Season(Game.State.GameMinute), "summer");
            Game.Ok("/plant corn A1");
            Game.Ok("/buy seed turnip 1");
            Game.Err("/plant turnip A2");
        }

        public static void TestPlantedCropsKeepGrowing()
        {
            Game.New();
            Game.Ok("/buy seed turnip 1");
            Engine.Advance(Game.State, 6 * 1440);
            Game.Ok("/plant turnip A1");
            var p = Game.Plot(0, "A1");
            p.WaterUntil = Game.State.GameMinute + 3 * 1440;
            Engine.Advance(Game.State, 3 * 1440);
            Assert.Equal(Engine.StageRipe, Engine.Stage(p), "grew through the season change");
        }

        public static void TestRegrowOnlyInSeason()
        {
            Game.New();
            Game.State.Gold = 10000;
            Game.Ok("/unlock strawberry");
            Game.State.Tools["sickle"] = 3;
            var a1 = Game.Plot(0, "A1");
            a1.Crop = "strawberry";
            a1.Growth = 6 * 1440;
            Game.Ok("/harvest A1");
            Assert.Equal("strawberry", a1.Crop, "regrows in spring");
            Assert.Equal(3 * 1440, a1.Growth, "back to 6 - 3 days");

            Engine.Advance(Game.State, 7 * 1440);
            a1.Growth = 6 * 1440;
            Game.Ok("/harvest A1");
            Assert.Equal(null, a1.Crop, "summer: plot is cleared");
        }

        public static void TestSeasonEvent()
        {
            var s = GameState.NewRun(1, 5);
            var rep = Engine.Advance(s, 6 * 1440);
            Assert.Equal(0, rep.SeasonStarts.Count, "still spring");
            Assert.Equal(6, rep.Days, "six mornings");
            rep = Engine.Advance(s, 1440);
            Assert.Equal(1, rep.SeasonStarts.Count, "summer starts");
            Assert.Equal(1, rep.SeasonStarts[0], "summer index");
            bool found = false;
            foreach (var e in Engine.ReportLogs(s, rep)) if (e.Title.Key == "t.season") found = true;
            Assert.True(found, "season event log");
        }
    }

    public static class ShopTests
    {
        public static void TestUnlock()
        {
            Game.New();
            Game.Err("/unlock potato");
            Game.State.Gold = 500;
            Game.Ok("/unlock potato");
            Assert.True(Game.State.IsUnlocked("potato"), "unlocked");
            Assert.Equal(300.0, Game.State.Gold, "costs 200");
            Assert.Equal(200.0, Game.State.Invested, "counts as invested");
            Game.Ok("/buy seed potato 1");
            Game.Ok("/plant potato A1");
            Game.Err("/unlock potato");
            Game.Err("/unlock banana");
        }

        public static void TestStorageUpgrade()
        {
            Game.New();
            Game.State.Gold = 2000;
            Game.Ok("/upgrade storage");
            Assert.Equal(80, Game.State.StorageCapacity, "doubles");
            Assert.Equal(1600.0, Game.State.Gold, "Lv0 -> 1 costs 400");
            Game.Ok("/upgrade storage");
            Assert.Equal(160, Game.State.StorageCapacity, "Lv2");
            Assert.Equal(400.0, Game.State.Gold, "Lv1 -> 2 costs 1,200");
            Game.State.StorageLevel = Content.MaxStorageLevel;
            Game.State.Gold = 1e12;
            Game.Err("/upgrade storage");
        }

        public static void TestTables()
        {
            Game.New();
            Game.State.Storage["turnip"] = 3;
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/market")), "/market");
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/inv")), "/inv");
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/map")), "/map");
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/shop")), "/shop");
            var inv = Game.Run("/inv").Logs[0];
            Assert.Equal(4, inv.Lines.Count, "item, total, seeds, gear");
        }

        public static void TestSellLowersPrice()
        {
            Game.New();
            Game.State.Storage["turnip"] = 30;
            Game.Ok("/sell turnip 30");
            Assert.True(Market.Price(Game.State, "turnip") < 30, "price dropped");
            Assert.True(Game.State.Gold > 100 + 30 * 28 && Game.State.Gold < 100 + 30 * 30, "a bit under 900G");
        }

        public static void TestSaveKeepsMarket()
        {
            Game.New();
            Engine.Advance(Game.State, 3 * 1440);
            var json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Game.State);
            var back = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<GameState>(json);
            back.Normalize();
            Assert.Equal(Game.State.MarketOf("corn").F, back.MarketOf("corn").F, "market survives save");
            Assert.Equal(Game.State.Rng, back.Rng, "rng survives save");
        }
    }
}
