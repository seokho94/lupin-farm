using System;
using System.Collections.Generic;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    public sealed class HarvestResult
    {
        public readonly Dictionary<string, int> Got = new Dictionary<string, int>();

        /// <summary>창고가 가득 차서 남긴 칸 수.</summary>
        public int Left;

        public int Total
        {
            get
            {
                int n = 0;
                foreach (var kv in Got) n += kv.Value;
                return n;
            }
        }
    }

    public sealed class PlantResult
    {
        public int Planted, NoSeed, Used, Locked;
    }

    /// <summary>손 명령과 규칙이 같이 쓰는 농사 동작 (FR-AUTO-10: 같은 제약).</summary>
    public static class Farming
    {
        /// <summary>지금 그 구역에 심거나 다시 열릴 수 있는 계절인지. 온실은 늘 된다 (M3 K12).</summary>
        public static bool SeasonOk(GameState s, int field, CropDef crop)
        {
            if (field >= 0 && field < s.Fields.Count && s.Fields[field].Greenhouse) return true;
            return Content.InSeason(crop, GameClock.Season(s.GameMinute));
        }

        public static List<int> RipeCells(FieldState f)
        {
            var list = new List<int>();
            for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                if (Engine.Stage(f.Plots[i]) == Engine.StageRipe) list.Add(i);
            return list;
        }

        /// <summary>다 자란 칸이 하나라도 있는지 (목록을 만들지 않는다. 규칙 조건용).</summary>
        public static bool HasRipe(FieldState f)
        {
            for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
            {
                var p = f.Plots[i];
                if (p.Crop != null && Engine.Stage(p) == Engine.StageRipe) return true;
            }
            return false;
        }

        public static bool HasEmpty(FieldState f)
        {
            for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                if (IsEmpty(f.Plots[i])) return true;
            return false;
        }

        public static bool IsEmpty(PlotState p)
        {
            return p.Crop == null && p.Sprinkler <= 0;
        }

        public static List<int> EmptyCells(FieldState f)
        {
            var list = new List<int>();
            for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                if (IsEmpty(f.Plots[i])) list.Add(i);
            return list;
        }

        /// <summary>cells 중 다 자란 칸을 창고 용량까지 거둔다. 재수확 작물은 계절(또는 온실)이 맞으면 다시 자란다.</summary>
        public static HarvestResult Harvest(GameState s, int field, IEnumerable<int> cells)
        {
            var res = new HarvestResult();
            var f = s.Fields[field];
            int cap = s.StorageCapacity;
            int used = s.StorageUsed;
            foreach (int i in cells)
            {
                if (i < 0 || i >= f.Open) continue;
                var p = f.Plots[i];
                if (Engine.Stage(p) != Engine.StageRipe) continue;
                if (used >= cap)
                {
                    res.Left++;
                    continue;
                }
                var d = Content.Crop(p.Crop);
                GameState.Add(s.Storage, d.Id, 1);
                GameState.Add(res.Got, d.Id, 1);
                GameState.Add(s.Harvested, d.Id, 1);
                used++;
                if (d.Regrows && SeasonOk(s, field, d))
                {
                    p.Harvests++;
                    p.Growth = Math.Max(0, Engine.Need(p, d) - Content.GrowNeed(d.RegrowMinutes, s.PerkLevel("rich-soil")));
                }
                else
                {
                    p.Crop = null;
                    p.Growth = 0;
                    p.Harvests = 0;
                    p.Need = 0;
                }
            }
            return res;
        }

        /// <summary>cells 중 빈 칸에 씨앗을 하나씩 심는다. 해금·계절 검사는 부르는 쪽에서 한다.</summary>
        public static PlantResult Plant(GameState s, int field, CropDef crop, IEnumerable<int> cells)
        {
            var res = new PlantResult();
            var f = s.Fields[field];
            foreach (int i in cells)
            {
                if (i >= f.Open)
                {
                    res.Locked++;
                    continue;
                }
                var p = f.Plots[i];
                if (!IsEmpty(p))
                {
                    res.Used++;
                    continue;
                }
                if (GameState.Count(s.Seeds, crop.Id) <= 0)
                {
                    res.NoSeed++;
                    continue;
                }
                GameState.Add(s.Seeds, crop.Id, -1);
                p.Crop = crop.Id;
                p.Growth = 0;
                p.Harvests = 0;
                p.Need = Content.GrowNeed(crop.GrowMinutes, s.PerkLevel("rich-soil"));
                res.Planted++;
            }
            return res;
        }

        /// <summary>창고의 작물을 판다. 판매 압력 반영 (Market.Sell).</summary>
        public static SaleQuote Sell(GameState s, string crop, int qty)
        {
            var q = Market.Sell(s, crop, qty);
            GameState.Add(s.Storage, crop, -qty);
            s.Gold += q.Total;
            s.Earned += q.Total;
            return q;
        }

        /// <summary>창고를 모두 판다. 판 작물은 sold에 담는다.</summary>
        public static double SellAll(GameState s, Dictionary<string, int> sold)
        {
            double total = 0;
            foreach (var c in Content.Crops)
            {
                int n = GameState.Count(s.Storage, c.Id);
                if (n <= 0) continue;
                total += Market.Sell(s, c.Id, n).Total;
                GameState.Add(sold, c.Id, n);
            }
            s.Storage.Clear();
            s.Gold += total;
            s.Earned += total;
            return total;
        }

        /// <summary>스프링클러 하나가 범위 안의 열린 칸에 물을 준다 (M3 K3). 물을 준 칸 수(스프링클러 칸 제외).</summary>
        public static int WaterAround(GameState s, int field, int cell, int level)
        {
            var f = s.Fields[field];
            long until = s.GameMinute + Content.WaterMinutes;
            int n = 0;
            foreach (int i in Reach(cell, level))
            {
                if (i >= f.Open) continue;
                var p = f.Plots[i];
                if (p.Sprinkler > 0) continue;
                p.WaterUntil = Math.Max(p.WaterUntil, until);
                n++;
            }
            return n;
        }

        /// <summary>
        /// 칸 cell에 놓은 레벨 level 스프링클러가 닿는 칸 번호(자기 칸 제외). 구역은 3줄이라
        /// Lv3(5×5)도 최대 14칸이다. 잠긴 칸과 스프링클러 칸은 거르지 않는다.
        /// </summary>
        public static List<int> Reach(int cell, int level)
        {
            var list = new List<int>();
            int row = cell / Content.Cols, col = cell % Content.Cols;
            foreach (var o in Content.SprinklerOffsets(level))
            {
                int r = row + o[0], c = col + o[1];
                if (r < 0 || r >= Content.Rows || c < 0 || c >= Content.Cols) continue;
                list.Add(r * Content.Cols + c);
            }
            return list;
        }

        /// <summary>
        /// 스프링클러 범위 안의 칸. 이 칸들은 놓을 때와 매일 06:00에 물을 받으므로 물이 끊기지 않는다.
        /// 남은 시간 계산(정보 칸, 스피너, /growing)이 "물이 다음 06:00에 끝난다"로 보지 않게 쓴다.
        /// </summary>
        public static bool[] Covered(FieldState f)
        {
            var cov = new bool[Content.PlotsPerField];
            for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
            {
                int lv = f.Plots[i].Sprinkler;
                if (lv <= 0) continue;
                foreach (int k in Reach(i, lv))
                    if (k < f.Open && f.Plots[k].Sprinkler <= 0) cov[k] = true;
            }
            return cov;
        }

        /// <summary>칸 cell에 레벨 level을 두면 물을 받을 열린 칸(스프링클러 칸 제외)과 자기 칸 (M5 K9 미리 보기).</summary>
        public static bool[] RangeIf(FieldState f, int cell, int level)
        {
            var range = new bool[Content.PlotsPerField];
            range[cell] = true;
            foreach (int k in Reach(cell, level))
                if (k < f.Open && f.Plots[k].Sprinkler <= 0) range[k] = true;
            return range;
        }

        /// <summary>보관함에서 가장 높은 레벨. 비었으면 0.</summary>
        public static int BestBoxLevel(GameState s)
        {
            for (int lv = Content.MaxSprinklerLevel; lv >= 1; lv--)
                if (GameState.Count(s.EquipmentBox, GameState.BoxKey(lv)) > 0) return lv;
            return 0;
        }

        /// <summary>매일 06:00: 모든 스프링클러가 물을 준다.</summary>
        public static void SprinklerDay(GameState s)
        {
            for (int fi = 0; fi < s.Fields.Count; fi++)
            {
                var f = s.Fields[fi];
                for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                    if (f.Plots[i].Sprinkler > 0) WaterAround(s, fi, i, f.Plots[i].Sprinkler);
            }
        }

        /// <summary>보관함에서 가장 높은 레벨의 스프링클러를 꺼낸다. 없으면 0.</summary>
        public static int TakeSprinkler(GameState s)
        {
            return TakeSprinkler(s, 0);
        }

        /// <summary>보관함에서 스프링클러를 꺼낸다. level이 0이면 가장 높은 레벨 (M5 K11). 없으면 0.</summary>
        public static int TakeSprinkler(GameState s, int level)
        {
            for (int lv = Content.MaxSprinklerLevel; lv >= 1; lv--)
            {
                if (level > 0 && lv != level) continue;
                string k = GameState.BoxKey(lv);
                if (GameState.Count(s.EquipmentBox, k) > 0)
                {
                    GameState.Add(s.EquipmentBox, k, -1);
                    return lv;
                }
            }
            return 0;
        }
    }
}
