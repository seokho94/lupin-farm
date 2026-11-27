using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    public enum CondKind
    {
        Ready,
        Empty,
        Price,
        Stock,
        Seed,
    }

    public enum ActKind
    {
        Harvest,
        Plant,
        Sell,
        Buy,
    }

    /// <summary>해석한 규칙 한 줄 (02 6.2절, M3 3.2절).</summary>
    public sealed class Rule
    {
        /// <summary>정리된 문장 (소문자, 공백 하나).</summary>
        public string Text;

        public CondKind Cond;

        /// <summary>0 = all, n = 구역 번호, -1 = 구역 조건이 아님.</summary>
        public int CondField = -1;

        /// <summary>stock all이면 null.</summary>
        public string CondCrop;

        public string Op;
        public double Value;

        public ActKind Act;

        /// <summary>harvest, plant의 구역. 0 = all, n = 구역 번호.</summary>
        public int ActField = -1;

        /// <summary>sell all이면 null.</summary>
        public string ActCrop;

        /// <summary>sell: 0이면 전부. buy: 개수.</summary>
        public int ActQty;

        /// <summary>로그 제목용 행동 (구역을 채운 것): "harvest f1".</summary>
        public string ActionText;

        /// <summary>행동을 여는 장비 ID (FR-AUTO-04).</summary>
        public string Equipment
        {
            get
            {
                switch (Act)
                {
                    case ActKind.Harvest: return "harvester";
                    case ActKind.Plant: return "planter";
                    case ActKind.Sell: return "shipping-bin";
                    default: return "auto-buyer";
                }
            }
        }

        public bool MentionsField(int fieldNumber)
        {
            return CondField == 0 || CondField == fieldNumber || ActField == 0 || ActField == fieldNumber;
        }
    }

    /// <summary>규칙이 한 번(또는 연달아 여러 번) 실행된 결과. 같은 규칙은 합친다 (M3 K9).</summary>
    public sealed class RuleRun
    {
        public int Number;
        public Rule Rule;
        public int Times = 1;
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public double Gold;
        public double GoldAfter;
        public int StorageUsed, StorageCap, Left, SeedsLeft;

        /// <summary>못 한 이유. null이면 성공.</summary>
        public string Fail;

        public bool CanMerge(RuleRun o)
        {
            return o != null && Fail == null && o.Fail == null && Number == o.Number && Rule.Text == o.Rule.Text;
        }

        public void Merge(RuleRun o)
        {
            Times += o.Times;
            foreach (var kv in o.Counts) GameState.Add(Counts, kv.Key, kv.Value);
            Gold += o.Gold;
            GoldAfter = o.GoldAfter;
            StorageUsed = o.StorageUsed;
            StorageCap = o.StorageCap;
            Left = o.Left;
            SeedsLeft = o.SeedsLeft;
        }

        public RuleRun Copy()
        {
            var c = new RuleRun();
            c.Number = Number;
            c.Rule = Rule;
            c.Fail = Fail;
            c.Times = 0;
            c.Merge(this);
            return c;
        }
    }

    /// <summary>규칙 해석·검사·실행 (FR-AUTO-01~10).</summary>
    public static class Rules
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Dictionary<string, Rule> cache = new Dictionary<string, Rule>();

        const string ExpCond = "f1, f2, … | all | price | stock | seed";
        const string ExpState = "ready | empty";
        const string ExpOp = ">= | <= | > | < | =";
        const string ExpAct = "harvest | plant | sell | buy";
        const string ExpField = "f1, f2, … | all";

        // ------------------------------------------------------------------ 해석

        /// <summary>공백으로 나누고, 비교 연산자는 붙어 있어도 따로 뗀다. 소문자로 바꾼다.</summary>
        public static List<string> Tokenize(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            string t = text ?? "";
            for (int i = 0; i < t.Length; i++)
            {
                char ch = t[i];
                if (char.IsWhiteSpace(ch))
                {
                    Flush(sb, list);
                    continue;
                }
                if (ch == '<' || ch == '>' || ch == '=')
                {
                    Flush(sb, list);
                    string op = ch.ToString();
                    if (i + 1 < t.Length && t[i + 1] == '=')
                    {
                        op = ch == '=' ? "=" : op + "=";
                        i++;
                    }
                    list.Add(op);
                    continue;
                }
                sb.Append(char.ToLowerInvariant(ch));
            }
            Flush(sb, list);
            return list;
        }

        static void Flush(StringBuilder sb, List<string> list)
        {
            if (sb.Length == 0) return;
            list.Add(sb.ToString());
            sb.Length = 0;
        }

        sealed class Reader
        {
            public List<string> T;
            public int I;
            public Msg Error;

            public string Peek
            {
                get { return I < T.Count ? T[I] : null; }
            }

            public string Next()
            {
                return I < T.Count ? T[I++] : null;
            }

            public bool Fail(string got, object expected)
            {
                if (Error == null) Error = got == null ? new Msg("rule.err.end", expected) : new Msg("rule.err.at", got, expected);
                return false;
            }
        }

        /// <summary>"all" → 0, "f2" → 2, 아니면 -1.</summary>
        static int FieldRef(string t)
        {
            if (t == "all") return 0;
            int n = RangeParser.FieldNumber(t);
            return n >= 1 ? n : -1;
        }

        static string FieldName(int n)
        {
            return n == 0 ? "all" : "f" + n.ToString(Inv);
        }

        static bool ReadCrop(Reader rd, bool allowAll, out string crop)
        {
            crop = null;
            string t = rd.Next();
            if (t == null) return rd.Fail(null, new Msg(allowAll ? "rule.exp.crop_all" : "rule.exp.crop"));
            if (allowAll && t == "all") return true;
            var d = Content.Crop(t);
            if (d == null)
            {
                if (rd.Error == null) rd.Error = new Msg("rule.err.crop", t);
                return false;
            }
            crop = d.Id;
            return true;
        }

        static bool ReadQty(Reader rd, out int qty)
        {
            qty = 0;
            string t = rd.Next();
            if (t == null) return rd.Fail(null, new Msg("rule.exp.qty"));
            if (!int.TryParse(t, NumberStyles.None, Inv, out qty) || qty < 1 || qty > Content.MaxQuantity)
                return rd.Fail(t, new Msg("rule.exp.qty"));
            return true;
        }

        /// <summary>규칙 문장을 해석한다. 틀리면 null과 틀린 위치를 알리는 error (FR-AUTO-09).</summary>
        public static Rule Compile(string text, out Msg error)
        {
            error = null;
            var rd = new Reader();
            rd.T = Tokenize(text);
            var r = new Rule();
            if (!Parse(rd, r))
            {
                error = rd.Error ?? new Msg("rule.err.end", "when");
                return null;
            }
            return r;
        }

        static bool Parse(Reader rd, Rule r)
        {
            string t = rd.Next();
            if (t != "when") return rd.Fail(t, "when");

            var cond = new StringBuilder();
            t = rd.Next();
            if (t == null) return rd.Fail(null, ExpCond);
            int field = FieldRef(t);
            if (field >= 0)
            {
                r.CondField = field;
                string st = rd.Next();
                if (st == "ready") r.Cond = CondKind.Ready;
                else if (st == "empty") r.Cond = CondKind.Empty;
                else return rd.Fail(st, ExpState);
                cond.Append(FieldName(field)).Append(' ').Append(st);
            }
            else if (t == "price" || t == "stock" || t == "seed")
            {
                r.Cond = t == "price" ? CondKind.Price : t == "stock" ? CondKind.Stock : CondKind.Seed;
                string crop;
                if (!ReadCrop(rd, r.Cond == CondKind.Stock, out crop)) return false;
                r.CondCrop = crop;
                string op = rd.Next();
                if (op != ">=" && op != "<=" && op != ">" && op != "<" && op != "=") return rd.Fail(op, ExpOp);
                r.Op = op;
                string num = rd.Next();
                double v;
                if (num == null || !double.TryParse(num, NumberStyles.AllowDecimalPoint, Inv, out v))
                    return rd.Fail(num, new Msg("rule.exp.number"));
                r.Value = v;
                cond.Append(t).Append(' ').Append(crop ?? "all").Append(' ').Append(op).Append(' ').Append(v.ToString("0.##", Inv));
            }
            else
            {
                return rd.Fail(t, ExpCond);
            }

            t = rd.Next();
            if (t != "do") return rd.Fail(t, "do");

            var act = new StringBuilder();
            t = rd.Next();
            switch (t)
            {
                case "harvest":
                    r.Act = ActKind.Harvest;
                    act.Append("harvest");
                    if (rd.Peek != null)
                    {
                        int f = FieldRef(rd.Peek);
                        if (f < 0) return rd.Fail(rd.Peek, ExpField);
                        rd.Next();
                        r.ActField = f;
                        act.Append(' ').Append(FieldName(f));
                    }
                    break;
                case "plant":
                    r.Act = ActKind.Plant;
                    string pc;
                    if (!ReadCrop(rd, false, out pc)) return false;
                    r.ActCrop = pc;
                    act.Append("plant ").Append(pc);
                    if (rd.Peek != null)
                    {
                        int f = FieldRef(rd.Peek);
                        if (f < 0) return rd.Fail(rd.Peek, ExpField);
                        rd.Next();
                        r.ActField = f;
                        act.Append(' ').Append(FieldName(f));
                    }
                    break;
                case "sell":
                    r.Act = ActKind.Sell;
                    string sc;
                    if (!ReadCrop(rd, true, out sc)) return false;
                    r.ActCrop = sc;
                    act.Append("sell ").Append(sc ?? "all");
                    if (sc != null && rd.Peek != null)
                    {
                        int q;
                        if (!ReadQty(rd, out q)) return false;
                        r.ActQty = q;
                        act.Append(' ').Append(q.ToString(Inv));
                    }
                    break;
                case "buy":
                    r.Act = ActKind.Buy;
                    string sd = rd.Next();
                    if (sd != "seed" && sd != "seeds") return rd.Fail(sd, "seed");
                    string bc;
                    if (!ReadCrop(rd, false, out bc)) return false;
                    r.ActCrop = bc;
                    int bq;
                    if (!ReadQty(rd, out bq)) return false;
                    r.ActQty = bq;
                    act.Append("buy seed ").Append(bc).Append(' ').Append(bq.ToString(Inv));
                    break;
                default:
                    return rd.Fail(t, ExpAct);
            }
            if (rd.Peek != null)
            {
                if (rd.Error == null) rd.Error = new Msg("rule.err.extra", rd.Peek);
                return false;
            }

            if ((r.Act == ActKind.Harvest || r.Act == ActKind.Plant) && r.ActField < 0)
            {
                if (r.CondField < 0)
                {
                    if (rd.Error == null) rd.Error = new Msg("rule.err.field", r.Act == ActKind.Harvest ? "harvest" : "plant");
                    return false;
                }
                r.ActField = r.CondField;
            }

            r.Text = "when " + cond + " do " + act;
            switch (r.Act)
            {
                case ActKind.Harvest: r.ActionText = "harvest " + FieldName(r.ActField); break;
                case ActKind.Plant: r.ActionText = "plant " + r.ActCrop + " " + FieldName(r.ActField); break;
                default: r.ActionText = act.ToString(); break;
            }
            return true;
        }

        /// <summary>저장된 문장의 해석 결과 (캐시). 틀린 문장이면 null.</summary>
        public static Rule Get(string text)
        {
            if (text == null) return null;
            Rule r;
            if (cache.TryGetValue(text, out r)) return r;
            Msg e;
            r = Compile(text, out e);
            cache[text] = r;
            return r;
        }

        // ------------------------------------------------------------------ 슬롯

        /// <summary>슬롯 = 컨트롤러 레벨 × 2 + Rule Memory (컨트롤러가 있을 때, M4 K13).</summary>
        public static int Slots(GameState s)
        {
            if (s.Farm.Controller <= 0) return 0;
            return Content.ControllerSlots(s.Farm.Controller) + s.PerkLevel("rule-memory");
        }

        /// <summary>검사 주기(게임 분). 컨트롤러가 없거나 켜진 규칙이 없으면 0 (엔진이 구간을 나누지 않는다).</summary>
        public static int CheckInterval(GameState s)
        {
            if (s.Farm.Controller <= 0) return 0;
            foreach (var r in s.Rules) if (r.On) return Content.ControllerInterval(s.Farm.Controller);
            return 0;
        }

        /// <summary>켜진 규칙 중 위에서부터 슬롯 수만큼 (M3 K7).</summary>
        public static List<int> Active(GameState s)
        {
            var list = new List<int>();
            int slots = Slots(s);
            for (int i = 0; i < s.Rules.Count && list.Count < slots; i++)
                if (s.Rules[i].On) list.Add(i);
            return list;
        }

        public static bool IsActive(GameState s, int index)
        {
            return Active(s).Contains(index);
        }

        /// <summary>이 구역에 관련된 동작 중 규칙 번호 (정보 패널).</summary>
        public static List<int> Related(GameState s, int fieldIndex)
        {
            var list = new List<int>();
            foreach (int i in Active(s))
            {
                var r = Get(s.Rules[i].Text);
                if (r != null && r.MentionsField(fieldIndex + 1)) list.Add(i + 1);
            }
            return list;
        }

        /// <summary>동작 중인 수확 규칙이 이 구역을 거둬 가는지 (그러면 Ready 알림을 생략한다, M3 K20).</summary>
        public static bool AutoHarvests(GameState s, int fieldIndex)
        {
            if (fieldIndex < 0 || fieldIndex >= s.Fields.Count || !s.Fields[fieldIndex].Harvester) return false;
            foreach (int i in Active(s))
            {
                var r = Get(s.Rules[i].Text);
                if (r != null && r.Act == ActKind.Harvest && (r.ActField == 0 || r.ActField == fieldIndex + 1)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 실행

        static bool Compare(double a, string op, double b)
        {
            switch (op)
            {
                case ">=": return a >= b;
                case "<=": return a <= b;
                case ">": return a > b;
                case "<": return a < b;
                default: return Math.Abs(a - b) < 1e-9;
            }
        }

        static List<int> FieldsOf(GameState s, int fieldRef)
        {
            var list = new List<int>();
            if (fieldRef == 0)
            {
                for (int i = 0; i < s.Fields.Count; i++) list.Add(i);
            }
            else if (fieldRef >= 1 && fieldRef <= s.Fields.Count)
            {
                list.Add(fieldRef - 1);
            }
            return list;
        }

        public static bool CondTrue(GameState s, Rule r)
        {
            switch (r.Cond)
            {
                case CondKind.Ready:
                    foreach (int fi in FieldsOf(s, r.CondField))
                        if (Farming.HasRipe(s.Fields[fi])) return true;
                    return false;
                case CondKind.Empty:
                    foreach (int fi in FieldsOf(s, r.CondField))
                        if (Farming.HasEmpty(s.Fields[fi])) return true;
                    return false;
                case CondKind.Price:
                    return Compare(Market.Price(s, r.CondCrop), r.Op, r.Value);
                case CondKind.Stock:
                    return Compare(r.CondCrop == null ? s.StorageUsed : GameState.Count(s.Storage, r.CondCrop), r.Op, r.Value);
                default:
                    return Compare(GameState.Count(s.Seeds, r.CondCrop), r.Op, r.Value);
            }
        }

        /// <summary>장비·구역이 갖춰졌는지. 아니면 "needs" 또는 "no_field".</summary>
        public static string Missing(GameState s, Rule r)
        {
            switch (r.Act)
            {
                case ActKind.Harvest:
                case ActKind.Plant:
                    if (r.ActField > s.Fields.Count) return "no_field";
                    foreach (int fi in FieldsOf(s, r.ActField))
                        if (s.Fields[fi].Has(r.Equipment)) return null;
                    return "needs";
                default:
                    return s.Farm.Has(r.Equipment) ? null : "needs";
            }
        }

        /// <summary>검사 시각마다: 동작 중 규칙을 위에서부터 검사하고, 조건이 맞으면 행동 1회 (FR-AUTO-05, M3 K19). 농장을 바꾼 규칙이 있으면 true.</summary>
        public static bool Run(GameState s, AdvanceReport rep)
        {
            bool changed = false;
            foreach (int i in Active(s))
            {
                var rs = s.Rules[i];
                var r = Get(rs.Text);
                if (r == null || !CondTrue(s, r)) continue;
                string fail;
                var run = Act(s, r, out fail);
                if (run != null)
                {
                    rs.Runs++;
                    rs.Fail = null;
                    run.Number = i + 1;
                    run.Rule = r;
                    rep.AddRun(run);
                    changed = true;
                    MarkAuto(s, r.Act);
                }
                else if (fail != null && fail != rs.Fail)
                {
                    rs.Fail = fail;
                    var f = new RuleRun();
                    f.Number = i + 1;
                    f.Rule = r;
                    f.Fail = fail;
                    rep.AddRun(f);
                }
            }
            return changed;
        }

        /// <summary>full-auto 업적: 같은 게임 날 안에 plant, harvest, sell 규칙이 모두 성공 (M4 K15).</summary>
        static void MarkAuto(GameState s, ActKind act)
        {
            int day = GameClock.Day(s.GameMinute);
            if (s.AutoDay != day)
            {
                s.AutoDay = day;
                s.AutoMask = 0;
            }
            s.AutoMask |= 1 << (int)act;
            int all = (1 << (int)ActKind.Plant) | (1 << (int)ActKind.Harvest) | (1 << (int)ActKind.Sell);
            if ((s.AutoMask & all) == all) s.FullAuto = true;
        }

        /// <summary>행동을 한 번 한다. 바뀐 것이 없으면 null (fail에 이유, 할 일이 없었으면 fail도 null).</summary>
        static RuleRun Act(GameState s, Rule r, out string fail)
        {
            fail = Missing(s, r);
            if (fail != null) return null;
            var run = new RuleRun();
            switch (r.Act)
            {
                case ActKind.Harvest:
                {
                    int left = 0;
                    foreach (int fi in FieldsOf(s, r.ActField))
                    {
                        var f = s.Fields[fi];
                        if (!f.Harvester) continue;
                        var res = Farming.Harvest(s, fi, Farming.RipeCells(f));
                        foreach (var kv in res.Got) GameState.Add(run.Counts, kv.Key, kv.Value);
                        left += res.Left;
                    }
                    if (run.Counts.Count == 0)
                    {
                        if (left > 0) fail = "storage_full";
                        return null;
                    }
                    run.Left = left;
                    run.StorageUsed = s.StorageUsed;
                    run.StorageCap = s.StorageCapacity;
                    return run;
                }
                case ActKind.Plant:
                {
                    var crop = Content.Crop(r.ActCrop);
                    if (!s.IsUnlocked(crop.Id))
                    {
                        fail = "locked";
                        return null;
                    }
                    int planted = 0, noSeed = 0;
                    bool season = false;
                    foreach (int fi in FieldsOf(s, r.ActField))
                    {
                        var f = s.Fields[fi];
                        if (!f.Planter) continue;
                        var empty = Farming.EmptyCells(f);
                        if (empty.Count == 0) continue;
                        if (!Farming.SeasonOk(s, fi, crop))
                        {
                            season = true;
                            continue;
                        }
                        var res = Farming.Plant(s, fi, crop, empty);
                        planted += res.Planted;
                        noSeed += res.NoSeed;
                    }
                    if (planted == 0)
                    {
                        if (noSeed > 0) fail = "no_seed";
                        else if (season) fail = "season";
                        return null;
                    }
                    run.Counts[crop.Id] = planted;
                    run.SeedsLeft = GameState.Count(s.Seeds, crop.Id);
                    return run;
                }
                case ActKind.Sell:
                {
                    if (r.ActCrop == null)
                    {
                        if (s.StorageUsed == 0) return null;
                        run.Gold = Farming.SellAll(s, run.Counts);
                    }
                    else
                    {
                        int have = GameState.Count(s.Storage, r.ActCrop);
                        if (have == 0) return null;
                        int qty = r.ActQty > 0 ? Math.Min(r.ActQty, have) : have;
                        run.Gold = Farming.Sell(s, r.ActCrop, qty).Total;
                        run.Counts[r.ActCrop] = qty;
                    }
                    run.GoldAfter = s.Gold;
                    return run;
                }
                default:
                {
                    var crop = Content.Crop(r.ActCrop);
                    if (!s.IsUnlocked(crop.Id))
                    {
                        fail = "locked";
                        return null;
                    }
                    double cost = crop.SeedCost * r.ActQty;
                    if (s.Gold < cost)
                    {
                        fail = "no_gold";
                        return null;
                    }
                    s.Gold -= cost;
                    GameState.Add(s.Seeds, crop.Id, r.ActQty);
                    run.Counts[crop.Id] = r.ActQty;
                    run.Gold = cost;
                    run.GoldAfter = s.Gold;
                    return run;
                }
            }
        }

        // ------------------------------------------------------------------ 문구

        static Msg M(string key, params object[] args)
        {
            return new Msg(key, args);
        }

        public static Msg EquipmentName(string id)
        {
            return M("eq." + id);
        }

        /// <summary>못 한 이유를 다음 할 일과 함께 (로그, 목록 공용).</summary>
        public static Msg FailMsg(Rule r, string fail)
        {
            switch (fail)
            {
                case "needs":
                    if (r.Act == ActKind.Harvest || r.Act == ActKind.Plant)
                    {
                        if (r.ActField == 0) return M("auto.fail.needs_any", EquipmentName(r.Equipment), r.Equipment);
                        return M("auto.fail.needs_field", EquipmentName(r.Equipment), FieldName(r.ActField), r.Equipment);
                    }
                    return M("auto.fail.needs_farm", EquipmentName(r.Equipment), r.Equipment, NumberFormat.Gold(Content.EquipmentCost(r.Equipment)));
                case "no_field":
                    return M("auto.fail.no_field", FieldName(r.ActField));
                case "locked":
                    return M("auto.fail.locked", M("crop." + r.ActCrop), r.ActCrop);
                case "season":
                    return M("auto.fail.season", M("crop." + r.ActCrop), Engine.CropSeasons(Content.Crop(r.ActCrop)));
                case "no_seed":
                    return M("auto.fail.no_seed", M("crop." + r.ActCrop), r.ActCrop);
                case "storage_full":
                    return M("auto.fail.storage_full");
                case "no_gold":
                    return M("auto.fail.no_gold", NumberFormat.Gold(Content.Crop(r.ActCrop).SeedCost * r.ActQty));
            }
            return M("auto.fail.unknown", fail);
        }

        /// <summary>실행 결과 로그. 성공은 주황 Auto, 못 한 것은 빨강 (M3 K8).</summary>
        public static LogEntry RunLog(RuleRun run)
        {
            var title = run.Times > 1
                ? M("t.auto_run_n", run.Number, run.Rule.ActionText, run.Times)
                : M("t.auto_run", run.Number, run.Rule.ActionText);
            if (run.Fail != null) return LogEntry.Make(LogKind.Error, title, FailMsg(run.Rule, run.Fail));
            var lines = new List<object>();
            string G0 = NumberFormat.Gold(run.Gold), G1 = NumberFormat.Gold(run.GoldAfter);
            switch (run.Rule.Act)
            {
                case ActKind.Harvest:
                    lines.Add(M("harvest.done", Engine.CropList(run.Counts), run.StorageUsed, run.StorageCap));
                    if (run.Left > 0) lines.Add(M("harvest.full", run.Left));
                    break;
                case ActKind.Plant:
                    lines.Add(M("plant.done", Engine.CropList(run.Counts), run.SeedsLeft));
                    break;
                case ActKind.Sell:
                    lines.Add(M("sell.done", Engine.CropList(run.Counts), G0, G1));
                    break;
                default:
                    lines.Add(M("auto.bought", Engine.CropList(run.Counts), G0, G1));
                    break;
            }
            return LogEntry.Make(LogKind.Auto, title, lines.ToArray());
        }
    }
}
