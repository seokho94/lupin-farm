using System;
using System.Collections.Generic;
using System.Globalization;
using LupinFarm.Core;
using LupinFarm.Data;

namespace LupinFarm.Ui
{
    internal sealed class Candidate
    {
        public string Text;
        public string Desc;

        public Candidate(string text, string desc)
        {
            Text = text;
            Desc = desc;
        }
    }

    /// <summary>자동완성 후보 (FR-CMD-04). 명령어 → 하위 명령 → 작물·도구·구역 이름.</summary>
    internal static class Completion
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<Candidate> Compute(string text, int caret, GameState s, Profile p, out int tokenStart)
        {
            return Compute(text, caret, s, p, 0, out tokenStart);
        }

        public static List<Candidate> Compute(string text, int caret, GameState s, Profile p, int viewField, out int tokenStart)
        {
            var list = new List<Candidate>();
            tokenStart = 0;
            if (string.IsNullOrEmpty(text) || text[0] != '/' || caret != text.Length) return list;
            string lang = p.Settings.Lang;

            int sp = text.LastIndexOf(' ');
            tokenStart = sp + 1;
            string token = text.Substring(tokenStart);
            string[] parts = text.Substring(0, tokenStart).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int index = parts.Length;

            var opts = new List<Candidate>();
            if (index == 0)
            {
                foreach (var name in Commands.Names) opts.Add(new Candidate("/" + name, Localizer.S(lang, "cmd." + name)));
            }
            else
            {
                Args(parts[0].Substring(1).ToLowerInvariant(), index, parts, s, p, lang, viewField, opts);
            }

            foreach (var o in opts)
                if (o.Text.StartsWith(token, StringComparison.OrdinalIgnoreCase)) list.Add(o);
            if (list.Count == 1 && string.Equals(list[0].Text, token, StringComparison.OrdinalIgnoreCase)) list.Clear();
            return list;
        }

        static string L(string lang, string key, params object[] args)
        {
            return Localizer.S(lang, key, args);
        }

        static string Gold(double v)
        {
            return NumberFormat.Gold(v);
        }

        static void Args(string cmd, int index, string[] parts, GameState s, Profile p, string lang, int viewField, List<Candidate> o)
        {
            switch (cmd)
            {
                case "plant":
                    if (index == 1)
                    {
                        bool greenhouse = viewField >= 0 && viewField < s.Fields.Count && s.Fields[viewField].Greenhouse;
                        Crops(s, lang, o, !greenhouse);
                    }
                    else if (index == 2) Ranges(s, lang, o);
                    break;
                case "place":
                    if (index == 1) o.Add(new Candidate("sprinkler", L(lang, "cand.box", s.BoxSprinklers)));
                    break;
                case "records":
                    if (index == 1) o.Add(new Candidate("daily", L(lang, "cand.daily")));
                    break;
                case "perks":
                    if (index == 1) o.Add(new Candidate("buy", L(lang, "cand.perks_buy")));
                    else if (index == 2 && parts.Length > 1 && parts[1].ToLowerInvariant() == "buy")
                    {
                        foreach (var id in Content.Perks)
                        {
                            int lv = p.PerkLevel(id);
                            string name = L(lang, "perk." + id);
                            if (Prestige.AtMax(id, lv)) o.Add(new Candidate(id, L(lang, "cand.perk_max", name, lv)));
                            else o.Add(new Candidate(id, L(lang, "cand.perk", name, lv + 1, Content.PerkCost(lv),
                                Localizer.Resolve(lang, Prestige.Effect(id, lv + 1)))));
                        }
                    }
                    break;
                case "auto":
                    AutoArgs(index, parts, s, lang, o);
                    break;
                case "unlock":
                    if (index == 1)
                    {
                        foreach (var c in Content.Crops)
                        {
                            if (s.IsUnlocked(c.Id)) continue;
                            o.Add(new Candidate(c.Id, Localizer.S(lang, "cand.unlock", Localizer.S(lang, "crop." + c.Id),
                                NumberFormat.Gold(c.UnlockCost), Localizer.Resolve(lang, Engine.CropSeasons(c)), NumberFormat.Gold(c.BasePrice), c.GrowDays)));
                        }
                    }
                    break;
                case "water":
                    if (index == 1) Ranges(s, lang, o);
                    break;
                case "harvest":
                    if (index == 1)
                    {
                        o.Add(new Candidate("ready", Localizer.S(lang, "cand.ready")));
                        Ranges(s, lang, o);
                    }
                    break;
                case "sell":
                    if (index == 1)
                    {
                        if (s.StorageUsed > 0) o.Add(new Candidate("all", Localizer.S(lang, "cand.sell_all", s.StorageUsed)));
                        foreach (var c in Content.Crops)
                        {
                            int n = GameState.Count(s.Storage, c.Id);
                            if (n > 0) o.Add(new Candidate(c.Id, Localizer.S(lang, "cand.stock", Localizer.S(lang, "crop." + c.Id), n, NumberFormat.Gold(Market.Price(s, c.Id)))));
                        }
                    }
                    break;
                case "buy":
                    if (index == 1)
                    {
                        o.Add(new Candidate("seed", Localizer.S(lang, "cand.seed")));
                        Gear(s, lang, o);
                    }
                    else if (index == 2 && parts.Length > 1)
                    {
                        string what = parts[1].ToLowerInvariant();
                        if (what.StartsWith("seed")) Crops(s, lang, o, false);
                        else if (Content.IsFieldEquipment(what))
                        {
                            for (int i = 0; i < s.Fields.Count; i++)
                                o.Add(new Candidate("f" + (i + 1).ToString(Inv), L(lang, "field.label", i + 1)
                                    + (s.Fields[i].Has(what) ? L(lang, "list.sep") + L(lang, "cand.installed") : "")));
                        }
                    }
                    break;
                case "upgrade":
                    if (index == 1)
                    {
                        foreach (var t in Content.Tools)
                        {
                            int lv = s.ToolLevel(t);
                            string desc = lv >= Content.MaxToolLevel
                                ? Localizer.S(lang, "cand.tool_max", Localizer.S(lang, "tool." + t), lv)
                                : Localizer.S(lang, "cand.tool", Localizer.S(lang, "tool." + t), lv, lv + 1, NumberFormat.Gold(Content.ToolUpgradeCost(lv)));
                            o.Add(new Candidate(t, desc));
                        }
                        double sc = Content.StorageUpgradeCost(s.StorageLevel);
                        o.Add(new Candidate("storage", sc < 0
                            ? Localizer.S(lang, "cand.storage_max", s.StorageCapacity)
                            : Localizer.S(lang, "cand.storage", s.StorageCapacity, Content.StorageCapacity(s.StorageLevel + 1), NumberFormat.Gold(sc))));
                        o.Add(new Candidate("sprinkler", L(lang, "cand.sprinkler_up")));
                        Leveled(lang, "controller", s.Farm.Controller, Content.ControllerCost(s.Farm.Controller), o);
                        Leveled(lang, "caretaker", s.Farm.Caretaker, Content.CaretakerCost(s.Farm.Caretaker), o);
                    }
                    break;
                case "expand":
                    if (index == 1)
                    {
                        o.Add(new Candidate("field", Localizer.S(lang, "cand.field", NumberFormat.Gold(Content.FieldCost(s.Fields.Count + 1)))));
                        for (int i = 0; i < s.Fields.Count; i++)
                        {
                            var f = s.Fields[i];
                            if (f.Open < Content.PlotsPerField)
                                o.Add(new Candidate("f" + (i + 1).ToString(Inv), Localizer.S(lang, "cand.plot", Localizer.S(lang, "field.label", i + 1), NumberFormat.Gold(Content.PlotCost(i + 1, f.Bought)))));
                        }
                    }
                    break;
                case "field":
                    if (index == 1)
                    {
                        o.Add(new Candidate("next", Localizer.S(lang, "cand.next")));
                        o.Add(new Candidate("prev", Localizer.S(lang, "cand.prev")));
                        for (int i = 0; i < s.Fields.Count; i++)
                            o.Add(new Candidate((i + 1).ToString(Inv), Localizer.S(lang, "field.label", i + 1)));
                    }
                    break;
                case "config":
                    var st = p.Settings;
                    if (index == 1)
                    {
                        o.Add(new Candidate("lang", st.Lang + " · en | ko"));
                        o.Add(new Candidate("timescale", st.Timescale.ToString(Inv) + " · " + Content.MinTimescale.ToString(Inv) + "-" + Content.MaxTimescale.ToString(Inv)));
                        o.Add(new Candidate("confirm", (st.Confirm ? "on" : "off") + " · on | off"));
                        o.Add(new Candidate("bosskey", st.BossKey));
                    }
                    else if (index == 2 && parts.Length > 1)
                    {
                        string k = parts[1].ToLowerInvariant();
                        if (k == "lang")
                        {
                            o.Add(new Candidate("en", "English"));
                            o.Add(new Candidate("ko", "한국어"));
                        }
                        else if (k == "confirm")
                        {
                            o.Add(new Candidate("on", ""));
                            o.Add(new Candidate("off", ""));
                        }
                    }
                    break;
                case "help":
                    if (index == 1)
                    {
                        foreach (var name in Commands.Names) o.Add(new Candidate(name, Localizer.S(lang, "cmd." + name)));
                        foreach (var id in Commands.Items) o.Add(new Candidate(id, ItemLabel(lang, id)));
                    }
                    break;
            }
        }

        /// <summary>/help 후보의 장비 설명: "물뿌리개 · 농기구", 창고는 이름만 (M4 K24).</summary>
        static string ItemLabel(string lang, string id)
        {
            if (id == "storage") return L(lang, "tool.storage");
            bool tool = Content.IsTool(id);
            string kind = tool ? "tool" : id == "sprinkler" ? "plot" : Content.IsFieldEquipment(id) ? "field" : "farm";
            return L(lang, (tool ? "tool." : "eq.") + id) + " · " + L(lang, "cand.kind." + kind);
        }

        static string EqName(string lang, string id)
        {
            return L(lang, "eq." + id);
        }

        static void Leveled(string lang, string id, int lv, double cost, List<Candidate> o)
        {
            o.Add(new Candidate(id, cost < 0
                ? L(lang, "cand.tool_max", EqName(lang, id), lv)
                : L(lang, "cand.tool", EqName(lang, id), lv, lv + 1, Gold(cost))));
        }

        /// <summary>/buy 장비 후보 (M3).</summary>
        static void Gear(GameState s, string lang, List<Candidate> o)
        {
            o.Add(new Candidate("sprinkler", L(lang, "cand.gear", EqName(lang, "sprinkler"), Gold(Content.EquipmentCost("sprinkler")))));
            foreach (var id in Content.FieldEquipment)
                o.Add(new Candidate(id, L(lang, "cand.gear_field", EqName(lang, id), Gold(Content.EquipmentCost(id)))));
            foreach (var id in new[] { "shipping-bin", "auto-buyer" })
                o.Add(new Candidate(id, s.Farm.Has(id) ? L(lang, "cand.gear_have", EqName(lang, id)) : L(lang, "cand.gear", EqName(lang, id), Gold(Content.EquipmentCost(id)))));
            if (s.Farm.Controller == 0) o.Add(new Candidate("controller", L(lang, "cand.gear", EqName(lang, "controller"), Gold(Content.ControllerCost(0)))));
            if (s.Farm.Caretaker == 0) o.Add(new Candidate("caretaker", L(lang, "cand.gear", EqName(lang, "caretaker"), Gold(Content.CaretakerCost(0)))));
        }

        static void Fields(GameState s, string lang, List<Candidate> o)
        {
            for (int i = 0; i < s.Fields.Count; i++) o.Add(new Candidate("f" + (i + 1).ToString(Inv), L(lang, "field.label", i + 1)));
            o.Add(new Candidate("all", L(lang, "cand.r.all")));
        }

        /// <summary>/auto 하위 명령과 규칙 문법 후보 (02 6.2절).</summary>
        static void AutoArgs(int index, string[] parts, GameState s, string lang, List<Candidate> o)
        {
            if (index == 1)
            {
                foreach (var sub in new[] { "add", "list", "on", "off", "del", "move" }) o.Add(new Candidate(sub, L(lang, "cand.auto." + sub)));
                return;
            }
            string sub1 = parts[1].ToLowerInvariant();
            if (sub1 == "on" || sub1 == "off" || sub1 == "del" || sub1 == "move")
            {
                if (index == 2 || (sub1 == "move" && index == 3))
                    for (int i = 0; i < s.Rules.Count; i++)
                        o.Add(new Candidate((i + 1).ToString(Inv), L(lang, s.Rules[i].On ? "auto.state.on" : "auto.state.off") + " · " + s.Rules[i].Text));
                return;
            }
            if (sub1 != "add") return;

            var w = new List<string>();
            for (int i = 2; i < parts.Length; i++) w.Add(parts[i].ToLowerInvariant());
            int k = w.Count;
            if (k == 0)
            {
                o.Add(new Candidate("when", L(lang, "cand.r.when")));
                return;
            }
            if (w[0] != "when") return;
            int doAt = w.IndexOf("do");
            if (doAt < 0)
            {
                int ck = k - 1;
                if (ck == 0)
                {
                    Fields(s, lang, o);
                    foreach (var c in new[] { "price", "stock", "seed" }) o.Add(new Candidate(c, L(lang, "cand.r." + c)));
                    return;
                }
                string c0 = w[1];
                if (c0 == "all" || RangeParser.FieldNumber(c0) >= 1)
                {
                    if (ck == 1)
                    {
                        o.Add(new Candidate("ready", L(lang, "cand.r.ready")));
                        o.Add(new Candidate("empty", L(lang, "cand.r.empty")));
                    }
                    else if (ck == 2) o.Add(new Candidate("do", L(lang, "cand.r.do")));
                }
                else if (c0 == "price" || c0 == "stock" || c0 == "seed")
                {
                    if (ck == 1)
                    {
                        if (c0 == "stock") o.Add(new Candidate("all", L(lang, "cand.r.stock_all")));
                        Crops(s, lang, o, false);
                    }
                    else if (ck == 2)
                    {
                        foreach (var op in new[] { ">=", "<=", ">", "<", "=" }) o.Add(new Candidate(op, ""));
                    }
                    else if (ck == 4) o.Add(new Candidate("do", L(lang, "cand.r.do")));
                }
                return;
            }
            int ak = k - doAt - 1;
            if (ak == 0)
            {
                foreach (var a in new[] { "harvest", "plant", "sell", "buy" }) o.Add(new Candidate(a, L(lang, "cand.r." + a)));
                return;
            }
            switch (w[doAt + 1])
            {
                case "harvest":
                    if (ak == 1) Fields(s, lang, o);
                    break;
                case "plant":
                    if (ak == 1) Crops(s, lang, o, false);
                    else if (ak == 2) Fields(s, lang, o);
                    break;
                case "sell":
                    if (ak == 1)
                    {
                        o.Add(new Candidate("all", L(lang, "cand.r.sell_all")));
                        Crops(s, lang, o, false);
                    }
                    break;
                case "buy":
                    if (ak == 1) o.Add(new Candidate("seed", L(lang, "cand.seed")));
                    else if (ak == 2) Crops(s, lang, o, false);
                    break;
            }
        }

        /// <summary>해금한 작물. inSeasonOnly면 지금 심을 수 있는 것만 (M2 K7).</summary>
        static void Crops(GameState s, string lang, List<Candidate> o, bool inSeasonOnly)
        {
            int season = GameClock.Season(s.GameMinute);
            int day = GameClock.Day(s.GameMinute);
            foreach (var c in Content.Crops)
            {
                if (!s.IsUnlocked(c.Id)) continue;
                if (inSeasonOnly && !Content.InSeason(c, season)) continue;
                string desc = Localizer.S(lang, "cand.crop", Localizer.S(lang, "crop." + c.Id),
                    NumberFormat.Gold(c.SeedCost), NumberFormat.Gold(Market.Price(s, c.Id)), c.GrowDays, GameState.Count(s.Seeds, c.Id));
                if (!inSeasonOnly)
                {
                    // 씨앗을 살 때 언제 심을 수 있는지 함께 (M4 K19)
                    int left = Content.SeasonDaysLeft(c, day);
                    string when = left > 0
                        ? Localizer.S(lang, "cand.season_now", left)
                        : Localizer.S(lang, "cand.season_in", Localizer.Resolve(lang, new Joined(Engine.CropSeasons(c).Items, "/")), Content.DaysUntilSeason(c, day));
                    desc += Localizer.S(lang, "list.sep") + when;
                }
                o.Add(new Candidate(c.Id, desc));
            }
        }

        static void Ranges(GameState s, string lang, List<Candidate> o)
        {
            o.Add(new Candidate("all", Localizer.S(lang, "cand.all")));
            o.Add(new Candidate("A", Localizer.S(lang, "cand.row", "A")));
            o.Add(new Candidate("B", Localizer.S(lang, "cand.row", "B")));
            o.Add(new Candidate("C", Localizer.S(lang, "cand.row", "C")));
        }
    }
}
