using System;
using System.Collections.Generic;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    public sealed class PlotState
    {
        /// <summary>작물 ID. null이면 빈 칸.</summary>
        public string Crop { get; set; }

        /// <summary>물이 있는 동안 쌓인 성장 게임 분.</summary>
        public int Growth { get; set; }

        /// <summary>이 게임 분까지 물이 있다.</summary>
        public long WaterUntil { get; set; }

        /// <summary>재수확 작물의 수확 횟수.</summary>
        public int Harvests { get; set; }

        /// <summary>설치된 스프링클러 레벨 (M3).</summary>
        public int Sprinkler { get; set; }

        /// <summary>다 자라는 데 필요한 성장 분. 심을 때 Rich Soil로 정한다. 0이면 작물 기본값 (M4 K13).</summary>
        public int Need { get; set; }
    }

    public sealed class FieldState
    {
        public int Open { get; set; }
        public int Bought { get; set; }

        /// <summary>구역 장비 (M3).</summary>
        public bool Harvester { get; set; }
        public bool Planter { get; set; }
        public bool Greenhouse { get; set; }

        public List<PlotState> Plots { get; set; }

        public bool Has(string equipment)
        {
            switch (equipment)
            {
                case "harvester": return Harvester;
                case "planter": return Planter;
                case "greenhouse": return Greenhouse;
            }
            return false;
        }

        public void Set(string equipment, bool on)
        {
            switch (equipment)
            {
                case "harvester": Harvester = on; break;
                case "planter": Planter = on; break;
                case "greenhouse": Greenhouse = on; break;
            }
        }

        public int SprinklerCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Open && i < Plots.Count; i++) if (Plots[i].Sprinkler > 0) n++;
                return n;
            }
        }

        public static FieldState Create(int open)
        {
            var f = new FieldState();
            f.Open = open;
            f.Plots = new List<PlotState>();
            for (int i = 0; i < Content.PlotsPerField; i++) f.Plots.Add(new PlotState());
            return f;
        }

        public void Normalize()
        {
            if (Plots == null) Plots = new List<PlotState>();
            while (Plots.Count < Content.PlotsPerField) Plots.Add(new PlotState());
            for (int i = 0; i < Plots.Count; i++) if (Plots[i] == null) Plots[i] = new PlotState();
            if (Open < 0) Open = 0;
            if (Open > Content.PlotsPerField) Open = Content.PlotsPerField;
            foreach (var p in Plots)
            {
                if (p.Sprinkler < 0) p.Sprinkler = 0;
                if (p.Sprinkler > Content.MaxSprinklerLevel) p.Sprinkler = Content.MaxSprinklerLevel;
            }
        }
    }

    /// <summary>농장 전체에 하나인 장비 (M3).</summary>
    public sealed class FarmEquipment
    {
        public bool ShippingBin { get; set; }
        public bool AutoBuyer { get; set; }
        public int Controller { get; set; }
        public int Caretaker { get; set; }

        public bool Has(string equipment)
        {
            switch (equipment)
            {
                case "shipping-bin": return ShippingBin;
                case "auto-buyer": return AutoBuyer;
                case "controller": return Controller > 0;
                case "caretaker": return Caretaker > 0;
            }
            return false;
        }
    }

    /// <summary>자동화 규칙 하나 (M3). Text는 정리된 문장, Fail은 마지막으로 못 한 이유(같은 이유는 한 번만 알린다, M3 K8).</summary>
    public sealed class RuleState
    {
        public string Text { get; set; }
        public bool On { get; set; }
        public int Runs { get; set; }
        public string Fail { get; set; }
    }

    /// <summary>작물 하나의 시세 계수 (02 3.6절).</summary>
    public sealed class MarketEntry
    {
        public double F { get; set; }
        public double Yesterday { get; set; }
    }

    /// <summary>현재 판 (save.json). 02 4절.</summary>
    public sealed class GameState
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; }
        public int Run { get; set; }
        public string SavedAtUtc { get; set; }
        public long GameMinute { get; set; }
        public double Gold { get; set; }
        public double Invested { get; set; }
        public long Rng { get; set; }
        public Dictionary<string, int> Tools { get; set; }
        public int StorageLevel { get; set; }
        public Dictionary<string, int> Storage { get; set; }
        public Dictionary<string, int> Seeds { get; set; }
        public List<string> Unlocked { get; set; }
        public List<FieldState> Fields { get; set; }
        public Dictionary<string, MarketEntry> Market { get; set; }

        /// <summary>설치하지 않은 스프링클러. 키 "sprinkler1"~"sprinkler3" = 레벨별 개수 (M3).</summary>
        public Dictionary<string, int> EquipmentBox { get; set; }
        public FarmEquipment Farm { get; set; }
        public List<RuleState> Rules { get; set; }

        /// <summary>이번 판 최고 총자산과 그때의 게임 날짜 (M4 K2).</summary>
        public double PeakNetWorth { get; set; }
        public int PeakDay { get; set; }

        /// <summary>체크포인트 날("7", "14", …) → 그날 끝의 총자산 (M4 K3).</summary>
        public Dictionary<string, double> Checkpoints { get; set; }

        /// <summary>이번 판 판매 수입 합계 (일별 기록용, M4 K4).</summary>
        public double Earned { get; set; }

        /// <summary>이번 판 작물별 수확 수 (도감용, M4 K14).</summary>
        public Dictionary<string, int> Harvested { get; set; }

        /// <summary>영구 보너스 레벨 사본. 원본은 Profile.Perks이고 화면이 불러올 때·살 때 맞춘다 (M4 K13).</summary>
        public Dictionary<string, int> Perks { get; set; }

        /// <summary>full-auto 업적 판정: 규칙이 성공한 게임 날과 그날 성공한 행동 비트 (M4 K15).</summary>
        public int AutoDay { get; set; }
        public int AutoMask { get; set; }
        public bool FullAuto { get; set; }

        public int PerkLevel(string id)
        {
            return Count(Perks, id);
        }

        public static string BoxKey(int sprinklerLevel)
        {
            return "sprinkler" + sprinklerLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public int BoxSprinklers
        {
            get
            {
                int n = 0;
                for (int lv = 1; lv <= Content.MaxSprinklerLevel; lv++) n += Count(EquipmentBox, BoxKey(lv));
                return n;
            }
        }

        public static GameState NewRun(int run, long seed)
        {
            return NewRun(run, seed, null);
        }

        /// <summary>새 판. 영구 보너스(head-start, old-fields, seasoned-hands)가 시작 상태에 붙는다 (M4 K13).</summary>
        public static GameState NewRun(int run, long seed, Dictionary<string, int> perks)
        {
            var s = new GameState();
            s.Version = CurrentVersion;
            s.Run = run;
            s.GameMinute = 0;
            s.Perks = perks == null ? new Dictionary<string, int>() : new Dictionary<string, int>(perks);
            s.Gold = Content.StartGold + Content.HeadStartGold * s.PerkLevel("head-start");
            s.Rng = seed == 0 ? Core.Rng.DefaultSeed : seed;
            s.Tools = new Dictionary<string, int>();
            int tool = Math.Min(Content.MaxToolLevel, 1 + s.PerkLevel("seasoned-hands"));
            foreach (var t in Content.Tools) s.Tools[t] = tool;
            s.Storage = new Dictionary<string, int>();
            s.Seeds = new Dictionary<string, int>();
            s.Unlocked = new List<string>();
            foreach (var c in Content.Crops) if (c.IsFree) s.Unlocked.Add(c.Id);
            s.Fields = new List<FieldState>();
            int open = Math.Min(Content.PlotsPerField, Content.StartOpenPlots + Content.OldFieldsPlots * s.PerkLevel("old-fields"));
            s.Fields.Add(FieldState.Create(open));
            s.Normalize();
            return s;
        }

        /// <summary>작물의 시세 항목. 없으면 만든다.</summary>
        public MarketEntry MarketOf(string crop)
        {
            if (Market == null) Market = new Dictionary<string, MarketEntry>();
            MarketEntry e;
            if (!Market.TryGetValue(crop, out e) || e == null)
            {
                e = new MarketEntry();
                e.F = 1;
                e.Yesterday = 1;
                Market[crop] = e;
            }
            return e;
        }

        /// <summary>불러온 뒤 빠진 값을 채운다.</summary>
        public void Normalize()
        {
            if (Version <= 0) Version = CurrentVersion;
            if (Run <= 0) Run = 1;
            if (Tools == null) Tools = new Dictionary<string, int>();
            foreach (var t in Content.Tools)
            {
                int lv;
                if (!Tools.TryGetValue(t, out lv) || lv < 1) Tools[t] = 1;
                else if (lv > Content.MaxToolLevel) Tools[t] = Content.MaxToolLevel;
            }
            if (Storage == null) Storage = new Dictionary<string, int>();
            if (Seeds == null) Seeds = new Dictionary<string, int>();
            if (Unlocked == null) Unlocked = new List<string>();
            foreach (var c in Content.Crops) if (c.IsFree && !Unlocked.Contains(c.Id)) Unlocked.Add(c.Id);
            if (Fields == null || Fields.Count == 0)
            {
                Fields = new List<FieldState>();
                Fields.Add(FieldState.Create(Content.StartOpenPlots));
            }
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i] == null) Fields[i] = FieldState.Create(Content.StartOpenPlots);
                Fields[i].Normalize();
            }
            if (Rng == 0) Rng = Core.Rng.DefaultSeed;
            if (StorageLevel < 0) StorageLevel = 0;
            if (StorageLevel > Content.MaxStorageLevel) StorageLevel = Content.MaxStorageLevel;
            foreach (var c in Content.Crops)
            {
                var e = MarketOf(c.Id);
                if (e.F <= 0 || double.IsNaN(e.F)) e.F = 1;
                if (e.Yesterday <= 0 || double.IsNaN(e.Yesterday)) e.Yesterday = e.F;
            }
            if (EquipmentBox == null) EquipmentBox = new Dictionary<string, int>();
            if (Farm == null) Farm = new FarmEquipment();
            if (Farm.Controller < 0) Farm.Controller = 0;
            if (Farm.Controller > Content.MaxControllerLevel) Farm.Controller = Content.MaxControllerLevel;
            if (Farm.Caretaker < 0) Farm.Caretaker = 0;
            if (Farm.Caretaker > Content.MaxCaretakerLevel) Farm.Caretaker = Content.MaxCaretakerLevel;
            if (Rules == null) Rules = new List<RuleState>();
            Rules.RemoveAll(r => r == null || string.IsNullOrEmpty(r.Text));
            if (Checkpoints == null) Checkpoints = new Dictionary<string, double>();
            if (Harvested == null) Harvested = new Dictionary<string, int>();
            if (Perks == null) Perks = new Dictionary<string, int>();
            if (double.IsNaN(PeakNetWorth) || PeakNetWorth < 0) PeakNetWorth = 0;
            if (double.IsNaN(Earned) || Earned < 0) Earned = 0;
        }

        public int ToolLevel(string tool)
        {
            int v;
            return Tools != null && Tools.TryGetValue(tool, out v) ? v : 1;
        }

        public int StorageCapacity
        {
            get { return Content.StorageCapacity(StorageLevel); }
        }

        public int StorageUsed
        {
            get
            {
                int n = 0;
                foreach (var kv in Storage) n += kv.Value;
                return n;
            }
        }

        public int SeedTotal
        {
            get
            {
                int n = 0;
                foreach (var kv in Seeds) n += kv.Value;
                return n;
            }
        }

        public bool IsUnlocked(string crop)
        {
            return Unlocked != null && Unlocked.Contains(crop);
        }

        public static int Count(Dictionary<string, int> d, string key)
        {
            int v;
            return d != null && key != null && d.TryGetValue(key, out v) ? v : 0;
        }

        public static void Add(Dictionary<string, int> d, string key, int n)
        {
            int v = Count(d, key) + n;
            if (v <= 0) d.Remove(key);
            else d[key] = v;
        }
    }

    public sealed class Settings
    {
        public string Lang { get; set; }
        public int Timescale { get; set; }
        public bool Confirm { get; set; }
        public string BossKey { get; set; }

        public static Settings Default()
        {
            var s = new Settings();
            s.Lang = "en";
            s.Timescale = Content.DefaultTimescale;
            s.Confirm = true;
            s.BossKey = "Ctrl+Alt+H";
            return s;
        }

        public void Normalize()
        {
            if (Lang != "en" && Lang != "ko") Lang = "en";
            if (Timescale < Content.MinTimescale || Timescale > Content.MaxTimescale) Timescale = Content.DefaultTimescale;
            if (string.IsNullOrEmpty(BossKey)) BossKey = "Ctrl+Alt+H";
        }
    }

    /// <summary>판 하나의 기록 (02 5절). 현재 판도 계속 갱신한다 (M4 K5).</summary>
    public sealed class RunRecord
    {
        public int Run { get; set; }
        public string StartedAt { get; set; }
        public string EndedAt { get; set; }
        public double Peak { get; set; }
        public int PeakDay { get; set; }
        public int LastDay { get; set; }
        public long Fertility { get; set; }
        public Dictionary<string, double> Checkpoints { get; set; }
        public Dictionary<string, int> Harvested { get; set; }

        public void Normalize()
        {
            if (Checkpoints == null) Checkpoints = new Dictionary<string, double>();
            if (Harvested == null) Harvested = new Dictionary<string, int>();
            if (double.IsNaN(Peak) || Peak < 0) Peak = 0;
        }
    }

    /// <summary>실제 날짜·판 하나의 기록 (M4 K4). Earned = 판의 판매 합계 − EarnedBase.</summary>
    public sealed class DailyRecord
    {
        public string Date { get; set; }
        public int Run { get; set; }
        public double NetWorth { get; set; }
        public double Earned { get; set; }
        public double EarnedBase { get; set; }
    }

    /// <summary>작물 도감 한 칸 (M4 K14). Total은 모든 판의 누적 수확 수.</summary>
    public sealed class DexEntry
    {
        public string First { get; set; }
        public int FirstRun { get; set; }
        public long Total { get; set; }
    }

    /// <summary>판을 넘어 남는 것 (profile.json). 설정, 이전 명령, 기록·보너스·도감·업적 (M4).</summary>
    public sealed class Profile
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; }
        public int CurrentRun { get; set; }
        public Settings Settings { get; set; }
        public List<string> History { get; set; }

        public long Fertility { get; set; }
        public Dictionary<string, int> Perks { get; set; }
        public List<RunRecord> Runs { get; set; }
        public List<DailyRecord> Daily { get; set; }
        public Dictionary<string, DexEntry> Dex { get; set; }

        /// <summary>업적 ID → 달성 날짜.</summary>
        public Dictionary<string, string> Achievements { get; set; }

        public static Profile New()
        {
            var p = new Profile();
            p.Version = CurrentVersion;
            p.CurrentRun = 1;
            p.Settings = Settings.Default();
            p.History = new List<string>();
            p.Normalize();
            return p;
        }

        public void Normalize()
        {
            if (Version <= 0) Version = CurrentVersion;
            if (CurrentRun <= 0) CurrentRun = 1;
            if (Settings == null) Settings = Settings.Default();
            Settings.Normalize();
            if (History == null) History = new List<string>();
            while (History.Count > Content.HistoryMax) History.RemoveAt(0);
            if (Fertility < 0) Fertility = 0;
            if (Perks == null) Perks = new Dictionary<string, int>();
            foreach (var id in new List<string>(Perks.Keys))
            {
                int max = Content.PerkMax(id);
                if (Perks[id] < 0) Perks[id] = 0;
                else if (max > 0 && Perks[id] > max) Perks[id] = max;
            }
            if (Runs == null) Runs = new List<RunRecord>();
            Runs.RemoveAll(r => r == null || r.Run <= 0);
            foreach (var r in Runs) r.Normalize();
            if (Daily == null) Daily = new List<DailyRecord>();
            Daily.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Date));
            if (Dex == null) Dex = new Dictionary<string, DexEntry>();
            foreach (var k in new List<string>(Dex.Keys)) if (Dex[k] == null) Dex.Remove(k);
            if (Achievements == null) Achievements = new Dictionary<string, string>();
        }

        public int PerkLevel(string id)
        {
            return GameState.Count(Perks, id);
        }

        public RunRecord RunOf(int run)
        {
            foreach (var r in Runs) if (r.Run == run) return r;
            return null;
        }

        public void AddHistory(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (History.Count > 0 && History[History.Count - 1] == line) return;
            History.Add(line);
            while (History.Count > Content.HistoryMax) History.RemoveAt(0);
        }
    }
}
