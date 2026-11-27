using System;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    /// <summary>판매 견적: 합계, 판 뒤 계수, 판매 전후 단가.</summary>
    public struct SaleQuote
    {
        public double Total;
        public double FactorAfter;
        public double UnitBefore;
        public double UnitAfter;
    }

    /// <summary>
    /// 시세 (02 3.6절, M2 3.2절). 단가 = 기준가 × 계수 × (제철 1 / 아니면 1.3).
    /// 판매 압력은 1개마다 계수 × 0.998, 하한 0.5. 합계는 등비수열 합으로 계산한다 (M2 K1, K2).
    /// </summary>
    public static class Market
    {
        public static double SeasonMultiplier(GameState s, CropDef crop)
        {
            return Content.InSeason(crop, GameClock.Season(s.GameMinute)) ? 1.0 : Content.OffSeasonMultiplier;
        }

        /// <summary>영구 보너스 Market Sense: 판매 단가 +3%/레벨 (M4 K13).</summary>
        public static double Bonus(GameState s)
        {
            return 1 + Content.MarketSensePerLevel * s.PerkLevel("market-sense");
        }

        static double Mult(GameState s, CropDef crop)
        {
            return SeasonMultiplier(s, crop) * Bonus(s);
        }

        static double Round(double v)
        {
            return Math.Max(1, Math.Round(v, MidpointRounding.AwayFromZero));
        }

        /// <summary>지금 화면에 보이는 단가 (반올림).</summary>
        public static double Price(GameState s, string crop)
        {
            var d = Content.Crop(crop);
            if (d == null) return 0;
            return Round(d.BasePrice * s.MarketOf(d.Id).F * Mult(s, d));
        }

        /// <summary>어제 계수로 만든 단가 (같은 계절 배수, M2 K12).</summary>
        public static double YesterdayPrice(GameState s, string crop)
        {
            var d = Content.Crop(crop);
            if (d == null) return 0;
            return Round(d.BasePrice * s.MarketOf(d.Id).Yesterday * Mult(s, d));
        }

        /// <summary>팔면 얼마인지 계산만 한다 (상태를 바꾸지 않음).</summary>
        public static SaleQuote Quote(GameState s, string crop, long qty)
        {
            var q = new SaleQuote();
            var d = Content.Crop(crop);
            if (d == null || qty <= 0) return q;
            double unit = d.BasePrice * Mult(s, d);
            double f0 = s.MarketOf(d.Id).F;
            double r = Content.SalePressure;
            double floor = Content.MarketMin;

            // 계수가 하한보다 큰 동안 팔리는 개수: f0 × r^i >= floor 인 i = 0..k-1
            long k = 0;
            if (f0 > floor) k = (long)Math.Floor(Math.Log(floor / f0) / Math.Log(r)) + 1;
            long m = Math.Min(qty, k);
            double sum = f0 * (1 - Math.Pow(r, m)) / (1 - r) + (qty - m) * Math.Min(f0, floor);

            q.Total = Math.Round(unit * sum, MidpointRounding.AwayFromZero);
            q.FactorAfter = Math.Max(floor, Math.Min(f0, f0 * Math.Pow(r, qty)));
            q.UnitBefore = Round(unit * f0);
            q.UnitAfter = Round(unit * q.FactorAfter);
            return q;
        }

        /// <summary>판다: 계수를 내리고 합계를 돌려준다. 창고와 골드는 부르는 쪽이 바꾼다.</summary>
        public static SaleQuote Sell(GameState s, string crop, long qty)
        {
            var q = Quote(s, crop, qty);
            var d = Content.Crop(crop);
            if (d != null && qty > 0) s.MarketOf(d.Id).F = q.FactorAfter;
            return q;
        }

        /// <summary>하루 시작(06:00) 시세 갱신. 작물 순서는 Content.Crops로 고정한다.</summary>
        public static void DailyUpdate(GameState s)
        {
            long rng = s.Rng;
            foreach (var c in Content.Crops)
            {
                var e = s.MarketOf(c.Id);
                e.Yesterday = e.F;
                double f = e.F + Content.MarketReversion * (1 - e.F) + Content.MarketVolatility * Rng.Normal(ref rng);
                e.F = Math.Max(Content.MarketMin, Math.Min(Content.MarketMax, f));
            }
            s.Rng = rng;
        }
    }
}
