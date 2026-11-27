using System;
using System.Collections.Generic;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    /// <summary>한 번의 진행에서 생긴 일. 구역별로 묶는다 (K6).</summary>
    public sealed class AdvanceReport
    {
        public long Minutes;
        public readonly SortedDictionary<int, Dictionary<string, int>> Ready = new SortedDictionary<int, Dictionary<string, int>>();
        public readonly SortedDictionary<int, int> Dried = new SortedDictionary<int, int>();

        /// <summary>지나간 하루 시작(06:00) 횟수 = 시세 갱신 횟수.</summary>
        public int Days;

        /// <summary>새로 시작된 계절들 (순서대로).</summary>
        public readonly List<int> SeasonStarts = new List<int>();

        /// <summary>규칙 실행 결과. 같은 규칙의 성공은 하나로 합친다 (M3 K9).</summary>
        public readonly List<RuleRun> Runs = new List<RuleRun>();

        /// <summary>이번 진행에서 기록한 체크포인트 날 (M4 K3).</summary>
        public readonly List<int> Checkpoints = new List<int>();

        public bool Any
        {
            get { return Ready.Count > 0 || Dried.Count > 0 || SeasonStarts.Count > 0 || Runs.Count > 0 || Checkpoints.Count > 0; }
        }

        public void AddRun(RuleRun run)
        {
            foreach (var r in Runs)
            {
                if (r.CanMerge(run))
                {
                    r.Merge(run);
                    return;
                }
            }
            Runs.Add(run);
        }

        /// <summary>규칙 행동별 합계 (부재 중 요약).</summary>
        public Dictionary<string, int> AutoCounts(ActKind act)
        {
            var d = new Dictionary<string, int>();
            foreach (var r in Runs)
                if (r.Fail == null && r.Rule.Act == act)
                    foreach (var kv in r.Counts) GameState.Add(d, kv.Key, kv.Value);
            return d;
        }

        public double AutoGold(ActKind act)
        {
            double g = 0;
            foreach (var r in Runs) if (r.Fail == null && r.Rule.Act == act) g += r.Gold;
            return g;
        }

        public Dictionary<string, int> ReadyTotal()
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in Ready)
                foreach (var c in kv.Value)
                    GameState.Add(d, c.Key, c.Value);
            return d;
        }

        public int DriedTotal()
        {
            int n = 0;
            foreach (var kv in Dried) n += kv.Value;
            return n;
        }
    }

    /// <summary>구역(또는 전체) 상태 요약. 정보 패널과 스피너가 쓴다.</summary>
    public sealed class FieldStats
    {
        public int Open;
        public int Ripe;
        public int Growing;
        public int Dry;
        public int Empty;

        /// <summary>물이 끝나기 전에 다 자라는 작물 중 가장 빠른 것까지 남은 게임 분. 없으면 -1.</summary>
        public long NextMinutes = -1;
        public string NextCrop;
        public int NextField = -1;
        public readonly List<int> NextCells = new List<int>();
    }

    public static class Engine
    {
        public const int StageSeed = 0;
        public const int StageSprout = 1;
        public const int StageGrowing = 2;
        public const int StageRipe = 3;

        /// <summary>그 칸이 다 자라는 데 필요한 성장 분 (심을 때 정한 값, 없으면 작물 기본값. M4 K13).</summary>
        public static int Need(PlotState p, CropDef d)
        {
            return p.Need > 0 ? p.Need : d.GrowMinutes;
        }

        /// <summary>-1 빈 칸, 0 씨앗(0~25%), 1 새싹(~60%), 2 자라는 중(~100%), 3 다 자람.</summary>
        public static int Stage(PlotState p)
        {
            if (p == null || p.Crop == null) return -1;
            var d = Content.Crop(p.Crop);
            if (d == null) return -1;
            int need = Need(p, d);
            if (p.Growth >= need) return StageRipe;
            double r = (double)p.Growth / need;
            if (r < 0.25) return StageSeed;
            if (r < 0.6) return StageSprout;
            return StageGrowing;
        }

        public static bool IsWet(PlotState p, long now)
        {
            return p.WaterUntil > now;
        }

        /// <summary>
        /// 게임 시간을 minutes만큼 진행한다. 성장은 물이 있는 구간만 센다.
        /// 하루 시작(06:00)과 규칙 검사 시각에서 구간을 나눈다: 성장 → 경계 이벤트(시세 → 스프링클러 → 계절 → 규칙) → 마름 판정 (M3 K5).
        /// </summary>
        public static AdvanceReport Advance(GameState s, long minutes)
        {
            var rep = new AdvanceReport();
            rep.Minutes = Math.Max(0, minutes);
            if (minutes <= 0) return rep;

            long end = s.GameMinute + minutes;
            var dryCandidates = new List<int[]>();
            // 규칙 결과가 바뀔 수 있는 일(새로 다 자람, 하루 시작, 직전 검사의 실행)이 없으면 검사를 건너뛴다.
            // 같은 상태면 같은 결과이고, 같은 이유의 실패는 어차피 조용하다 (M3 이슈 참고).
            bool rulesDirty = true;
            while (s.GameMinute < end)
            {
                long t0 = s.GameMinute;
                long t1 = Math.Min(end, (t0 / GameClock.MinutesPerDay + 1) * GameClock.MinutesPerDay);
                int interval = Rules.CheckInterval(s);
                if (interval > 0)
                {
                    if (rulesDirty)
                    {
                        t1 = Math.Min(t1, (t0 / interval + 1) * interval);
                    }
                    else
                    {
                        // 바뀐 것이 없으면 다음에 다 자라는 칸 뒤의 첫 검사 시각까지 한 번에 간다 (결과는 같고 구간만 줄어든다)
                        long ripe = NextRipe(s, t0);
                        if (ripe < long.MaxValue) t1 = Math.Min(t1, (ripe + interval - 1) / interval * interval);
                    }
                }

                dryCandidates.Clear();
                if (Grow(s, t0, t1, rep, dryCandidates) > 0) rulesDirty = true;
                s.GameMinute = t1;

                if (t1 % GameClock.MinutesPerDay == 0)
                {
                    // 끝난 날의 기록: 판 최고, 체크포인트 (시세 갱신 전, M4 K2·K3)
                    Records.DayEnd(s, (int)(t1 / GameClock.MinutesPerDay), rep);
                    Market.DailyUpdate(s);
                    Farming.SprinklerDay(s);
                    rep.Days++;
                    int season = GameClock.Season(t1);
                    if (season != GameClock.Season(t1 - 1)) rep.SeasonStarts.Add(season);
                    rulesDirty = true;
                }
                if (interval > 0 && t1 % interval == 0 && rulesDirty) rulesDirty = Rules.Run(s, rep);

                foreach (var c in dryCandidates)
                {
                    var p = s.Fields[c[0]].Plots[c[1]];
                    if (p.Crop == null || p.WaterUntil > t1 || Stage(p) == StageRipe) continue;
                    int n;
                    rep.Dried.TryGetValue(c[0], out n);
                    rep.Dried[c[0]] = n + 1;
                }
            }
            return rep;
        }

        /// <summary>t0 뒤에 처음으로 다 자라는 게임 분 (물이 그때까지 있는 칸만). 없으면 long.MaxValue.</summary>
        static long NextRipe(GameState s, long t0)
        {
            long best = long.MaxValue;
            foreach (var f in s.Fields)
            {
                for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                {
                    var p = f.Plots[i];
                    if (p.Crop == null) continue;
                    var d = Content.Crop(p.Crop);
                    if (d == null) continue;
                    int need = Need(p, d);
                    if (p.Growth >= need) continue;
                    long rem = need - p.Growth;
                    if (p.WaterUntil - t0 >= rem && t0 + rem < best) best = t0 + rem;
                }
            }
            return best;
        }

        /// <summary>(t0, t1] 동안의 성장. 이 구간에서 물이 끝나는 칸은 마름 후보로 모은다. 새로 다 자란 칸 수를 돌려준다.</summary>
        static int Grow(GameState s, long t0, long t1, AdvanceReport rep, List<int[]> dryCandidates)
        {
            int ripened = 0;
            for (int fi = 0; fi < s.Fields.Count; fi++)
            {
                var f = s.Fields[fi];
                for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                {
                    var p = f.Plots[i];
                    if (p.Crop == null) continue;
                    var d = Content.Crop(p.Crop);
                    if (d == null) continue;
                    int need = Need(p, d);
                    if (p.Growth >= need) continue;

                    long wet = Math.Min(p.WaterUntil, t1) - t0;
                    if (wet <= 0) continue;
                    long add = Math.Min(wet, need - p.Growth);
                    p.Growth += (int)add;

                    if (p.Growth >= need)
                    {
                        Dictionary<string, int> byCrop;
                        if (!rep.Ready.TryGetValue(fi, out byCrop))
                        {
                            byCrop = new Dictionary<string, int>();
                            rep.Ready[fi] = byCrop;
                        }
                        GameState.Add(byCrop, p.Crop, 1);
                        ripened++;
                    }
                    else if (p.WaterUntil <= t1)
                    {
                        dryCandidates.Add(new[] { fi, i });
                    }
                }
            }
            return ripened;
        }

        /// <summary>계절에 심을 수 있는 작물 (해금 여부와 상관없이 콘텐츠 순서).</summary>
        public static Joined SeasonCrops(int season)
        {
            var items = new List<object>();
            foreach (var c in Content.Crops)
                if (Content.InSeason(c, season)) items.Add(new Msg("crop." + c.Id));
            return new Joined(items);
        }

        /// <summary>작물이 자라는 계절 이름들.</summary>
        public static Joined CropSeasons(CropDef crop)
        {
            var items = new List<object>();
            foreach (int season in crop.Seasons) items.Add(new Msg("season." + season));
            return new Joined(items);
        }

        /// <summary>꺼져 있던 실제 시간을 상한까지 게임 분으로 바꾼다.</summary>
        public static long OfflineGameMinutes(double realSeconds, double capHours, int timescale, out double appliedSeconds)
        {
            if (double.IsNaN(realSeconds) || realSeconds < 0) realSeconds = 0;
            appliedSeconds = Math.Min(realSeconds, capHours * 3600.0);
            return (long)Math.Floor(GameClock.GameMinutesFromReal(appliedSeconds, timescale));
        }

        /// <summary>꺼져 있던 시간의 반영 상한(실제 시간). Caretaker 레벨별 (M3 K13) + Night Shift 레벨당 2시간 (M4 K13).</summary>
        public static double OfflineCapHours(GameState s)
        {
            return Content.CaretakerCap(s.Farm == null ? 0 : s.Farm.Caretaker) + Content.NightShiftHours * s.PerkLevel("night-shift");
        }

        /// <summary>field가 -1이면 모든 구역.</summary>
        public static FieldStats Stats(GameState s, int field)
        {
            var st = new FieldStats();
            long now = s.GameMinute;
            for (int fi = 0; fi < s.Fields.Count; fi++)
            {
                if (field >= 0 && fi != field) continue;
                var f = s.Fields[fi];
                st.Open += f.Open;
                bool[] covered = f.SprinklerCount > 0 ? Farming.Covered(f) : null;
                for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                {
                    var p = f.Plots[i];
                    int stage = Stage(p);
                    if (stage < 0)
                    {
                        if (p.Sprinkler <= 0) st.Empty++;
                        continue;
                    }
                    if (stage == StageRipe)
                    {
                        st.Ripe++;
                        continue;
                    }
                    st.Growing++;
                    if (!IsWet(p, now))
                    {
                        st.Dry++;
                        continue;
                    }
                    var d = Content.Crop(p.Crop);
                    long remaining = Need(p, d) - p.Growth;
                    // 스프링클러가 닿는 칸은 매일 06:00에 다시 물을 받으므로 끝까지 자란다
                    if (p.WaterUntil - now < remaining && (covered == null || !covered[i])) continue;
                    if (st.NextMinutes < 0 || remaining < st.NextMinutes)
                    {
                        st.NextMinutes = remaining;
                        st.NextCrop = p.Crop;
                        st.NextField = fi;
                        st.NextCells.Clear();
                        st.NextCells.Add(i);
                    }
                    else if (remaining == st.NextMinutes && p.Crop == st.NextCrop && fi == st.NextField)
                    {
                        st.NextCells.Add(i);
                    }
                }
            }
            return st;
        }

        public static Msg FieldLabel(int fieldIndex)
        {
            return new Msg("field.label", fieldIndex + 1);
        }

        public static Msg CropCount(string crop, int n)
        {
            return new Msg("crop.count", new Msg("crop." + crop), n);
        }

        /// <summary>작물별 개수를 콘텐츠 순서로 "turnip x4 · corn x2".</summary>
        public static Joined CropList(Dictionary<string, int> counts)
        {
            var items = new List<object>();
            foreach (var c in Content.Crops)
            {
                int n = GameState.Count(counts, c.Id);
                if (n > 0) items.Add(CropCount(c.Id, n));
            }
            return new Joined(items);
        }

        /// <summary>진행 결과를 이벤트 로그로 (K6). 규칙 실행 결과(Runs)는 화면이 따로 합쳐서 남긴다 (M3 K9).</summary>
        public static List<LogEntry> ReportLogs(GameState s, AdvanceReport r)
        {
            var list = new List<LogEntry>();
            foreach (var kv in r.Ready)
            {
                // 자동 수확기가 이미 거뒀거나 곧 거둘 구역은 알리지 않는다 (M3 K20)
                if (kv.Key < s.Fields.Count && (!Farming.HasRipe(s.Fields[kv.Key]) || Rules.AutoHarvests(s, kv.Key))) continue;
                list.Add(LogEntry.Make(LogKind.Event, new Msg("t.ready", FieldLabel(kv.Key)), new Msg("ready.line", CropList(kv.Value))));
            }
            foreach (var kv in r.Dried)
                list.Add(LogEntry.Make(LogKind.Event, new Msg("t.dry", FieldLabel(kv.Key)), new Msg("dry.line", kv.Value)));
            if (r.SeasonStarts.Count > 0)
            {
                int season = r.SeasonStarts[r.SeasonStarts.Count - 1];
                list.Add(LogEntry.Make(LogKind.Event, new Msg("t.season", new Msg("season." + season)), new Msg("season.line", SeasonCrops(season)), new Msg("season.hint")));
            }
            return list;
        }

        /// <summary>지금 다 자라 있는 작물 (모든 구역).</summary>
        public static Dictionary<string, int> RipeNow(GameState s)
        {
            var d = new Dictionary<string, int>();
            foreach (var f in s.Fields)
                foreach (int i in Farming.RipeCells(f)) GameState.Add(d, f.Plots[i].Crop, 1);
            return d;
        }

        /// <summary>부재 중 요약 줄 (시작 환영 상자, 절전 복귀 이벤트). 지금 상태 기준 + 자동화 합계 (M3 K14).</summary>
        public static List<object> OfflineLines(GameState s, double realSeconds, double appliedSeconds, double capHours, AdvanceReport r)
        {
            return OfflineLines(s, realSeconds, appliedSeconds, capHours, r, null);
        }

        /// <summary>extra: 첫 줄 다음에 넣을 줄 (그동안 지나간 체크포인트, M4 K17).</summary>
        public static List<object> OfflineLines(GameState s, double realSeconds, double appliedSeconds, double capHours, AdvanceReport r, List<object> extra)
        {
            var lines = new List<object>();
            lines.Add(new Msg("welcome.offline", new Dur(realSeconds), new Dur(capHours * 3600), GameSpan(r.Minutes)));
            if (extra != null) lines.AddRange(extra);

            var harvested = r.AutoCounts(ActKind.Harvest);
            var planted = r.AutoCounts(ActKind.Plant);
            var sold = r.AutoCounts(ActKind.Sell);
            var bought = r.AutoCounts(ActKind.Buy);
            if (harvested.Count > 0) lines.Add(new Msg("welcome.auto_harvest", CropList(harvested)));
            if (planted.Count > 0) lines.Add(new Msg("welcome.auto_plant", CropList(planted)));
            if (sold.Count > 0) lines.Add(new Msg("welcome.auto_sell", CropList(sold), NumberFormat.Gold(r.AutoGold(ActKind.Sell))));
            if (bought.Count > 0) lines.Add(new Msg("welcome.auto_buy", CropList(bought), NumberFormat.Gold(r.AutoGold(ActKind.Buy))));
            int fails = 0;
            foreach (var run in r.Runs)
            {
                if (run.Fail == null || fails >= 3) continue;
                lines.Add(new Msg("welcome.auto_fail", run.Number, Rules.FailMsg(run.Rule, run.Fail)));
                fails++;
            }

            var ready = RipeNow(s);
            if (ready.Count > 0) lines.Add(new Msg("welcome.ready", CropList(ready)));
            int dry = Stats(s, -1).Dry;
            if (dry > 0) lines.Add(new Msg("welcome.dried", dry));
            if (r.SeasonStarts.Count > 0)
            {
                int season = GameClock.Season(s.GameMinute);
                lines.Add(new Msg("welcome.season", new Msg("season." + season), GameClock.SeasonDay(s.GameMinute), SeasonCrops(season)));
            }
            if (ready.Count == 0 && dry == 0 && r.Runs.Count == 0 && (extra == null || extra.Count == 0)) lines.Add(new Msg("welcome.nothing"));
            return lines;
        }

        public static Msg GameSpan(long gameMinutes)
        {
            if (gameMinutes >= GameClock.MinutesPerDay)
            {
                double days = Math.Round(gameMinutes / (double)GameClock.MinutesPerDay, 1);
                return new Msg("span.days", days.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            }
            return new Msg("span.hours", (gameMinutes / 60).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
