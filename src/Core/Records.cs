using System;
using System.Collections.Generic;
using System.Globalization;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    /// <summary>
    /// 총자산, 판 최고 기록, 체크포인트, 일별 기록, 도감, 업적 (M4 K1~K5, K14, K15).
    /// 게임 로직 안(엔진·규칙)에서는 GameState에만 쌓고, 화면이 Sync로 프로필에 옮긴다. 날짜는 화면이 넘긴다.
    /// </summary>
    public static class Records
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Key(long day)
        {
            return day.ToString(Inv);
        }

        /// <summary>총자산 = 골드 + 창고 × 지금 단가 + 씨앗 × 씨앗값 + 투자액 (D11, K1).</summary>
        public static double NetWorth(GameState s)
        {
            double v = s.Gold + s.Invested;
            foreach (var kv in s.Storage) if (kv.Value > 0) v += kv.Value * Market.Price(s, kv.Key);
            foreach (var kv in s.Seeds)
            {
                var d = Content.Crop(kv.Key);
                if (d != null && kv.Value > 0) v += kv.Value * d.SeedCost;
            }
            return v;
        }

        /// <summary>판 최고 기록을 갱신한다 (K2). 날짜는 지금 게임 날.</summary>
        public static void Observe(GameState s)
        {
            Peak(s, NetWorth(s), GameClock.Day(s.GameMinute));
        }

        static void Peak(GameState s, double nw, int day)
        {
            if (nw > s.PeakNetWorth)
            {
                s.PeakNetWorth = nw;
                s.PeakDay = day;
            }
        }

        /// <summary>Day day가 끝난 순간 (게임 분 = day × 1440, 시세 갱신 전): 판 최고, 체크포인트 (K3).</summary>
        public static void DayEnd(GameState s, int day, AdvanceReport rep)
        {
            double nw = NetWorth(s);
            Peak(s, nw, day);
            if (!Content.IsCheckpointDay(day)) return;
            s.Checkpoints[Key(day)] = nw;
            if (rep != null) rep.Checkpoints.Add(day);
        }

        // ------------------------------------------------------------------ 판 기록

        /// <summary>현재 판의 기록. 없으면 만든다 (이전 세이브의 판은 처음 기록할 때 시작 날짜 = 그날).</summary>
        public static RunRecord Current(GameState s, Profile p, string today)
        {
            var r = p.RunOf(s.Run);
            if (r != null) return r;
            r = new RunRecord();
            r.Run = s.Run;
            r.StartedAt = today;
            r.Normalize();
            p.Runs.Add(r);
            return r;
        }

        /// <summary>지난 판들(현재 판 제외) 중 그 체크포인트의 최고 기록.</summary>
        public static bool BestBefore(Profile p, int currentRun, long day, out double best, out int bestRun)
        {
            best = 0;
            bestRun = 0;
            bool any = false;
            string k = Key(day);
            foreach (var r in p.Runs)
            {
                if (r.Run == currentRun || r.Checkpoints == null) continue;
                double v;
                if (!r.Checkpoints.TryGetValue(k, out v)) continue;
                if (!any || v > best)
                {
                    best = v;
                    bestRun = r.Run;
                    any = true;
                }
            }
            return any;
        }

        /// <summary>체크포인트 한 줄: "310K · best 280K (run #2) · ▲ 30.0K NEW BEST". isBest: 지난 최고를 넘었는지.</summary>
        public static Msg CheckpointLine(GameState s, Profile p, long day, out bool isBest)
        {
            isBest = false;
            double v;
            s.Checkpoints.TryGetValue(Key(day), out v);
            double best;
            int bestRun;
            if (!BestBefore(p, s.Run, day, out best, out bestRun)) return new Msg("checkpoint.first", NumberFormat.Num(v));
            if (v > best)
            {
                isBest = true;
                return new Msg("checkpoint.best", NumberFormat.Num(v), NumberFormat.Num(best), bestRun, NumberFormat.Num(v - best));
            }
            return new Msg("checkpoint.behind", NumberFormat.Num(v), NumberFormat.Num(best), bestRun, NumberFormat.Num(best - v));
        }

        /// <summary>진행에서 지나간 체크포인트를 노란 이벤트로 (K3). NEW BEST면 업적 new-best.</summary>
        public static List<LogEntry> CheckpointLogs(GameState s, Profile p, AdvanceReport rep, string today)
        {
            var list = new List<LogEntry>();
            foreach (int day in rep.Checkpoints)
            {
                bool best;
                var line = CheckpointLine(s, p, day, out best);
                list.Add(LogEntry.Make(LogKind.Event, new Msg("t.checkpoint", day), line));
                if (best) Achieve(p, "new-best", today, list);
            }
            return list;
        }

        /// <summary>부재 중 요약에 넣을 체크포인트 줄 (K17). NEW BEST 업적은 logs에.</summary>
        public static List<object> CheckpointSummary(GameState s, Profile p, AdvanceReport rep, string today, List<LogEntry> logs)
        {
            var lines = new List<object>();
            foreach (int day in rep.Checkpoints)
            {
                bool best;
                lines.Add(new Msg("welcome.checkpoint", day, CheckpointLine(s, p, day, out best)));
                if (best) Achieve(p, "new-best", today, logs);
            }
            return lines;
        }

        /// <summary>
        /// 게임 상태를 프로필 기록에 옮긴다: 판 최고, 체크포인트, 마지막 날, 일별 기록, 도감, 업적.
        /// 처음 수확·별 증가·업적은 노란 이벤트로 돌려준다 (K5, K14, K15).
        /// </summary>
        public static List<LogEntry> Sync(GameState s, Profile p, string today)
        {
            var logs = new List<LogEntry>();
            double nw = NetWorth(s);
            Peak(s, nw, GameClock.Day(s.GameMinute));

            var rr = Current(s, p, today);
            if (s.PeakNetWorth >= rr.Peak)
            {
                rr.Peak = s.PeakNetWorth;
                rr.PeakDay = s.PeakDay;
            }
            rr.LastDay = GameClock.Day(s.GameMinute);
            foreach (var kv in s.Checkpoints) rr.Checkpoints[kv.Key] = kv.Value;

            Daily(s, p, today, nw);
            Dex(s, p, rr, today, logs);
            Achievements(s, p, today, logs);
            return logs;
        }

        static void Daily(GameState s, Profile p, string today, double nw)
        {
            DailyRecord d = null;
            for (int i = p.Daily.Count - 1; i >= 0; i--)
            {
                var e = p.Daily[i];
                if (e.Date == today && e.Run == s.Run)
                {
                    d = e;
                    break;
                }
            }
            if (d == null)
            {
                d = new DailyRecord();
                d.Date = today;
                d.Run = s.Run;
                d.EarnedBase = s.Earned;
                p.Daily.Add(d);
            }
            if (s.Earned < d.EarnedBase) d.EarnedBase = s.Earned;
            d.NetWorth = nw;
            d.Earned = s.Earned - d.EarnedBase;
        }

        static void Dex(GameState s, Profile p, RunRecord rr, string today, List<LogEntry> logs)
        {
            foreach (var c in Content.Crops)
            {
                int now = GameState.Count(s.Harvested, c.Id);
                int had = GameState.Count(rr.Harvested, c.Id);
                if (now == had) continue;
                rr.Harvested[c.Id] = now;
                if (now < had) continue;

                DexEntry e;
                if (!p.Dex.TryGetValue(c.Id, out e))
                {
                    e = new DexEntry();
                    p.Dex[c.Id] = e;
                }
                int before = Content.DexStars(e.Total);
                e.Total += now - had;
                bool first = e.First == null;
                if (first)
                {
                    e.First = today;
                    e.FirstRun = s.Run;
                }
                int after = Content.DexStars(e.Total);
                if (first || after > before)
                {
                    var line = first ? new Msg("dex.first", Stars(after), NumberFormat.Int(e.Total)) : new Msg("dex.star", Stars(after), NumberFormat.Int(e.Total));
                    logs.Add(LogEntry.Make(LogKind.Event, new Msg("t.dex_crop", new Msg("crop." + c.Id)), line));
                }
            }
        }

        public static string Stars(int n)
        {
            return n <= 0 ? "-" : new string('★', n);
        }

        /// <summary>업적 조건 검사 (02 3.8절). new-best, first-bloom은 그 일이 생긴 곳에서 부른다.</summary>
        static void Achievements(GameState s, Profile p, string today, List<LogEntry> logs)
        {
            var a = p.Achievements;
            if (!a.ContainsKey("first-harvest"))
                foreach (var kv in p.Dex) if (kv.Value.Total > 0) { Achieve(p, "first-harvest", today, logs); break; }
            if (!a.ContainsKey("gold-1k") && s.Gold >= 1000) Achieve(p, "gold-1k", today, logs);
            if (!a.ContainsKey("field-2") && s.Fields.Count >= 2) Achieve(p, "field-2", today, logs);
            if (!a.ContainsKey("first-sprinkler"))
                foreach (var f in s.Fields) if (f.SprinklerCount > 0) { Achieve(p, "first-sprinkler", today, logs); break; }
            if (!a.ContainsKey("first-rule"))
                foreach (var r in s.Rules) if (r.Runs > 0) { Achieve(p, "first-rule", today, logs); break; }
            if (!a.ContainsKey("full-auto") && s.FullAuto) Achieve(p, "full-auto", today, logs);
            if (!a.ContainsKey("four-seasons") && FourSeasons(p)) Achieve(p, "four-seasons", today, logs);
            if (!a.ContainsKey("net-1m") && s.PeakNetWorth >= 1000000) Achieve(p, "net-1m", today, logs);
        }

        /// <summary>네 계절마다 그 계절 작물을 하나 이상 수확했는지 (모든 판, K15).</summary>
        public static bool FourSeasons(Profile p)
        {
            for (int season = 0; season < 4; season++)
            {
                bool ok = false;
                foreach (var c in Content.Crops)
                {
                    DexEntry e;
                    if (Content.InSeason(c, season) && p.Dex.TryGetValue(c.Id, out e) && e.Total > 0)
                    {
                        ok = true;
                        break;
                    }
                }
                if (!ok) return false;
            }
            return true;
        }

        public static void Achieve(Profile p, string id, string today, List<LogEntry> logs)
        {
            if (p.Achievements.ContainsKey(id)) return;
            p.Achievements[id] = today;
            logs.Add(LogEntry.Make(LogKind.Event, new Msg("t.achievement", new Msg("ach." + id)),
                new Msg("ach." + id + ".desc"), new Msg("ach.count", p.Achievements.Count, Content.Achievements.Length)));
        }

        /// <summary>판별 최고 기록 순위 (/rank). 같으면 판 번호 순.</summary>
        public static List<RunRecord> Ranking(Profile p)
        {
            var list = new List<RunRecord>(p.Runs);
            list.Sort(delegate(RunRecord x, RunRecord y)
            {
                int c = y.Peak.CompareTo(x.Peak);
                return c != 0 ? c : x.Run.CompareTo(y.Run);
            });
            return list;
        }

        /// <summary>표에 보일 체크포인트 날: Day 7부터, 이번 판·지난 판이 닿은 마지막 날까지, 이번 판의 다음 체크포인트까지 (K6).</summary>
        public static List<long> CheckpointRows(GameState s, Profile p)
        {
            long last = Content.NextCheckpointDay(GameClock.Day(s.GameMinute) - 1);
            foreach (var r in p.Runs)
                foreach (var k in r.Checkpoints.Keys)
                {
                    long d;
                    if (long.TryParse(k, NumberStyles.None, Inv, out d) && d > last) last = d;
                }
            foreach (var k in s.Checkpoints.Keys)
            {
                long d;
                if (long.TryParse(k, NumberStyles.None, Inv, out d) && d > last) last = d;
            }
            var rows = new List<long>();
            for (long d = Content.FirstCheckpointDay; d <= last; d *= 2) rows.Add(d);
            return rows;
        }
    }
}
