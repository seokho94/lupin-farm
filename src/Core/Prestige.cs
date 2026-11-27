using System;
using System.Collections.Generic;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    /// <summary>프레스티지 Lupin Bloom과 영구 보너스 (FR-PRS-01~07, M4 K8~K13).</summary>
    public static class Prestige
    {
        /// <summary>얻을 Fertility = floor(sqrt(이번 판 최고 총자산 / 100,000)).</summary>
        public static long Gain(GameState s)
        {
            Records.Observe(s);
            return Content.FertilityGain(s.PeakNetWorth);
        }

        /// <summary>
        /// 판을 마감하고 새 판을 만든다 (K9). 판 기록에 끝 날짜·Fertility를 남기고, 규칙 문장과 켜짐/꺼짐을 새 판에 옮긴다 (K10).
        /// 이벤트 로그는 logs에 (업적, 도감, 마지막에 Lupin Bloom).
        /// </summary>
        public static GameState Bloom(GameState s, Profile p, string today, long seed, List<LogEntry> logs)
        {
            long gain = Gain(s);
            logs.AddRange(Records.Sync(s, p, today));
            var done = Records.Current(s, p, today);
            done.EndedAt = today;
            done.Fertility = gain;
            p.Fertility += gain;

            var n = GameState.NewRun(s.Run + 1, seed, p.Perks);
            foreach (var r in s.Rules)
            {
                var c = new RuleState();
                c.Text = r.Text;
                c.On = r.On;
                n.Rules.Add(c);
            }
            p.CurrentRun = n.Run;
            Records.Current(n, p, today);
            Records.Achieve(p, "first-bloom", today, logs);

            var lines = new List<object>();
            lines.Add(new Msg("bloom.gain", gain, p.Fertility));
            lines.Add(new Msg("bloom.start", n.Fields[0].Open, NumberFormat.Gold(n.Gold), n.ToolLevel("can")));
            if (n.Rules.Count > 0) lines.Add(new Msg("bloom.rules", n.Rules.Count));
            logs.Add(LogEntry.Make(LogKind.Event, new Msg("t.bloom", n.Run), lines.ToArray()));
            return n;
        }

        /// <summary>보너스 레벨 lv의 효과 문구 (지금 값): "growth +10%".</summary>
        public static Msg Effect(string id, int lv)
        {
            return new Msg("perk.eff." + id, EffectValue(id, lv));
        }

        /// <summary>효과 값만: "+10%". "지금 → 다음"의 다음 쪽에 쓴다.</summary>
        public static Msg Value(string id, int lv)
        {
            return new Msg("perk.val." + id, EffectValue(id, lv));
        }

        /// <summary>"growth +10% → +15%".</summary>
        public static Msg Change(string id, int lv)
        {
            return new Msg("perks.arrow", Effect(id, lv), Value(id, lv + 1));
        }

        static object EffectValue(string id, int lv)
        {
            switch (id)
            {
                case "rich-soil": return Math.Round(lv * Content.RichSoilPerLevel * 100);
                case "market-sense": return Math.Round(lv * Content.MarketSensePerLevel * 100);
                case "night-shift": return lv * Content.NightShiftHours;
                case "head-start": return NumberFormat.Gold(lv * Content.HeadStartGold);
                case "old-fields": return lv * Content.OldFieldsPlots;
                case "seasoned-hands": return Math.Min(Content.MaxToolLevel, 1 + lv);
            }
            return lv;
        }

        public static bool AtMax(string id, int lv)
        {
            int max = Content.PerkMax(id);
            return max > 0 && lv >= max;
        }

        /// <summary>보너스를 한 레벨 산다. 성공하면 게임 상태의 사본도 맞춘다 (바로 붙는 효과, K12).</summary>
        public static bool Buy(GameState s, Profile p, string id, out long cost)
        {
            int lv = p.PerkLevel(id);
            cost = Content.PerkCost(lv);
            if (AtMax(id, lv) || p.Fertility < cost) return false;
            p.Fertility -= cost;
            p.Perks[id] = lv + 1;
            s.Perks = new Dictionary<string, int>(p.Perks);
            return true;
        }
    }
}
