using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LupinFarm.Core;
using LupinFarm.Data;

namespace LupinFarm.Tests
{
    /// <summary>
    /// 밸런스 시뮬레이션 (M5 K12). 정해진 간격(게임 분)마다 들여다보는 플레이어가 명령(`Commands.Execute`)만으로
    /// 수확·판매·심기·물 주기·구매·자동화를 한다. 한 번 들여다볼 때 칠 수 있는 명령 수(Budget)를 둬서 농기구
    /// 레벨이 손을 덜어 주는 효과가 드러나게 한다. 결과는 단계별로 처음 닿은 게임 날짜다.
    /// </summary>
    public sealed class BalanceSim
    {
        public const int Budget = 15;

        /// <summary>칸을 사는 회수 기간 한도(게임 날짜). 투자액은 총자산에 그대로 들어가므로 기록을 노리는 플레이어는 넉넉히 산다.</summary>
        public static int PaybackDays = 90;
        public static readonly string[] Steps = { "tool2", "sprinkler", "auto", "field2", "nw100k", "nw1m" };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly int[] SprinklerCells = { 11, 14, 17 }; // B2, B5, B8: Lv1·Lv2 범위가 겹치지 않게

        readonly GameState s;
        readonly Profile p;
        readonly CommandContext ctx;
        readonly int interval;
        int used;
        public readonly Dictionary<string, int> Reached = new Dictionary<string, int>();
        public int Commands;

        public BalanceSim(int intervalMinutes, long seed)
        {
            interval = intervalMinutes;
            p = Profile.New();
            p.Settings.Confirm = false;
            s = GameState.NewRun(1, seed);
            ctx = new CommandContext();
            ctx.State = s;
            ctx.Profile = p;
        }

        public GameState State
        {
            get { return s; }
        }

        /// <summary>maxDays까지 진행한다. 모든 단계에 닿으면 일찍 끝낸다.</summary>
        public void Run(int maxDays)
        {
            Run(maxDays, null);
        }

        /// <summary>trace가 있으면 10일마다 골드·총자산·칸·구역·작물을 한 줄씩 남긴다.</summary>
        public void Run(int maxDays, StringBuilder trace)
        {
            int nextTrace = 1;
            while (GameClock.Day(s.GameMinute) <= maxDays && (trace != null || Reached.Count < Steps.Length))
            {
                used = 0;
                Check();
                Mark();
                if (trace != null && Day >= nextTrace)
                {
                    nextTrace = Day + 10;
                    int plots = 0;
                    foreach (var f in s.Fields) plots += f.Open;
                    var c = BestCrop(0, true);
                    trace.Append("day ").Append(Day.ToString(Inv).PadRight(4)).Append(" gold ").Append(NumberFormat.Num(s.Gold).PadRight(7))
                        .Append(" nw ").Append(NumberFormat.Num(Records.NetWorth(s)).PadRight(7)).Append(" plots ").Append(plots.ToString(Inv).PadRight(3))
                        .Append(" fields ").Append(s.Fields.Count.ToString(Inv)).Append(" earned ").Append(NumberFormat.Num(s.Earned).PadRight(7))
                        .Append(" tools ").Append(s.ToolLevel("bag")).Append(s.ToolLevel("sickle")).Append(s.ToolLevel("can"))
                        .Append(" ctl ").Append(s.Farm.Controller).Append(" crop ").Append(c != null ? c.Id : "-").Append('\n');
                }
                Engine.Advance(s, interval);
                Mark();
            }
        }

        int Day
        {
            get { return GameClock.Day(s.GameMinute); }
        }

        void Mark()
        {
            int d = Day;
            Hit("tool2", s.ToolLevel("bag") >= 2 || s.ToolLevel("can") >= 2 || s.ToolLevel("sickle") >= 2, d);
            bool spr = s.BoxSprinklers > 0;
            foreach (var f in s.Fields) if (f.SprinklerCount > 0) spr = true;
            Hit("sprinkler", spr, d);
            Hit("auto", s.Farm.Controller > 0 && s.Fields[0].Harvester, d);
            Hit("field2", s.Fields.Count >= 2, d);
            double nw = Records.NetWorth(s);
            Hit("nw100k", nw >= 1e5, d);
            Hit("nw1m", nw >= 1e6, d);
        }

        void Hit(string step, bool ok, int day)
        {
            if (ok && !Reached.ContainsKey(step)) Reached[step] = day;
        }

        bool Do(string line)
        {
            if (used >= Budget) return false;
            used++;
            Commands++;
            var r = LupinFarm.Core.Commands.Execute(ctx, line);
            if (r.Prompt != null) r = r.Prompt.OnSelect(0);
            foreach (var l in r.Logs) if (l.Kind == LogKind.Error) return false;
            return true;
        }

        static string F(int fi)
        {
            return "f" + (fi + 1).ToString(Inv) + ".";
        }

        // ------------------------------------------------------------------ 한 번 들여다볼 때

        void Check()
        {
            if (s.StorageUsed > 0) Do("/sell all");
            for (int fi = 0; fi < s.Fields.Count; fi++)
            {
                if (Automated(fi)) continue;
                int guard = 0;
                while (Farming.HasRipe(s.Fields[fi]) && guard++ < 40)
                {
                    if (s.StorageUsed >= s.StorageCapacity && !Do("/sell all")) break;
                    if (!Do("/harvest " + F(fi) + "ready")) break;
                }
            }
            if (s.StorageUsed > 0) Do("/sell all");
            Buy();
            for (int fi = 0; fi < s.Fields.Count; fi++) PlantField(fi);
            for (int fi = 0; fi < s.Fields.Count; fi++) WaterField(fi);
        }

        bool Automated(int fi)
        {
            return s.Fields[fi].Harvester && s.Farm.Controller > 0 && HasRule("when f" + (fi + 1).ToString(Inv) + " ready");
        }

        bool HasRule(string prefix)
        {
            foreach (var r in s.Rules) if (r.Text.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>하루당 이익이 가장 높은 심을 수 있는 작물 (재수확 작물은 남은 제철 동안의 수확 수로).</summary>
        CropDef BestCrop(int fi, bool unlockedOnly)
        {
            CropDef best = null;
            double bestScore = 0;
            foreach (var c in Content.Crops)
            {
                if (unlockedOnly && !s.IsUnlocked(c.Id)) continue;
                if (!Farming.SeasonOk(s, fi, c)) continue;
                double score = Score(c, fi);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        double Score(CropDef c, int fi)
        {
            double price = Market.Price(s, c.Id);
            if (!c.Regrows) return (price - c.SeedCost) / c.GrowDays;
            int span = s.Fields[fi].Greenhouse ? 28 : Math.Max(c.GrowDays, Content.SeasonDaysLeft(c, Day));
            int harvests = 1 + Math.Max(0, (span - c.GrowDays) / c.RegrowDays);
            return (price * harvests - c.SeedCost) / Math.Max(span, c.GrowDays);
        }

        void PlantField(int fi)
        {
            var f = s.Fields[fi];
            var empty = Farming.EmptyCells(f);
            if (empty.Count == 0) return;
            if (f.Planter && s.Farm.Controller > 0 && HasRule("when f" + (fi + 1).ToString(Inv) + " empty")) return;
            var crop = BestCrop(fi, true);
            if (crop == null) return;
            int have = GameState.Count(s.Seeds, crop.Id);
            int need = empty.Count - have;
            if (need > 0)
            {
                int afford = (int)Math.Floor(s.Gold / crop.SeedCost);
                int n = Math.Min(need, afford);
                if (n > 0) Do("/buy seed " + crop.Id + " " + n.ToString(Inv));
            }
            int bag = s.ToolLevel("bag");
            if (bag >= 3)
            {
                Do("/plant " + crop.Id + " " + F(fi) + "all");
                return;
            }
            var done = new HashSet<int>();
            foreach (int i in empty)
            {
                if (GameState.Count(s.Seeds, crop.Id) == 0) return;
                int key = bag >= 2 ? i / Content.Cols : i;
                if (!done.Add(key)) continue;
                string target = bag >= 2 ? ((char)('A' + key)).ToString() : RangeParser.CellName(i);
                if (!Do("/plant " + crop.Id + " " + F(fi) + target)) return;
            }
        }

        void WaterField(int fi)
        {
            var f = s.Fields[fi];
            bool[] cov = Farming.Covered(f);
            long until = s.GameMinute + interval;
            var dry = new List<int>();
            for (int i = 0; i < f.Open; i++)
            {
                var pl = f.Plots[i];
                if (pl.Crop == null || cov[i] || Engine.Stage(pl) == Engine.StageRipe) continue;
                if (pl.WaterUntil < until) dry.Add(i);
            }
            if (dry.Count == 0) return;
            int can = s.ToolLevel("can");
            if (can >= 3)
            {
                Do("/water " + F(fi) + "all");
                return;
            }
            var done = new HashSet<int>();
            foreach (int i in dry)
            {
                int key = can >= 2 ? i / Content.Cols : i;
                if (!done.Add(key)) continue;
                string target = can >= 2 ? ((char)('A' + key)).ToString() : RangeParser.CellName(i);
                if (!Do("/water " + F(fi) + target)) return;
            }
        }

        // ------------------------------------------------------------------ 투자

        /// <summary>한 번 다시 심을 씨앗값(지금 가장 나은 작물)은 남겨 둔다.</summary>
        double Spare
        {
            get
            {
                double seeds = 0;
                for (int fi = 0; fi < s.Fields.Count; fi++)
                {
                    var c = BestCrop(fi, true);
                    if (c != null) seeds += s.Fields[fi].Open * c.SeedCost;
                }
                return s.Gold - seeds;
            }
        }

        bool Afford(double cost)
        {
            return cost >= 0 && Spare >= cost;
        }

        void Buy()
        {
            foreach (var t in new[] { "bag", "sickle", "can" })
                if (s.ToolLevel(t) == 1 && Afford(Content.ToolUpgradeCost(1))) Do("/upgrade " + t);

            // 스프링클러: B2, B5, B8 순서로 (빈 칸일 때)
            for (int fi = 0; fi < s.Fields.Count; fi++)
            {
                var f = s.Fields[fi];
                foreach (int cell in SprinklerCells)
                {
                    if (cell >= f.Open || !Farming.IsEmpty(f.Plots[cell])) continue;
                    if (s.BoxSprinklers == 0)
                    {
                        if (!Afford(Content.EquipmentCost("sprinkler") * 1.5)) break;
                        Do("/buy sprinkler");
                    }
                    if (s.BoxSprinklers > 0) Do("/place sprinkler " + F(fi) + RangeParser.CellName(cell));
                }
            }

            // 작물 해금: 지금 계절의 더 나은 작물
            for (int fi = 0; fi < 1; fi++)
            {
                var have = BestCrop(fi, true);
                var any = BestCrop(fi, false);
                if (any != null && !s.IsUnlocked(any.Id) && (have == null || Score(any, fi) > Score(have, fi) * 1.2)
                    && Afford(any.UnlockCost * 1.5))
                    Do("/unlock " + any.Id);
            }

            // 자동화: 컨트롤러 + 자동 수확기 + 출하함 → 파종기 → 컨트롤러 Lv2
            if (s.Farm.Controller == 0 && Afford(Content.ControllerCost(0) + Content.EquipmentCost("harvester") + Content.EquipmentCost("shipping-bin")))
            {
                Do("/buy controller");
                Do("/buy harvester f1");
                Do("/buy shipping-bin");
                Do("/auto add when f1 ready do harvest f1");
                Do("/auto add when stock all >= 1 do sell all");
            }
            // 칸 늘리기: 회수 기간 30일 안이면. 첫 농기구 Lv2(씨앗 주머니)를 먼저 모은다
            bool full = true;
            for (int fi = 0; fi < s.Fields.Count && s.ToolLevel("bag") >= 2; fi++)
            {
                var f = s.Fields[fi];
                if (f.Open >= Content.PlotsPerField) continue;
                double cost = Content.PlotCost(fi + 1, f.Bought);
                var crop = BestCrop(fi, true);
                double perDay = crop != null ? Score(crop, fi) : 10;
                if (cost <= perDay * PaybackDays)
                {
                    full = false;
                    if (Afford(cost)) Do("/expand f" + (fi + 1).ToString(Inv));
                }
            }
            if (s.ToolLevel("bag") < 2) full = false;

            int plots = 0;
            foreach (var f in s.Fields) plots += f.Open;
            if (plots > s.StorageCapacity && Afford(Content.StorageUpgradeCost(s.StorageLevel))) Do("/upgrade storage");

            // 살 만한 칸을 다 샀으면 새 구역. 사치품(농기구 Lv3 등)보다 먼저
            double nextField = Content.FieldCost(s.Fields.Count + 1);
            if (full && Afford(nextField)) Do("/expand field");

            // 다른 구역 자동 수확: 슬롯이 모자라면 컨트롤러 Lv2
            for (int fi = 1; fi < s.Fields.Count && s.Farm.Controller > 0; fi++)
            {
                var f = s.Fields[fi];
                string n = (fi + 1).ToString(Inv);
                if (!f.Harvester && Afford(Content.EquipmentCost("harvester"))) Do("/buy harvester f" + n);
                if (f.Harvester && !HasRule("when f" + n + " ready"))
                {
                    if (Rules.Slots(s) <= s.Rules.Count && Afford(Content.ControllerCost(s.Farm.Controller))) Do("/upgrade controller");
                    if (Rules.Slots(s) > s.Rules.Count) Do("/auto add when f" + n + " ready do harvest f" + n);
                }
            }

            // 늦은 투자: 농기구 Lv3, 스프링클러 Lv2. 다음 구역 값의 절반 이상 여유가 있을 때만
            double luxury = Math.Max(0, nextField * 0.5);
            foreach (var t in new[] { "bag", "sickle", "can" })
                if (s.ToolLevel(t) == 2 && Afford(Math.Max(Content.ToolUpgradeCost(2) * 2, luxury))) Do("/upgrade " + t);
            for (int fi = 0; fi < s.Fields.Count; fi++)
                foreach (int cell in SprinklerCells)
                {
                    var f = s.Fields[fi];
                    if (cell < f.Open && f.Plots[cell].Sprinkler == 1 && Afford(Math.Max(Content.SprinklerUpgradeCost(1) * 2, luxury)))
                        Do("/upgrade sprinkler " + F(fi) + RangeParser.CellName(cell));
                }
        }

        // ------------------------------------------------------------------ 보고

        /// <summary>플레이어 세 유형: 자주(게임 6시간), 보통(하루), 가끔(이틀).</summary>
        public static readonly int[] Intervals = { 360, 1440, 2880 };
        public static readonly string[] IntervalNames = { "often (6h)", "regular (1d)", "casual (2d)" };

        public static BalanceSim Play(int interval, int maxDays)
        {
            var sim = new BalanceSim(interval, 12345);
            sim.Run(maxDays);
            return sim;
        }

        public static string Trace(int interval, int maxDays)
        {
            var sb = new StringBuilder();
            new BalanceSim(interval, 12345).Run(maxDays, sb);
            return sb.ToString();
        }

        public static string Report(int maxDays)
        {
            var sb = new StringBuilder();
            sb.Append("game day when first reached (real hours at 60s = 1 game hour: day x 0.4h)\n");
            sb.Append("player          ");
            foreach (var st in Steps) sb.Append(st.PadRight(10));
            sb.Append("commands\n");
            for (int k = 0; k < Intervals.Length; k++)
            {
                var sim = Play(Intervals[k], maxDays);
                sb.Append(IntervalNames[k].PadRight(16));
                foreach (var st in Steps)
                {
                    int d;
                    sb.Append((sim.Reached.TryGetValue(st, out d) ? d.ToString(Inv) : "-").PadRight(10));
                }
                sb.Append(sim.Commands.ToString(Inv)).Append('\n');
            }
            return sb.ToString();
        }
    }
}
