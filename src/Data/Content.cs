using System;
using System.Collections.Generic;

namespace LupinFarm.Data
{
    /// <summary>작물 정의. 수치는 02_도메인 모델 및 데이터 포맷.md 3.2절의 초기값.</summary>
    public sealed class CropDef
    {
        public readonly string Id;
        public readonly int[] Seasons;
        public readonly double UnlockCost;
        public readonly double SeedCost;
        public readonly double BasePrice;
        public readonly int GrowDays;
        public readonly int RegrowDays;

        public CropDef(string id, int[] seasons, double unlockCost, double seedCost, double basePrice, int growDays, int regrowDays)
        {
            Id = id;
            Seasons = seasons;
            UnlockCost = unlockCost;
            SeedCost = seedCost;
            BasePrice = basePrice;
            GrowDays = growDays;
            RegrowDays = regrowDays;
        }

        public int GrowMinutes { get { return GrowDays * 1440; } }
        public int RegrowMinutes { get { return RegrowDays * 1440; } }
        public bool Regrows { get { return RegrowDays > 0; } }
        public bool IsFree { get { return UnlockCost <= 0; } }
    }

    /// <summary>밸런스와 콘텐츠는 이 파일 한 곳에서 고친다 (NFR-05).</summary>
    public static class Content
    {
        public const int Cols = 10;
        public const int Rows = 3;
        public const int PlotsPerField = Cols * Rows;
        public const int StartOpenPlots = 6;
        public const double StartGold = 100;
        public const int StorageBase = 40;

        public const int DefaultTimescale = 60;
        public const int MinTimescale = 5;
        public const int MaxTimescale = 600;
        public const int SeasonDays = 7;
        public const int WaterMinutes = 1440;
        public const double OfflineCapHours = 2;
        public const double SleepGapSeconds = 300;

        public const double ConfirmGold = 1000;
        public const int MaxToolLevel = 3;
        public const int HistoryMax = 100;
        public const int LogMax = 500;
        public const int MaxQuantity = 1000000;

        public const int MinWidth = 120;
        public const int MinHeight = 30;

        public static readonly string[] Tools = { "can", "bag", "sickle" };

        public static readonly CropDef[] Crops =
        {
            new CropDef("turnip",     new[] { 0 },    0,    10,  30,  2, 0),
            new CropDef("potato",     new[] { 0 },    200,  25,  80,  4, 0),
            new CropDef("strawberry", new[] { 0 },    1500, 80,  55,  6, 3),
            new CropDef("corn",       new[] { 1, 2 }, 0,    60,  150, 7, 0),
            new CropDef("tomato",     new[] { 1 },    600,  40,  45,  5, 3),
            new CropDef("melon",      new[] { 1 },    4000, 150, 500, 10, 0),
            new CropDef("carrot",     new[] { 2 },    0,    20,  50,  3, 0),
            new CropDef("eggplant",   new[] { 2 },    900,  50,  40,  5, 2),
            new CropDef("pumpkin",    new[] { 2 },    5000, 100, 380, 10, 0),
            new CropDef("mushroom",   new[] { 3 },    300,  30,  70,  3, 0),
        };

        static readonly Dictionary<string, CropDef> cropById = new Dictionary<string, CropDef>(StringComparer.OrdinalIgnoreCase);

        static Content()
        {
            foreach (var c in Crops) cropById[c.Id] = c;
        }

        public static CropDef Crop(string id)
        {
            CropDef c;
            if (id != null && cropById.TryGetValue(id, out c)) return c;
            return null;
        }

        public static bool IsTool(string id)
        {
            return Array.IndexOf(Tools, id) >= 0;
        }

        /// <summary>현재 레벨에서 다음 레벨로 올리는 비용. 최대면 -1. (K3: Lv2 200G)</summary>
        public static double ToolUpgradeCost(int currentLevel)
        {
            if (currentLevel == 1) return 200;
            if (currentLevel == 2) return 5000;
            return -1;
        }

        /// <summary>구역 n(1부터)의 다음 칸 가격.</summary>
        public static double PlotCost(int fieldNumber, int boughtInField)
        {
            return Math.Round(50 * Math.Pow(10, fieldNumber - 1) * Math.Pow(1.12, boughtInField));
        }

        /// <summary>새 구역 n(2부터)의 가격.</summary>
        public static double FieldCost(int fieldNumber)
        {
            return 20000 * Math.Pow(10, fieldNumber - 2);
        }

        // ---------------------------------------------------------- 창고 (02 3.4절, M2 K6)

        public const int MaxStorageLevel = 20;

        public static int StorageCapacity(int level)
        {
            return StorageBase << Math.Max(0, Math.Min(level, MaxStorageLevel));
        }

        public static double StorageUpgradeCost(int currentLevel)
        {
            if (currentLevel >= MaxStorageLevel) return -1;
            return 400 * Math.Pow(3, currentLevel);
        }

        // ---------------------------------------------------------- 계절

        public static bool InSeason(CropDef crop, int season)
        {
            return crop != null && Array.IndexOf(crop.Seasons, season) >= 0;
        }

        // ---------------------------------------------------------- 시세 (02 3.6절)

        public const double MarketReversion = 0.3;
        public const double MarketVolatility = 0.08;
        public const double MarketMin = 0.5;
        public const double MarketMax = 2.0;
        public const double SalePressure = 0.998;
        public const double OffSeasonMultiplier = 1.3;

        // ---------------------------------------------------------- 장비 (02 3.5절, M3 3.1절)

        public static readonly string[] Equipment =
        {
            "sprinkler", "harvester", "planter", "greenhouse", "shipping-bin", "auto-buyer", "controller", "caretaker",
        };

        /// <summary>구역마다 하나씩 두는 장비.</summary>
        public static readonly string[] FieldEquipment = { "harvester", "planter", "greenhouse" };

        public static bool IsEquipment(string id)
        {
            return Array.IndexOf(Equipment, id) >= 0;
        }

        public static bool IsFieldEquipment(string id)
        {
            return Array.IndexOf(FieldEquipment, id) >= 0;
        }

        /// <summary>한 번 사는 장비의 가격. 레벨형(controller, caretaker)은 따로.</summary>
        public static double EquipmentCost(string id)
        {
            switch (id)
            {
                case "sprinkler": return 500;
                case "harvester": return 3000;
                case "planter": return 5000;
                case "greenhouse": return 50000;
                case "shipping-bin": return 2000;
                case "auto-buyer": return 4000;
            }
            return -1;
        }

        public const int MaxSprinklerLevel = 3;

        public static double SprinklerUpgradeCost(int currentLevel)
        {
            if (currentLevel == 1) return 2000;
            if (currentLevel == 2) return 10000;
            return -1;
        }

        /// <summary>스프링클러 범위 (줄, 칸) 상대 좌표. Lv1 상하좌우 4칸, Lv2 주변 8칸, Lv3 5×5. 구역이 3줄이라 실제로 닿는 칸은 `Farming.Reach`(Lv3 최대 14칸).</summary>
        public static List<int[]> SprinklerOffsets(int level)
        {
            var list = new List<int[]>();
            if (level <= 1)
            {
                list.Add(new[] { -1, 0 });
                list.Add(new[] { 1, 0 });
                list.Add(new[] { 0, -1 });
                list.Add(new[] { 0, 1 });
                return list;
            }
            int r = level == 2 ? 1 : 2;
            for (int dr = -r; dr <= r; dr++)
                for (int dc = -r; dc <= r; dc++)
                    if (dr != 0 || dc != 0) list.Add(new[] { dr, dc });
            return list;
        }

        public const int MaxControllerLevel = 20;

        /// <summary>컨트롤러 다음 레벨 비용. Lv1 1,500G, Lv2 8,000G, Lv3 40,000G, 이후 ×5. 최대면 -1.</summary>
        public static double ControllerCost(int currentLevel)
        {
            if (currentLevel >= MaxControllerLevel) return -1;
            if (currentLevel <= 0) return 1500;
            if (currentLevel == 1) return 8000;
            return 40000 * Math.Pow(5, currentLevel - 2);
        }

        /// <summary>규칙 슬롯 = 레벨 × 2.</summary>
        public static int ControllerSlots(int level)
        {
            return 2 * Math.Max(0, Math.Min(level, MaxControllerLevel));
        }

        /// <summary>규칙 검사 주기(게임 분). 컨트롤러가 없으면 0.</summary>
        public static int ControllerInterval(int level)
        {
            if (level <= 0) return 0;
            if (level == 1) return 60;
            if (level == 2) return 30;
            return 10;
        }

        /// <summary>Caretaker 레벨별 부재 반영 상한(실제 시간). 02 2.1절.</summary>
        public static readonly double[] CaretakerCapHours = { 2, 4, 8, 16, 64 };
        static readonly double[] caretakerCosts = { 1000, 6000, 30000, 150000 };

        public static int MaxCaretakerLevel
        {
            get { return CaretakerCapHours.Length - 1; }
        }

        public static double CaretakerCost(int currentLevel)
        {
            if (currentLevel < 0) currentLevel = 0;
            if (currentLevel >= caretakerCosts.Length) return -1;
            return caretakerCosts[currentLevel];
        }

        public static double CaretakerCap(int level)
        {
            return CaretakerCapHours[Math.Max(0, Math.Min(level, MaxCaretakerLevel))];
        }

        public const int MaxRules = 30;

        // ---------------------------------------------------------- 기록·프레스티지 (02 3.7절, M4)

        /// <summary>체크포인트 첫날. Day 7 × 2^k.</summary>
        public const int FirstCheckpointDay = 7;

        public static bool IsCheckpointDay(long day)
        {
            if (day < FirstCheckpointDay || day % FirstCheckpointDay != 0) return false;
            long k = day / FirstCheckpointDay;
            return (k & (k - 1)) == 0;
        }

        /// <summary>day 다음의 체크포인트 날 (day가 체크포인트면 그다음).</summary>
        public static long NextCheckpointDay(long day)
        {
            long d = FirstCheckpointDay;
            while (d <= day) d *= 2;
            return d;
        }

        /// <summary>프레스티지로 얻는 Fertility = floor(sqrt(최고 총자산 / 100,000)).</summary>
        public const double FertilityUnit = 100000;

        public static long FertilityGain(double peakNetWorth)
        {
            if (double.IsNaN(peakNetWorth) || peakNetWorth < FertilityUnit) return 0;
            double v = Math.Floor(Math.Sqrt(peakNetWorth / FertilityUnit));
            return v > 1e15 ? (long)1e15 : (long)v;
        }

        public static readonly string[] Perks =
        {
            "rich-soil", "market-sense", "night-shift", "rule-memory", "head-start", "old-fields", "seasoned-hands",
        };

        public static bool IsPerk(string id)
        {
            return Array.IndexOf(Perks, id) >= 0;
        }

        /// <summary>영구 보너스 최대 레벨. 0이면 제한 없음.</summary>
        public static int PerkMax(string id)
        {
            switch (id)
            {
                case "rule-memory": return 4;
                case "old-fields": return 12;
                case "seasoned-hands": return 2;
            }
            return 0;
        }

        /// <summary>다음 레벨 비용 = 현재 레벨 + 1 Fertility.</summary>
        public static long PerkCost(int currentLevel)
        {
            return (long)Math.Max(0, currentLevel) + 1;
        }

        /// <summary>다음 판부터 붙는 보너스.</summary>
        public static bool PerkNextRun(string id)
        {
            return id == "head-start" || id == "old-fields" || id == "seasoned-hands";
        }

        public const double RichSoilPerLevel = 0.05;
        public const double MarketSensePerLevel = 0.03;
        public const double NightShiftHours = 2;
        public const double HeadStartGold = 500;
        public const int OldFieldsPlots = 2;

        /// <summary>Rich Soil을 반영한 필요 성장 분 = 올림(분 / (1 + 0.05 × 레벨)).</summary>
        public static int GrowNeed(int minutes, int richSoil)
        {
            if (richSoil <= 0) return minutes;
            return (int)Math.Ceiling(minutes / (1 + RichSoilPerLevel * richSoil) - 1e-9);
        }

        public static readonly string[] Achievements =
        {
            "first-harvest", "gold-1k", "field-2", "first-sprinkler", "first-rule",
            "full-auto", "four-seasons", "net-1m", "new-best", "first-bloom",
        };

        /// <summary>도감 별: 누적 1개 ★, 100개 ★★, 1,000개 ★★★.</summary>
        public static int DexStars(long total)
        {
            if (total >= 1000) return 3;
            if (total >= 100) return 2;
            if (total >= 1) return 1;
            return 0;
        }

        public const int RankRows = 20;
        public const int DailyRows = 14;
        public const double BloomFrameSeconds = 0.7;
        public const int BloomFrames = 4;

        // ---------------------------------------------------------- 계절 달력 (M4 K18)

        /// <summary>day(1부터)가 crop의 계절이면 그 계절이 이어지는 남은 날 수(오늘 포함), 아니면 0.</summary>
        public static int SeasonDaysLeft(CropDef crop, long day)
        {
            int n = 0;
            for (long d = day; d < day + 4 * SeasonDays && InSeason(crop, SeasonOfDay(d)); d++) n++;
            return n;
        }

        /// <summary>day(1부터)에서 crop의 계절이 시작될 때까지 남은 날 수. 지금 제철이면 0.</summary>
        public static int DaysUntilSeason(CropDef crop, long day)
        {
            for (int k = 0; k < 4 * SeasonDays; k++)
                if (InSeason(crop, SeasonOfDay(day + k))) return k;
            return -1;
        }

        public static int SeasonOfDay(long day)
        {
            return (int)(((day - 1) / SeasonDays) % 4);
        }
    }
}
