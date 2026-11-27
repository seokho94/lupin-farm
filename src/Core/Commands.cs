using System;
using System.Collections.Generic;
using System.Globalization;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    public sealed class CommandContext
    {
        public GameState State;
        public Profile Profile;
        public int ViewField;
    }

    /// <summary>번호 선택지 요청. 고르면 OnSelect가 다음 결과를 만든다.</summary>
    public sealed class PromptRequest
    {
        public Msg Question;

        /// <summary>질문 아래에 보일 설명 줄 (프레스티지 상자, M4 K8).</summary>
        public readonly List<object> Details = new List<object>();
        public readonly List<Msg> Options = new List<Msg>();
        public int Default;
        public Func<int, CommandResult> OnSelect;
    }

    public enum UiAction
    {
        None,
        ToggleHide,
        ClearLog,
        Exit,
        SettingsChanged,
        BossKeyChanged,
        Reset,
        Prestige,

        /// <summary>/save: 저장 폴더·내보낸 파일 안내 (M5 K6). 파일은 화면(App)이 다룬다.</summary>
        SaveInfo,

        /// <summary>/save export [경로] (M5 K6). 경로는 CommandResult.Path.</summary>
        SaveExport,

        /// <summary>/save import &lt;경로&gt; (M5 K7).</summary>
        SaveImport,
    }

    /// <summary>스프링클러 설치·업그레이드 미리 보기 (M5 K9): 구역, 칸, 놓을(올린) 레벨, 범위(자기 칸 포함).</summary>
    public sealed class SprinklerPreview
    {
        public int Field;
        public int Cell;
        public int Level;
        public bool[] Range;
    }

    public sealed class CommandResult
    {
        public readonly List<LogEntry> Logs = new List<LogEntry>();
        public PromptRequest Prompt;
        public UiAction Action;
        public int ViewField = -1;
        public bool Changed;
        public string PreviousBossKey;

        /// <summary>/save export·import의 경로 인자 (공백 포함, 그대로). 없으면 "".</summary>
        public string Path = "";
    }

    /// <summary>슬래시 명령 실행. 상태를 바꾸고 로그 이벤트(문구 키)를 돌려준다.</summary>
    public static class Commands
    {
        public static readonly string[] Names =
        {
            "help", "status", "plant", "water", "harvest", "remove", "growing", "crops", "sell", "market", "shop", "buy", "unlock",
            "inv", "upgrade", "place", "auto", "expand", "field", "map", "records", "rank", "dex", "prestige", "perks",
            "hide", "clear", "config", "save", "reset", "exit",
        };

        /// <summary>명령 설명. 가격은 문구에 쓰지 않고 Content.cs에서 넣는다 (M5 밸런스로 값이 바뀌어도 맞게).</summary>
        static Msg HelpText(string name)
        {
            if (name == "buy")
                return M("help.buy", G(Content.EquipmentCost("sprinkler")), G(Content.EquipmentCost("harvester")), G(Content.EquipmentCost("planter")),
                    G(Content.EquipmentCost("greenhouse")), G(Content.EquipmentCost("shipping-bin")), G(Content.EquipmentCost("auto-buyer")));
            return M("help." + name);
        }

        /// <summary>/help로 설명을 볼 수 있는 농기구·창고·장비 (M4 K24). 명령 이름과 겹치지 않는다.</summary>
        public static readonly string[] Items =
        {
            "can", "bag", "sickle", "storage", "sprinkler", "harvester", "planter", "greenhouse",
            "shipping-bin", "auto-buyer", "controller", "caretaker",
        };

        /// <summary>이후 마일스톤 명령. 치면 "이후 업데이트에서 추가"로 알린다. M4에서 모두 들어갔다.</summary>
        public static readonly string[] Later = { };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static CommandResult Execute(CommandContext c, string line)
        {
            var r = new CommandResult();
            // 한글 자판으로 친 명령도 같은 키의 영문으로 읽는다 (M5 K1)
            string trimmed = HangulKeys.FixLine((line ?? "").Trim());
            if (trimmed.Length == 0) return r;
            if (trimmed[0] != '/') return Fail(r, M("t.unknown"), M("err.not_cmd"));

            string[] a = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = a[0].Substring(1).ToLowerInvariant();
            if (c.ViewField < 0 || c.ViewField >= c.State.Fields.Count) c.ViewField = 0;

            switch (cmd)
            {
                case "help": return Help(c, a);
                case "status": return Status(c);
                case "plant": return Plant(c, a);
                case "water": return Water(c, a);
                case "harvest": return Harvest(c, a);
                case "remove": return Remove(c, a);
                case "sell": return Sell(c, a);
                case "market": return MarketTable(c);
                case "shop": return Shop(c);
                case "buy": return Buy(c, a);
                case "unlock": return Unlock(c, a);
                case "inv": return Inventory(c);
                case "upgrade": return Upgrade(c, a);
                case "place": return Place(c, a);
                case "auto": return Auto(c, a);
                case "expand": return Expand(c, a);
                case "field": return Field(c, a);
                case "map": return Map(c);
                case "growing": return Growing(c, a);
                case "crops": return CropsTable(c);
                case "records": return RecordsCmd(c, a);
                case "rank": return Rank(c);
                case "dex": return DexCmd(c);
                case "prestige": return PrestigeCmd(c);
                case "perks": return Perks(c, a);
                case "hide": r.Action = UiAction.ToggleHide; return r;
                case "clear": r.Action = UiAction.ClearLog; return r;
                case "config": return Config(c, a);
                case "save": return SaveCmd(trimmed, a);
                case "reset": return Reset();
                case "exit": r.Action = UiAction.Exit; return r;
            }

            if (Array.IndexOf(Later, cmd) >= 0) return Fail(r, M("t.cmd", "/" + cmd), M("err.not_yet", "/" + cmd));
            return Fail(r, M("t.unknown"), M("err.unknown_cmd", "/" + cmd));
        }

        /// <summary>'?' 단축키 목록.</summary>
        public static LogEntry Shortcuts(Profile p)
        {
            return LogEntry.Make(LogKind.Info, M("t.shortcuts"),
                M("keys.1"), M("keys.2"), M("keys.3", p.Settings.BossKey), M("keys.4"));
        }

        // ------------------------------------------------------------------ helpers

        static Msg M(string key, params object[] args)
        {
            return new Msg(key, args);
        }

        static Msg FieldLabel(int fi)
        {
            return Engine.FieldLabel(fi);
        }

        static Msg CropName(string id)
        {
            return M("crop." + id);
        }

        static Msg ToolName(string tool)
        {
            return M("tool." + tool);
        }

        static string G(double v)
        {
            return NumberFormat.Gold(v);
        }

        static CommandResult Fail(CommandResult r, Msg title, params object[] lines)
        {
            r.Logs.Add(LogEntry.Make(LogKind.Error, title, lines));
            return r;
        }

        static CommandResult Ok(CommandResult r, Msg title, params object[] lines)
        {
            r.Logs.Add(LogEntry.Make(LogKind.Ok, title, lines));
            r.Changed = true;
            return r;
        }

        static CommandResult Info(CommandResult r, Msg title, params object[] lines)
        {
            r.Logs.Add(LogEntry.Make(LogKind.Info, title, lines));
            return r;
        }

        static CommandResult Usage(CommandResult r, string cmd)
        {
            return Fail(r, M("t.cmd", "/" + cmd), M("err.usage", M("usage." + cmd)));
        }

        static CommandResult Cancelled()
        {
            var r = new CommandResult();
            r.Logs.Add(LogEntry.Make(LogKind.Info, M("t.cancelled")));
            return r;
        }

        static bool TryQty(string s, out int qty)
        {
            qty = 0;
            return int.TryParse(s, NumberStyles.None, Inv, out qty) && qty >= 1 && qty <= Content.MaxQuantity;
        }

        static double RealSeconds(CommandContext c, long gameMinutes)
        {
            return GameClock.RealSecondsFromGame(gameMinutes, c.Profile.Settings.Timescale);
        }

        static bool NeedConfirm(CommandContext c, double cost)
        {
            if (!c.Profile.Settings.Confirm) return false;
            return cost >= Content.ConfirmGold || cost >= c.State.Gold / 2;
        }

        /// <summary>오래 남는 구매는 확인 선택지를 띄운다 (K4).</summary>
        static CommandResult Confirmed(CommandContext c, Msg what, double cost, Func<CommandResult> apply)
        {
            if (!NeedConfirm(c, cost)) return apply();
            var r = new CommandResult();
            var pr = new PromptRequest();
            pr.Question = M("confirm.buy", what, G(cost), G(c.State.Gold), G(c.State.Gold - cost));
            pr.Options.Add(M("opt.yes"));
            pr.Options.Add(M("opt.no"));
            pr.Default = 0;
            pr.OnSelect = delegate(int i) { return i == 0 ? apply() : Cancelled(); };
            r.Prompt = pr;
            return r;
        }

        static bool ToolOk(GameState s, string tool, Target t, CommandResult r, Msg title)
        {
            int need = RangeParser.ToolLevelNeeded(t);
            int lv = s.ToolLevel(tool);
            if (lv >= need) return true;
            Fail(r, title, M("err.tool", ToolName(tool), lv, M("range." + lv), tool, G(Content.ToolUpgradeCost(lv))));
            return false;
        }

        static bool ParseTarget(CommandContext c, string text, bool allowReady, string titleKey, CommandResult r, out Target t)
        {
            Msg e;
            if (RangeParser.TryParse(text, c.ViewField, c.State.Fields.Count, allowReady, out t, out e)) return true;
            Fail(r, M(titleKey, FieldLabel(c.ViewField)), e);
            return false;
        }

        static void Inc(Dictionary<string, int> d, string k)
        {
            GameState.Add(d, k, 1);
        }

        // ------------------------------------------------------------------ farm

        static CommandResult Plant(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 3) return Usage(r, "plant");
            var s = c.State;
            var crop = Content.Crop(a[1]);
            if (crop == null) return Fail(r, M("t.plant", FieldLabel(c.ViewField)), M("err.unknown_crop", a[1]));
            if (!s.IsUnlocked(crop.Id)) return Fail(r, M("t.plant", FieldLabel(c.ViewField)), M("err.locked_crop", CropName(crop.Id)));

            Target t;
            if (!ParseTarget(c, a[2], false, "t.plant", r, out t)) return r;
            var title = M("t.plant", FieldLabel(t.Field));
            if (!Farming.SeasonOk(s, t.Field, crop))
            {
                int season = GameClock.Season(s.GameMinute);
                int day = GameClock.Day(s.GameMinute);
                int wait = Content.DaysUntilSeason(crop, day);
                return Fail(r, title, M("err.out_of_season", CropName(crop.Id), Engine.CropSeasons(crop), M("season." + season)),
                    M("err.season_when", wait, M("season." + Content.SeasonOfDay(day + wait))));
            }
            if (!ToolOk(s, "bag", t, r, title)) return r;

            var res = Farming.Plant(s, t.Field, crop, t.Cells);
            int planted = res.Planted;
            var lines = new List<object>();
            if (planted > 0) lines.Add(M("plant.done", Engine.CropCount(crop.Id, planted), GameState.Count(s.Seeds, crop.Id)));
            if (res.NoSeed > 0) lines.Add(M("plant.no_seed", res.NoSeed, crop.Id));
            if (res.Used > 0) lines.Add(M("skip.used", res.Used));
            if (res.Locked > 0) lines.Add(M("skip.locked", res.Locked));
            if (planted == 0)
            {
                if (lines.Count == 0) lines.Add(M("plant.none"));
                return Fail(r, title, lines.ToArray());
            }
            return Ok(r, title, lines.ToArray());
        }

        static CommandResult Water(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "water");
            var s = c.State;
            Target t;
            if (!ParseTarget(c, a[1], false, "t.water", r, out t)) return r;
            var title = M("t.water", FieldLabel(t.Field));
            if (!ToolOk(s, "can", t, r, title)) return r;

            var f = s.Fields[t.Field];
            long until = s.GameMinute + Content.WaterMinutes;
            int n = 0, locked = 0;
            foreach (int i in t.Cells)
            {
                if (i >= f.Open) { locked++; continue; }
                f.Plots[i].WaterUntil = until;
                n++;
            }
            if (n == 0) return Fail(r, title, M("err.all_locked"));
            var lines = new List<object>();
            lines.Add(M("water.done", n, new Dur(RealSeconds(c, Content.WaterMinutes))));
            if (locked > 0) lines.Add(M("skip.locked", locked));
            return Ok(r, title, lines.ToArray());
        }

        static CommandResult Harvest(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "harvest");
            var s = c.State;
            Target t;
            if (!ParseTarget(c, a[1], true, "t.harvest", r, out t)) return r;
            var title = M("t.harvest", FieldLabel(t.Field));
            var f = s.Fields[t.Field];

            var cells = new List<int>();
            int moreReady = 0;
            if (t.Ready)
            {
                var ripe = new List<int>();
                for (int i = 0; i < f.Open; i++) if (Engine.Stage(f.Plots[i]) == Engine.StageRipe) ripe.Add(i);
                int lv = s.ToolLevel("sickle");
                if (lv <= 1)
                {
                    if (ripe.Count > 0) cells.Add(ripe[0]);
                }
                else if (lv == 2)
                {
                    if (ripe.Count > 0)
                    {
                        int row = ripe[0] / Content.Cols;
                        foreach (int i in ripe) if (i / Content.Cols == row) cells.Add(i);
                    }
                }
                else
                {
                    cells.AddRange(ripe);
                }
                moreReady = ripe.Count - cells.Count;
            }
            else
            {
                if (!ToolOk(s, "sickle", t, r, title)) return r;
                foreach (int i in t.Cells) if (i < f.Open && Engine.Stage(f.Plots[i]) == Engine.StageRipe) cells.Add(i);
            }
            if (cells.Count == 0) return Fail(r, title, M("harvest.none"));

            var res = Farming.Harvest(s, t.Field, cells);
            if (res.Total == 0) return Fail(r, title, M("harvest.full", res.Left));

            var lines = new List<object>();
            lines.Add(M("harvest.done", Engine.CropList(res.Got), s.StorageUsed, s.StorageCapacity));
            if (res.Left > 0) lines.Add(M("harvest.full", res.Left));
            if (moreReady > 0) lines.Add(M("harvest.more", moreReady));
            return Ok(r, title, lines.ToArray());
        }

        static CommandResult Remove(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "remove");
            Target t;
            if (!ParseTarget(c, a[1], false, "t.remove", r, out t)) return r;
            var title = M("t.remove", FieldLabel(t.Field));
            if (!t.SingleCell) return Fail(r, title, M("err.cell_only"));
            var f = c.State.Fields[t.Field];
            int i = t.Cells[0];
            string cell = RangeParser.CellName(i);
            if (i >= f.Open) return Fail(r, title, M("err.all_locked"));
            var p = f.Plots[i];
            if (p.Sprinkler > 0)
            {
                int lv = p.Sprinkler;
                p.Sprinkler = 0;
                GameState.Add(c.State.EquipmentBox, GameState.BoxKey(lv), 1);
                return Ok(r, title, M("remove.sprinkler", lv, cell, c.State.BoxSprinklers));
            }
            if (p.Crop == null) return Fail(r, title, M("remove.empty", cell));
            string crop = p.Crop;
            p.Crop = null;
            p.Growth = 0;
            p.Harvests = 0;
            p.Need = 0;
            return Ok(r, title, M("remove.done", CropName(crop), cell));
        }

        // ------------------------------------------------------------------ trade

        static CommandResult Sell(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "sell");
            var s = c.State;

            if (a[1].ToLowerInvariant() == "all")
            {
                var title = M("t.sell", M("sell.all_label"));
                if (s.StorageUsed == 0) return Fail(r, title, M("sell.none"));
                var got = new Dictionary<string, int>();
                double total = Farming.SellAll(s, got);
                return Ok(r, title, M("sell.done", Engine.CropList(got), G(total), G(s.Gold)));
            }

            var crop = Content.Crop(a[1]);
            if (crop == null) return Fail(r, M("t.sell", a[1]), M("err.unknown_crop", a[1]));
            int have = GameState.Count(s.Storage, crop.Id);
            if (have == 0) return Fail(r, M("t.sell", CropName(crop.Id)), M("sell.none_crop", CropName(crop.Id)));
            int qty = have;
            if (a.Length > 2)
            {
                if (!TryQty(a[2], out qty)) return Fail(r, M("t.sell", CropName(crop.Id)), M("err.qty", a[2]));
                if (qty > have) return Fail(r, M("t.sell", CropName(crop.Id)), M("sell.not_enough", have));
            }
            var q = Farming.Sell(s, crop.Id, qty);
            return Ok(r, M("t.sell", Engine.CropCount(crop.Id, qty)),
                M("sell.done_one", G(q.Total), G(Math.Round(q.Total / qty)), G(q.UnitBefore), G(q.UnitAfter), G(s.Gold)));
        }

        static CommandResult MarketTable(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            long now = s.GameMinute;
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad(M("market.h.crop"), 13), new Pad(M("market.h.price"), 8), new Pad(M("market.h.base"), 7),
                new Pad(M("market.h.vs"), 10), new Pad(M("market.h.today"), 11), M("market.h.note")));
            int locked = 0;
            foreach (var crop in Content.Crops)
            {
                if (!s.IsUnlocked(crop.Id)) { locked++; continue; }
                double price = Market.Price(s, crop.Id);
                double diff = price - Market.YesterdayPrice(s, crop.Id);
                double pct = Math.Round((price / crop.BasePrice - 1) * 100);
                string trend = diff > 0 ? "▲ " + NumberFormat.Num(diff) : diff < 0 ? "▼ " + NumberFormat.Num(-diff) : "-";
                string vs = (pct > 0 ? "+" : "") + pct.ToString("0", Inv) + "%";
                object note = Content.InSeason(crop, GameClock.Season(now)) ? (object)"" : M("market.off");
                // 오른 값은 초록, 내린 값은 빨강 (M5 K5)
                lines.Add(Joined.Row(new Pad(CropName(crop.Id), 13), new Pad(G(price), 8), new Pad(G(crop.BasePrice), 7),
                    new Pad(Tone.BySign(pct, vs), 10), new Pad(Tone.BySign(diff, trend), 11), note));
            }
            if (locked > 0) lines.Add(M("market.locked", locked));
            return Info(r, M("t.market", M("season." + GameClock.Season(now)), GameClock.SeasonDay(now), GameClock.Time(now)), lines.ToArray());
        }

        static CommandResult Inventory(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            var lines = new List<object>();
            double total = 0;
            foreach (var crop in Content.Crops)
            {
                int n = GameState.Count(s.Storage, crop.Id);
                if (n <= 0) continue;
                double value = Market.Quote(s, crop.Id, n).Total;
                total += value;
                lines.Add(Joined.Row(new Pad(CropName(crop.Id), 13), new Pad(M("inv.qty", n), 10),
                    new Pad(M("inv.each", G(Market.Price(s, crop.Id))), 16), M("inv.value", G(value))));
            }
            if (lines.Count == 0) lines.Add(M("inv.empty"));
            else lines.Add(M("inv.total", G(total)));
            lines.Add(M("inv.seeds", s.SeedTotal > 0 ? (object)Engine.CropList(s.Seeds) : "-"));

            var eq = new List<object>();
            for (int lv = 1; lv <= Content.MaxSprinklerLevel; lv++)
            {
                int n = GameState.Count(s.EquipmentBox, GameState.BoxKey(lv));
                if (n > 0) eq.Add(M("inv.box_sprinkler", lv, n));
            }
            int placed = 0;
            foreach (var f in s.Fields) placed += f.SprinklerCount;
            if (placed > 0) eq.Add(M("inv.placed_sprinkler", placed));
            if (s.Farm.ShippingBin) eq.Add(EqName("shipping-bin"));
            if (s.Farm.AutoBuyer) eq.Add(EqName("auto-buyer"));
            if (s.Farm.Controller > 0) eq.Add(M("inv.leveled", EqName("controller"), s.Farm.Controller));
            if (s.Farm.Caretaker > 0) eq.Add(M("inv.leveled", EqName("caretaker"), s.Farm.Caretaker));
            lines.Add(M("inv.equipment", eq.Count > 0 ? (object)new Joined(eq) : "-"));
            return Info(r, M("t.inv", s.StorageUsed, s.StorageCapacity), lines.ToArray());
        }

        /// <summary>구역 장비 요약: "sprinkler x2 · harvester". 없으면 "-".</summary>
        static object FieldGear(FieldState f)
        {
            var items = new List<object>();
            int n = f.SprinklerCount;
            if (n > 0) items.Add(M("map.sprinklers", n));
            foreach (var id in Content.FieldEquipment) if (f.Has(id)) items.Add(EqName(id));
            return items.Count > 0 ? (object)new Joined(items) : "-";
        }

        static CommandResult Unlock(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "unlock");
            var s = c.State;
            var crop = Content.Crop(a[1]);
            if (crop == null) return Fail(r, M("t.unlock", a[1]), M("err.unknown_crop", a[1]));
            var title = M("t.unlock", CropName(crop.Id));
            if (s.IsUnlocked(crop.Id)) return Fail(r, title, M("err.already_unlocked", CropName(crop.Id)));
            double cost = crop.UnlockCost;
            if (s.Gold < cost) return Fail(r, title, M("err.no_gold", G(cost), G(s.Gold)));

            return Confirmed(c, M("confirm.what_unlock", CropName(crop.Id)), cost, delegate
            {
                var res = new CommandResult();
                if (s.IsUnlocked(crop.Id)) return Fail(res, title, M("err.already_unlocked", CropName(crop.Id)));
                if (s.Gold < cost) return Fail(res, title, M("err.no_gold", G(cost), G(s.Gold)));
                s.Gold -= cost;
                s.Invested += cost;
                s.Unlocked.Add(crop.Id);
                var lines = new List<object>();
                lines.Add(M("unlock.done", G(cost), G(s.Gold)));
                lines.Add(M("unlock.info", Engine.CropSeasons(crop), G(crop.SeedCost), G(crop.BasePrice), crop.GrowDays));
                if (crop.Regrows) lines.Add(M("unlock.regrow", crop.RegrowDays));
                return Ok(res, title, lines.ToArray());
            });
        }

        static CommandResult Shop(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            var seeds = new List<object>();
            var unlocks = new List<object>();
            foreach (var crop in Content.Crops)
            {
                if (s.IsUnlocked(crop.Id)) seeds.Add(M("shop.price", crop.Id, G(crop.SeedCost)));
                else unlocks.Add(M("shop.price", crop.Id, G(crop.UnlockCost)));
            }
            double storageCost = Content.StorageUpgradeCost(s.StorageLevel);
            Msg storage = storageCost < 0
                ? M("shop.storage_max", s.StorageCapacity)
                : M("shop.storage", s.StorageCapacity, Content.StorageCapacity(s.StorageLevel + 1), G(storageCost));

            var tools = new List<object>();
            foreach (var t in Content.Tools)
            {
                int lv = s.ToolLevel(t);
                if (lv >= Content.MaxToolLevel) tools.Add(M("shop.tool_max", t));
                else tools.Add(M("shop.tool", t, lv + 1, G(Content.ToolUpgradeCost(lv))));
            }

            var f = s.Fields[c.ViewField];
            string fname = "f" + (c.ViewField + 1).ToString(Inv);
            var fieldGear = new List<object>();
            foreach (var id in Content.FieldEquipment)
                if (!f.Has(id)) fieldGear.Add(M("shop.price", id, G(Content.EquipmentCost(id))));
            var farmGear = new List<object>();
            farmGear.Add(M("shop.price", "sprinkler", G(Content.EquipmentCost("sprinkler"))));
            foreach (var id in new[] { "shipping-bin", "auto-buyer" })
                if (!s.Farm.Has(id)) farmGear.Add(M("shop.price", id, G(Content.EquipmentCost(id))));
            double cc = Content.ControllerCost(s.Farm.Controller);
            farmGear.Add(cc < 0 ? M("shop.tool_max", "controller") : M("shop.tool", "controller", s.Farm.Controller + 1, G(cc)));
            double ck = Content.CaretakerCost(s.Farm.Caretaker);
            farmGear.Add(ck < 0 ? M("shop.tool_max", "caretaker") : M("shop.tool", "caretaker", s.Farm.Caretaker + 1, G(ck)));

            Msg land;
            double fieldCost = Content.FieldCost(s.Fields.Count + 1);
            if (f.Open < Content.PlotsPerField)
                land = M("shop.land", G(Content.PlotCost(c.ViewField + 1, f.Bought)), "f" + (c.ViewField + 1).ToString(Inv), G(fieldCost));
            else
                land = M("shop.land_full", "f" + (c.ViewField + 1).ToString(Inv), G(fieldCost));

            var lines = new List<object>();
            lines.Add(M("shop.seeds", new Joined(seeds)));
            if (unlocks.Count > 0) lines.Add(M("shop.unlock", new Joined(unlocks)));
            lines.Add(M("shop.tools", new Joined(tools)));
            lines.Add(fieldGear.Count > 0 ? M("shop.gear_field", new Joined(fieldGear), fname) : M("shop.gear_field_full", fname));
            lines.Add(M("shop.gear_farm", new Joined(farmGear)));
            lines.Add(storage);
            lines.Add(land);
            lines.Add(M("shop.hint"));
            lines.Add(M("shop.help_hint"));
            return Info(r, M("t.shop"), lines.ToArray());
        }

        static CommandResult Buy(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "buy");
            var s = c.State;
            string what = a[1].ToLowerInvariant();

            if (what == "seed" || what == "seeds")
            {
                if (a.Length < 3) return Usage(r, "buy");
                var crop = Content.Crop(a[2]);
                if (crop == null) return Fail(r, M("t.buy", a[2]), M("err.unknown_crop", a[2]));
                var title = M("t.buy", M("buy.seed_label", CropName(crop.Id)));
                if (!s.IsUnlocked(crop.Id)) return Fail(r, title, M("err.locked_crop", CropName(crop.Id)));
                int qty = 1;
                if (a.Length > 3 && !TryQty(a[3], out qty)) return Fail(r, title, M("err.qty", a[3]));
                double cost = crop.SeedCost * qty;
                if (s.Gold < cost) return Fail(r, title, M("err.no_gold", G(cost), G(s.Gold)));
                s.Gold -= cost;
                GameState.Add(s.Seeds, crop.Id, qty);
                return Ok(r, title, M("buy.seed_done", qty, G(cost), GameState.Count(s.Seeds, crop.Id), G(s.Gold)));
            }

            if (Content.IsEquipment(what)) return BuyEquipment(c, a, what);
            return Usage(r, "buy");
        }

        static Msg EqName(string id)
        {
            return Rules.EquipmentName(id);
        }

        static CommandResult NoGold(CommandResult r, Msg title, double cost, double gold)
        {
            return Fail(r, title, M("err.no_gold", G(cost), G(gold)));
        }

        /// <summary>장비 구매 (M3 K1, K2).</summary>
        static CommandResult BuyEquipment(CommandContext c, string[] a, string id)
        {
            var r = new CommandResult();
            var s = c.State;
            var name = EqName(id);

            if (id == "controller" || id == "caretaker")
            {
                int lv = id == "controller" ? s.Farm.Controller : s.Farm.Caretaker;
                if (lv > 0) return Fail(r, M("t.buy", name), M("err.have_level", name, lv, id));
                return UpgradeFarm(c, id);
            }

            if (id == "sprinkler")
            {
                int qty = 1;
                if (a.Length > 2 && !TryQty(a[2], out qty)) return Fail(r, M("t.buy", name), M("err.qty", a[2]));
                double cost = Content.EquipmentCost(id) * qty;
                var title = M("t.buy", M("buy.sprinkler_label", qty));
                if (s.Gold < cost) return NoGold(r, title, cost, s.Gold);
                return Confirmed(c, M("buy.sprinkler_label", qty), cost, delegate
                {
                    var res = new CommandResult();
                    if (s.Gold < cost) return NoGold(res, title, cost, s.Gold);
                    s.Gold -= cost;
                    s.Invested += cost;
                    GameState.Add(s.EquipmentBox, GameState.BoxKey(1), qty);
                    return Ok(res, title, M("buy.sprinkler_done", qty, s.BoxSprinklers, G(cost), G(s.Gold)), M("buy.sprinkler_hint"));
                });
            }

            double price = Content.EquipmentCost(id);
            if (Content.IsFieldEquipment(id))
            {
                int fi = c.ViewField;
                if (a.Length > 2)
                {
                    int n = RangeParser.FieldNumber(a[2]);
                    if (n < 0) return Usage(r, "buy");
                    if (n < 1 || n > s.Fields.Count) return Fail(r, M("t.buy", name), M("err.no_field", n, s.Fields.Count));
                    fi = n - 1;
                }
                var f = s.Fields[fi];
                string fname = "f" + (fi + 1).ToString(Inv);
                var title = M("t.buy", M("buy.eq_in", name, FieldLabel(fi)));
                if (f.Has(id)) return Fail(r, title, M("err.have_field", name, FieldLabel(fi)));
                if (s.Gold < price) return NoGold(r, title, price, s.Gold);
                return Confirmed(c, M("buy.eq_in", name, FieldLabel(fi)), price, delegate
                {
                    var res = new CommandResult();
                    if (f.Has(id)) return Fail(res, title, M("err.have_field", name, FieldLabel(fi)));
                    if (s.Gold < price) return NoGold(res, title, price, s.Gold);
                    s.Gold -= price;
                    s.Invested += price;
                    f.Set(id, true);
                    return Ok(res, title, M("buy.eq_done", G(price), G(s.Gold)), M("buy.hint." + id, fname));
                });
            }

            var titleF = M("t.buy", name);
            if (s.Farm.Has(id)) return Fail(r, titleF, M("err.have_farm", name));
            if (s.Gold < price) return NoGold(r, titleF, price, s.Gold);
            return Confirmed(c, name, price, delegate
            {
                var res = new CommandResult();
                if (s.Farm.Has(id)) return Fail(res, titleF, M("err.have_farm", name));
                if (s.Gold < price) return NoGold(res, titleF, price, s.Gold);
                s.Gold -= price;
                s.Invested += price;
                if (id == "shipping-bin") s.Farm.ShippingBin = true;
                else s.Farm.AutoBuyer = true;
                return Ok(res, titleF, M("buy.eq_done", G(price), G(s.Gold)), M("buy.hint." + id));
            });
        }

        /// <summary>컨트롤러·Caretaker 다음 레벨 (Lv0 → Lv1 포함).</summary>
        static CommandResult UpgradeFarm(CommandContext c, string id)
        {
            var r = new CommandResult();
            var s = c.State;
            var name = EqName(id);
            var title = M("t.upgrade", name);
            bool ctl = id == "controller";
            int lv = ctl ? s.Farm.Controller : s.Farm.Caretaker;
            double cost = ctl ? Content.ControllerCost(lv) : Content.CaretakerCost(lv);
            if (cost < 0) return Fail(r, title, M("err.tool_max", name, lv));
            if (s.Gold < cost) return NoGold(r, title, cost, s.Gold);

            return Confirmed(c, M("confirm.what_tool", name, lv + 1), cost, delegate
            {
                var res = new CommandResult();
                int now = ctl ? s.Farm.Controller : s.Farm.Caretaker;
                if (now != lv) return Fail(res, title, M("t.cancelled"));
                if (s.Gold < cost) return NoGold(res, title, cost, s.Gold);
                s.Gold -= cost;
                s.Invested += cost;
                var done = M("upgrade.done", lv, lv + 1, G(cost), G(s.Gold));
                if (ctl)
                {
                    s.Farm.Controller = lv + 1;
                    var line = M("upgrade.controller", Rules.Slots(s),
                        new Dur(RealSeconds(c, Content.ControllerInterval(lv + 1))), Rules.Active(s).Count);
                    if (s.Rules.Count == 0) return Ok(res, title, done, line, M("upgrade.controller_hint"));
                    return Ok(res, title, done, line);
                }
                s.Farm.Caretaker = lv + 1;
                return Ok(res, title, done, M("upgrade.caretaker", new Dur(Content.CaretakerCap(lv) * 3600), new Dur(Content.CaretakerCap(lv + 1) * 3600)));
            });
        }

        /// <summary>/upgrade sprinkler &lt;칸&gt; (M3 K4).</summary>
        static CommandResult UpgradeSprinkler(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            var s = c.State;
            var name = EqName("sprinkler");
            if (a.Length < 3) return Fail(r, M("t.upgrade", name), M("err.usage", "/upgrade sprinkler <plot>"));
            Target t;
            if (!ParseTarget(c, a[2], false, "t.upgrade", r, out t)) return r;
            if (!t.SingleCell) return Fail(r, M("t.upgrade", name), M("err.cell_only"));
            int i = t.Cells[0];
            string cell = RangeParser.CellName(i);
            var f = s.Fields[t.Field];
            var what = M("eq.sprinkler_at", cell);
            var title = M("t.upgrade", what);
            if (i >= f.Open || f.Plots[i].Sprinkler <= 0) return Fail(r, title, M("err.no_sprinkler_at", cell));
            var p = f.Plots[i];
            int lv = p.Sprinkler;
            double cost = Content.SprinklerUpgradeCost(lv);
            if (cost < 0) return Fail(r, title, M("err.tool_max", name, lv));
            if (s.Gold < cost) return NoGold(r, title, cost, s.Gold);

            return Confirmed(c, M("confirm.what_tool", what, lv + 1), cost, delegate
            {
                var res = new CommandResult();
                if (p.Sprinkler != lv) return Fail(res, title, M("t.cancelled"));
                if (s.Gold < cost) return NoGold(res, title, cost, s.Gold);
                s.Gold -= cost;
                s.Invested += cost;
                p.Sprinkler = lv + 1;
                int n = Farming.WaterAround(s, t.Field, i, lv + 1);
                return Ok(res, title, M("upgrade.done", lv, lv + 1, G(cost), G(s.Gold)),
                    M("upgrade.sprinkler", Farming.Reach(i, lv + 1).Count, n));
            });
        }

        /// <summary>"2" 또는 "lv2" → 2. 스프링클러 레벨 범위 밖이면 false.</summary>
        public static bool TryLevel(string s, out int level)
        {
            level = 0;
            if (s == null) return false;
            string t = s.ToLowerInvariant();
            if (t.StartsWith("lv")) t = t.Substring(2);
            return int.TryParse(t, NumberStyles.None, Inv, out level) && level >= 1 && level <= Content.MaxSprinklerLevel;
        }

        /// <summary>보관함에 든 레벨별 수: "Lv1 ×2 · Lv3 ×1".</summary>
        static object BoxLevels(GameState s)
        {
            var items = new List<object>();
            for (int lv = 1; lv <= Content.MaxSprinklerLevel; lv++)
            {
                int n = GameState.Count(s.EquipmentBox, GameState.BoxKey(lv));
                if (n > 0) items.Add(M("box.level", lv, n));
            }
            return new Joined(items);
        }

        /// <summary>/place sprinkler &lt;칸&gt; [레벨] (FR-EQP-01, M3 K3, M5 K11).</summary>
        static CommandResult Place(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 3 || a.Length > 4) return Usage(r, "place");
            var s = c.State;
            string what = a[1].ToLowerInvariant();
            if (what != "sprinkler")
            {
                if (Content.IsEquipment(what)) return Fail(r, M("t.place", EqName(what)), M("err.place_whole", EqName(what), what));
                return Usage(r, "place");
            }
            Target t;
            if (!ParseTarget(c, a[2], false, "t.place", r, out t)) return r;
            var title = M("t.place", FieldLabel(t.Field));
            int want = 0;
            if (a.Length == 4 && !TryLevel(a[3], out want)) return Usage(r, "place");
            if (s.BoxSprinklers == 0) return Fail(r, title, M("err.no_sprinkler", G(Content.EquipmentCost("sprinkler"))));
            if (want > 0 && GameState.Count(s.EquipmentBox, GameState.BoxKey(want)) == 0)
                return Fail(r, title, M("err.no_sprinkler_lv", want, BoxLevels(s)));
            if (!t.SingleCell) return Fail(r, title, M("err.cell_only"));
            var f = s.Fields[t.Field];
            int i = t.Cells[0];
            string cell = RangeParser.CellName(i);
            if (i >= f.Open) return Fail(r, title, M("err.all_locked"));
            var p = f.Plots[i];
            if (!Farming.IsEmpty(p)) return Fail(r, title, M("err.place_used", cell));
            int lv = Farming.TakeSprinkler(s, want);
            p.Sprinkler = lv;
            int n = Farming.WaterAround(s, t.Field, i, lv);
            return Ok(r, title, M("place.done", lv, cell, n), M("place.daily", s.BoxSprinklers));
        }

        /// <summary>
        /// 입력 중인 `/place sprinkler &lt;칸&gt; [레벨]`·`/upgrade sprinkler &lt;칸&gt;`을 실행하면 물을 받을 범위 (M5 K9).
        /// 칸·레벨이 맞지 않으면(잠김, 작물, 보관함에 없음, 최대 레벨) null. 골드는 보지 않는다.
        /// </summary>
        public static SprinklerPreview PreviewSprinkler(CommandContext c, string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] a = HangulKeys.FixLine(line.Trim()).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 3 || a[1].ToLowerInvariant() != "sprinkler") return null;
            string cmd = a[0].ToLowerInvariant();
            bool place = cmd == "/place";
            if (!place && cmd != "/upgrade") return null;
            if (a.Length > (place ? 4 : 3)) return null;
            var s = c.State;
            int view = c.ViewField >= 0 && c.ViewField < s.Fields.Count ? c.ViewField : 0;
            Target t;
            Msg e;
            if (!RangeParser.TryParse(a[2], view, s.Fields.Count, false, out t, out e) || !t.SingleCell) return null;
            var f = s.Fields[t.Field];
            int i = t.Cells[0];
            if (i >= f.Open) return null;
            int lv;
            if (place)
            {
                int want = 0;
                if (a.Length == 4 && !TryLevel(a[3], out want)) return null;
                if (!Farming.IsEmpty(f.Plots[i])) return null;
                lv = want > 0 ? (GameState.Count(s.EquipmentBox, GameState.BoxKey(want)) > 0 ? want : 0) : Farming.BestBoxLevel(s);
            }
            else
            {
                int now = f.Plots[i].Sprinkler;
                lv = now > 0 && Content.SprinklerUpgradeCost(now) >= 0 ? now + 1 : 0;
            }
            if (lv <= 0) return null;
            var pv = new SprinklerPreview();
            pv.Field = t.Field;
            pv.Cell = i;
            pv.Level = lv;
            pv.Range = Farming.RangeIf(f, i, lv);
            return pv;
        }

        // ------------------------------------------------------------------ auto (F9)

        static CommandResult Auto(CommandContext c, string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "list": return AutoList(c);
                case "add": return AutoAdd(c, a);
                case "on": return AutoToggle(c, a, true);
                case "off": return AutoToggle(c, a, false);
                case "del": return AutoDel(c, a);
                case "move": return AutoMove(c, a);
            }
            return Usage(new CommandResult(), "auto");
        }

        static Dur CheckEvery(CommandContext c)
        {
            return new Dur(RealSeconds(c, Content.ControllerInterval(c.State.Farm.Controller)));
        }

        /// <summary>목록의 상태 열 (M3 K18). state는 on / off / wait 문구 키.</summary>
        static Msg RuleNote(CommandContext c, int i, List<int> active, out string state)
        {
            var s = c.State;
            var rs = s.Rules[i];
            var rule = Rules.Get(rs.Text);
            if (!rs.On)
            {
                state = "auto.state.off";
                return M("auto.st.ran", rs.Runs);
            }
            if (!active.Contains(i))
            {
                state = "auto.state.wait";
                return Rules.Slots(s) == 0 ? M("auto.st.no_controller", G(Content.ControllerCost(0))) : M("auto.st.wait");
            }
            state = "auto.state.on";
            if (rule == null) return M("auto.st.invalid");
            string miss = Rules.Missing(s, rule);
            if (miss != null) return Rules.FailMsg(rule, miss);
            if (rs.Fail != null && rs.Fail != "needs" && rs.Fail != "no_field") return M("auto.st.last", Rules.FailMsg(rule, rs.Fail));
            return M("auto.st.ran", rs.Runs);
        }

        /// <summary>추가·켜기 뒤에 지금 동작하는지 한 줄로.</summary>
        static Msg RuleStatusLine(CommandContext c, int i)
        {
            var s = c.State;
            if (Rules.Slots(s) == 0) return M("auto.add.no_controller", G(Content.ControllerCost(0)));
            if (!Rules.IsActive(s, i)) return M("auto.add.wait", Rules.Active(s).Count, Rules.Slots(s));
            var rule = Rules.Get(s.Rules[i].Text);
            string miss = rule == null ? null : Rules.Missing(s, rule);
            if (miss != null) return Rules.FailMsg(rule, miss);
            return M("auto.add.active", CheckEvery(c));
        }

        static CommandResult AutoList(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            int slots = Rules.Slots(s);
            var active = Rules.Active(s);
            var title = slots == 0 ? M("t.auto_none") : M("t.auto_list", active.Count, slots, CheckEvery(c));
            var lines = new List<object>();
            if (s.Rules.Count == 0)
            {
                lines.Add(M("auto.empty"));
            }
            else
            {
                int w = 0;
                foreach (var rs in s.Rules) w = Math.Max(w, rs.Text.Length);
                w = Math.Min(w, 60) + 3;
                for (int i = 0; i < s.Rules.Count; i++)
                {
                    string state;
                    var note = RuleNote(c, i, active, out state);
                    lines.Add(Joined.Row(new Pad("#" + (i + 1).ToString(Inv), 5), new Pad(M(state), 6), new Pad(s.Rules[i].Text, w), note));
                }
            }
            if (slots == 0) lines.Add(M("auto.no_controller", G(Content.ControllerCost(0))));
            return Info(r, title, lines.ToArray());
        }

        static CommandResult AutoAdd(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            var s = c.State;
            if (a.Length < 3) return Usage(r, "auto");
            string text = string.Join(" ", a, 2, a.Length - 2);
            Msg err;
            var rule = Rules.Compile(text, out err);
            if (rule == null) return Fail(r, M("t.auto"), err, M("auto.syntax_hint"));
            if (s.Rules.Count >= Content.MaxRules) return Fail(r, M("t.auto"), M("err.max_rules", Content.MaxRules));
            for (int i = 0; i < s.Rules.Count; i++)
                if (s.Rules[i].Text == rule.Text) return Fail(r, M("t.auto"), M("err.rule_exists", i + 1));
            var rs = new RuleState();
            rs.Text = rule.Text;
            rs.On = true;
            s.Rules.Add(rs);
            int n = s.Rules.Count;
            return Ok(r, M("t.auto_add", n), M("auto.rule", n, rule.Text), RuleStatusLine(c, n - 1));
        }

        static bool RuleIndex(CommandContext c, string[] a, int at, CommandResult r, out int index)
        {
            index = -1;
            int count = c.State.Rules.Count;
            if (a.Length <= at)
            {
                Usage(r, "auto");
                return false;
            }
            int n;
            if (!int.TryParse(a[at].TrimStart('#'), NumberStyles.None, Inv, out n) || n < 1 || n > count)
            {
                Fail(r, M("t.auto"), M("err.no_rule", a[at], count));
                return false;
            }
            index = n - 1;
            return true;
        }

        static CommandResult AutoToggle(CommandContext c, string[] a, bool on)
        {
            var r = new CommandResult();
            int i;
            if (!RuleIndex(c, a, 2, r, out i)) return r;
            var rs = c.State.Rules[i];
            rs.On = on;
            var line = M("auto.rule", i + 1, rs.Text);
            if (!on) return Ok(r, M("t.auto_off", i + 1), line);
            return Ok(r, M("t.auto_on", i + 1), line, RuleStatusLine(c, i));
        }

        static CommandResult AutoDel(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            int i;
            if (!RuleIndex(c, a, 2, r, out i)) return r;
            var rs = c.State.Rules[i];
            c.State.Rules.RemoveAt(i);
            return Ok(r, M("t.auto_del", i + 1), M("auto.rule", i + 1, rs.Text), M("auto.renumbered"));
        }

        static CommandResult AutoMove(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            int i;
            if (!RuleIndex(c, a, 2, r, out i)) return r;
            int j;
            if (!RuleIndex(c, a, 3, r, out j)) return r;
            var rs = c.State.Rules[i];
            c.State.Rules.RemoveAt(i);
            c.State.Rules.Insert(j, rs);
            return Ok(r, M("t.auto_move", i + 1, j + 1), M("auto.rule", j + 1, rs.Text), M("auto.order_hint"));
        }

        static CommandResult Upgrade(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "upgrade");
            var s = c.State;
            string tool = a[1].ToLowerInvariant();
            if (tool == "storage") return UpgradeStorage(c);
            if (tool == "sprinkler") return UpgradeSprinkler(c, a);
            if (tool == "controller" || tool == "caretaker") return UpgradeFarm(c, tool);
            if (!Content.IsTool(tool))
            {
                if (Content.IsEquipment(tool)) return Fail(r, M("t.upgrade", EqName(tool)), M("err.no_upgrade", EqName(tool)));
                return Usage(r, "upgrade");
            }
            var title = M("t.upgrade", ToolName(tool));
            int lv = s.ToolLevel(tool);
            if (lv >= Content.MaxToolLevel) return Fail(r, title, M("err.tool_max", ToolName(tool), lv));
            double cost = Content.ToolUpgradeCost(lv);
            if (s.Gold < cost) return Fail(r, title, M("err.no_gold", G(cost), G(s.Gold)));

            return Confirmed(c, M("confirm.what_tool", ToolName(tool), lv + 1), cost, delegate
            {
                var res = new CommandResult();
                int now = s.ToolLevel(tool);
                if (now != lv) return Fail(res, title, M("err.tool_max", ToolName(tool), now));
                if (s.Gold < cost) return Fail(res, title, M("err.no_gold", G(cost), G(s.Gold)));
                s.Gold -= cost;
                s.Invested += cost;
                s.Tools[tool] = lv + 1;
                return Ok(res, title, M("upgrade.done", lv, lv + 1, G(cost), G(s.Gold)), M("upgrade.range", M("range." + (lv + 1))));
            });
        }

        static CommandResult UpgradeStorage(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            var title = M("t.upgrade", M("tool.storage"));
            int lv = s.StorageLevel;
            double cost = Content.StorageUpgradeCost(lv);
            if (cost < 0) return Fail(r, title, M("err.tool_max", M("tool.storage"), lv));
            if (s.Gold < cost) return Fail(r, title, M("err.no_gold", G(cost), G(s.Gold)));
            int capBefore = s.StorageCapacity;
            int capAfter = Content.StorageCapacity(lv + 1);

            return Confirmed(c, M("confirm.what_storage", capBefore, capAfter), cost, delegate
            {
                var res = new CommandResult();
                if (s.StorageLevel != lv) return Fail(res, title, M("t.cancelled"));
                if (s.Gold < cost) return Fail(res, title, M("err.no_gold", G(cost), G(s.Gold)));
                s.Gold -= cost;
                s.Invested += cost;
                s.StorageLevel = lv + 1;
                return Ok(res, title, M("upgrade.storage_done", capBefore, capAfter, G(cost), G(s.Gold)));
            });
        }

        // ------------------------------------------------------------------ land

        static CommandResult Expand(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            var s = c.State;
            if (a.Length >= 2 && a[1].ToLowerInvariant() == "field") return NewField(c);

            int fi = c.ViewField;
            int count = 1;
            int idx = 1;
            if (a.Length > idx)
            {
                int n = RangeParser.FieldNumber(a[idx]);
                if (n >= 0)
                {
                    if (n < 1 || n > s.Fields.Count) return Fail(r, M("t.expand", a[idx]), M("err.no_field", n, s.Fields.Count));
                    fi = n - 1;
                    idx++;
                }
            }
            var title = M("t.expand", FieldLabel(fi));
            if (a.Length > idx && !TryQty(a[idx], out count)) return Fail(r, title, M("err.qty", a[idx]));
            if (a.Length > idx + 1) return Usage(r, "expand");

            var f = s.Fields[fi];
            int remaining = Content.PlotsPerField - f.Open;
            if (remaining <= 0) return Fail(r, title, M("expand.full", FieldLabel(fi), G(Content.FieldCost(s.Fields.Count + 1))));
            count = Math.Min(count, remaining);

            double total = 0;
            for (int k = 0; k < count; k++) total += Content.PlotCost(fi + 1, f.Bought + k);
            if (s.Gold < total) return Fail(r, title, M("err.no_gold", G(total), G(s.Gold)));

            int openBefore = f.Open;
            return Confirmed(c, M("confirm.what_plots", count, FieldLabel(fi)), total, delegate
            {
                var res = new CommandResult();
                if (f.Open != openBefore) return Fail(res, title, M("t.cancelled"));
                if (s.Gold < total) return Fail(res, title, M("err.no_gold", G(total), G(s.Gold)));
                s.Gold -= total;
                s.Invested += total;
                f.Open += count;
                f.Bought += count;
                var lines = new List<object>();
                lines.Add(M("expand.plots_done", count, f.Open, Content.PlotsPerField, G(total), G(s.Gold)));
                if (f.Open < Content.PlotsPerField) lines.Add(M("expand.next", G(Content.PlotCost(fi + 1, f.Bought))));
                else lines.Add(M("expand.now_full", G(Content.FieldCost(s.Fields.Count + 1))));
                return Ok(res, title, lines.ToArray());
            });
        }

        static CommandResult NewField(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            int n = s.Fields.Count + 1;
            double cost = Content.FieldCost(n);
            var title = M("t.expand", FieldLabel(n - 1));
            if (s.Gold < cost) return Fail(r, title, M("err.no_gold", G(cost), G(s.Gold)));
            return Confirmed(c, M("confirm.what_field", FieldLabel(n - 1)), cost, delegate
            {
                var res = new CommandResult();
                if (s.Fields.Count + 1 != n) return Fail(res, title, M("t.cancelled"));
                if (s.Gold < cost) return Fail(res, title, M("err.no_gold", G(cost), G(s.Gold)));
                s.Gold -= cost;
                s.Invested += cost;
                s.Fields.Add(FieldState.Create(Content.StartOpenPlots));
                res.ViewField = n - 1;
                return Ok(res, title, M("expand.field_done", Content.StartOpenPlots, G(cost), G(s.Gold)));
            });
        }

        static CommandResult Field(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length < 2) return Usage(r, "field");
            int count = c.State.Fields.Count;
            string v = a[1].ToLowerInvariant();
            int target;
            if (v == "next") target = Math.Min(count - 1, c.ViewField + 1);
            else if (v == "prev") target = Math.Max(0, c.ViewField - 1);
            else
            {
                int n = RangeParser.FieldNumber(v);
                if (n < 0 && !int.TryParse(v, NumberStyles.None, Inv, out n)) return Usage(r, "field");
                if (n < 1 || n > count) return Fail(r, M("t.field", v), M("err.no_field", n, count));
                target = n - 1;
            }
            r.ViewField = target;
            r.Logs.Add(LogEntry.Make(LogKind.Ok, M("t.field", FieldLabel(target)), M("field.done", target + 1, count)));
            return r;
        }

        static CommandResult Map(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad(M("map.h.field"), 8), new Pad(M("map.h.plots"), 9), new Pad(M("map.h.ready"), 8),
                new Pad(M("map.h.dry"), 7), new Pad(M("map.h.growing"), 10), new Pad(M("map.h.empty"), 8), M("map.h.equipment")));
            for (int i = 0; i < s.Fields.Count; i++)
            {
                var st = Engine.Stats(s, i);
                string name = "f" + (i + 1).ToString(Inv) + (i == c.ViewField ? " *" : "");
                lines.Add(Joined.Row(new Pad(name, 8), new Pad(st.Open.ToString(Inv) + "/" + Content.PlotsPerField.ToString(Inv), 9),
                    new Pad(st.Ripe.ToString(Inv), 8), new Pad(st.Dry.ToString(Inv), 7), new Pad(st.Growing.ToString(Inv), 10),
                    new Pad(st.Empty.ToString(Inv), 8), FieldGear(s.Fields[i])));
            }
            return Info(r, M("t.map", s.Fields.Count), lines.ToArray());
        }

        // ------------------------------------------------------------------ crops (M4 K18, 사용자 요청)

        /// <summary>/crops: 작물별 계절, 지금 심을 수 있는지(제철 남은 날 / 며칠 뒤), 성장·재수확, 값, 잠김.</summary>
        static CommandResult CropsTable(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            long now = s.GameMinute;
            int day = GameClock.Day(now);
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad(M("crops.h.crop"), 13), new Pad(M("crops.h.season"), 16), new Pad(M("crops.h.plant"), 20),
                new Pad(M("crops.h.grow"), 7), new Pad(M("crops.h.regrow"), 8), new Pad(M("crops.h.seed"), 7), new Pad(M("crops.h.price"), 7), M("crops.h.note")));
            foreach (var crop in Content.Crops)
            {
                object when;
                int left = Content.SeasonDaysLeft(crop, day);
                if (left > 0)
                {
                    when = M("crops.now", left);
                }
                else
                {
                    int wait = Content.DaysUntilSeason(crop, day);
                    when = M("crops.in", wait, M("season." + Content.SeasonOfDay(day + wait)));
                }
                object regrow = crop.Regrows ? (object)M("crops.days", crop.RegrowDays) : "-";
                object note = s.IsUnlocked(crop.Id) ? (object)"" : M("crops.locked", G(crop.UnlockCost));
                lines.Add(Joined.Row(new Pad(CropName(crop.Id), 13), new Pad(Engine.CropSeasons(crop), 16), new Pad(when, 20),
                    new Pad(M("crops.days", crop.GrowDays), 7), new Pad(regrow, 8), new Pad(G(crop.SeedCost), 7), new Pad(G(crop.BasePrice), 7), note));
            }
            var gh = new List<object>();
            for (int i = 0; i < s.Fields.Count; i++) if (s.Fields[i].Greenhouse) gh.Add("f" + (i + 1).ToString(Inv));
            if (gh.Count > 0) lines.Add(M("crops.greenhouse", new Joined(gh)));
            lines.Add(M("crops.hint"));
            int sd = GameClock.SeasonDay(now);
            return Info(r, M("t.crops", M("season." + GameClock.Season(now)), sd, Content.SeasonDays - sd + 1), lines.ToArray());
        }

        // ------------------------------------------------------------------ growing (M4 K21, 사용자 요청)

        /// <summary>/growing 한 줄: 같은 구역·작물·상태·남은 시간인 칸 묶음.</summary>
        sealed class GrowGroup
        {
            public int Field;
            public string Crop;
            public int State; // 0 다 자람, 1 물 충분(또는 스프링클러), 2 다 자라기 전에 물이 끝남, 3 마름
            public long Left; // 남은 성장 분 (물이 있는 시간)
            public long WaterLeft;
            public readonly List<int> Cells = new List<int>();
        }

        public const int GrowingRows = 20;

        /// <summary>/growing [fN|작물]: 자라는 작물을 묶어 남은 실제 시간 순으로 (사용자 요청 2026-11-27).</summary>
        static CommandResult Growing(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            var s = c.State;
            if (a.Length > 2) return Usage(r, "growing");
            int onlyField = -1;
            CropDef onlyCrop = null;
            if (a.Length == 2)
            {
                int n = RangeParser.FieldNumber(a[1]);
                if (n >= 0)
                {
                    if (n < 1 || n > s.Fields.Count) return Fail(r, M("t.cmd", "/growing"), M("err.no_field", n, s.Fields.Count));
                    onlyField = n - 1;
                }
                else
                {
                    onlyCrop = Content.Crop(a[1]);
                    if (onlyCrop == null) return Fail(r, M("t.cmd", "/growing"), M("err.unknown_crop", a[1]));
                }
            }

            long now = s.GameMinute;
            var groups = new List<GrowGroup>();
            int plots = 0;
            for (int fi = 0; fi < s.Fields.Count; fi++)
            {
                if (onlyField >= 0 && fi != onlyField) continue;
                var f = s.Fields[fi];
                bool[] covered = f.SprinklerCount > 0 ? Farming.Covered(f) : null;
                for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                {
                    var p = f.Plots[i];
                    if (p.Crop == null) continue;
                    var d = Content.Crop(p.Crop);
                    if (d == null || (onlyCrop != null && d.Id != onlyCrop.Id)) continue;
                    plots++;
                    long left = Math.Max(0, Engine.Need(p, d) - p.Growth);
                    long water = Math.Max(0, p.WaterUntil - now);
                    bool sprinkled = covered != null && covered[i];
                    int state = left == 0 ? 0 : sprinkled || water >= left ? 1 : water > 0 ? 2 : 3;
                    GrowGroup g = null;
                    foreach (var x in groups)
                        if (x.Field == fi && x.Crop == d.Id && x.State == state && x.Left == left && (state != 2 || x.WaterLeft == water)) { g = x; break; }
                    if (g == null)
                    {
                        g = new GrowGroup();
                        g.Field = fi;
                        g.Crop = d.Id;
                        g.State = state;
                        g.Left = state == 0 ? 0 : left;
                        g.WaterLeft = water;
                        groups.Add(g);
                    }
                    g.Cells.Add(i);
                }
            }
            if (groups.Count == 0) return Info(r, M("t.growing", 0), M("grow.none"));

            // 다 자람 → 물 충분(빠른 순) → 물이 먼저 끝남 → 마름
            groups.Sort(delegate(GrowGroup x, GrowGroup y)
            {
                int rx = x.State == 3 ? 3 : x.State == 0 ? 0 : 1;
                int ry = y.State == 3 ? 3 : y.State == 0 ? 0 : 1;
                if (rx != ry) return rx.CompareTo(ry);
                if (x.Left != y.Left) return x.Left.CompareTo(y.Left);
                return x.Field != y.Field ? x.Field.CompareTo(y.Field) : x.Cells[0].CompareTo(y.Cells[0]);
            });

            bool many = s.Fields.Count > 1 && onlyField < 0;
            var lines = new List<object>();
            lines.Add(many
                ? Joined.Row(new Pad(M("grow.h.field"), 6), new Pad(M("grow.h.crop"), 13), new Pad(M("grow.h.plots"), 22), new Pad(M("grow.h.ready"), 13), M("grow.h.note"))
                : Joined.Row(new Pad(M("grow.h.crop"), 13), new Pad(M("grow.h.plots"), 22), new Pad(M("grow.h.ready"), 13), M("grow.h.note")));
            for (int k = 0; k < groups.Count && k < GrowingRows; k++)
            {
                var g = groups[k];
                string cells = RangeParser.Runs(g.Cells);
                string one = RangeParser.Compress(g.Cells);
                string target = one == null ? "" : (g.Field == c.ViewField ? one : "f" + (g.Field + 1).ToString(Inv) + "." + one);
                object when, note;
                switch (g.State)
                {
                    case 0:
                        when = Tone.Good(M("grow.ready"));
                        note = Rules.AutoHarvests(s, g.Field) ? (object)M("grow.note_auto") : M("grow.note_harvest", target.Length > 0 ? target : "ready");
                        break;
                    case 1:
                        when = new Dur(RealSeconds(c, g.Left));
                        note = "";
                        break;
                    case 2:
                        when = new Dur(RealSeconds(c, g.Left));
                        note = M("grow.note_short", new Dur(RealSeconds(c, g.WaterLeft)), target.Length > 0 ? target : "<range>");
                        break;
                    default:
                        when = Tone.Warn(M("grow.stopped"));
                        note = M("grow.note_dry", new Dur(RealSeconds(c, g.Left)), target.Length > 0 ? target : "<range>");
                        break;
                }
                var cellsPad = new Pad(TextFit(cells, 21), 22);
                lines.Add(many
                    ? Joined.Row(new Pad("f" + (g.Field + 1).ToString(Inv), 6), new Pad(CropName(g.Crop), 13), cellsPad, new Pad(when, 13), note)
                    : Joined.Row(new Pad(CropName(g.Crop), 13), cellsPad, new Pad(when, 13), note));
            }
            if (groups.Count > GrowingRows) lines.Add(M("grow.more", groups.Count - GrowingRows));
            return Info(r, M("t.growing", plots), lines.ToArray());
        }

        /// <summary>칸 목록이 열 폭보다 길면 끝을 "…"로 줄인다 (칸 이름은 ASCII).</summary>
        static string TextFit(string s, int width)
        {
            return s.Length <= width ? s : s.Substring(0, width - 1) + "…";
        }

        // ------------------------------------------------------------------ records (M4)

        static string N(double v)
        {
            return NumberFormat.Num(v);
        }

        /// <summary>/records: 체크포인트 표 (S15, K6). /records daily: 일별 기록.</summary>
        static CommandResult RecordsCmd(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length >= 2)
            {
                if (a[1].ToLowerInvariant() == "daily" && a.Length == 2) return DailyTable(c);
                return Usage(r, "records");
            }
            var s = c.State;
            var p = c.Profile;
            Records.Observe(s);
            int day = GameClock.Day(s.GameMinute);
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad(M("rec.h.day"), 7), new Pad(M("rec.h.best"), 16), new Pad(M("rec.h.run", s.Run), 14), M("rec.h.diff")));
            foreach (long d in Records.CheckpointRows(s, p))
            {
                double best, mine;
                int bestRun;
                bool hasBest = Records.BestBefore(p, s.Run, d, out best, out bestRun);
                bool has = s.Checkpoints.TryGetValue(Records.Key(d), out mine);
                object bestCell = hasBest ? (object)M("rec.best", N(best), bestRun) : "-";
                object mineCell = has ? (object)N(mine) : d >= day ? (object)M("rec.in_days", d - day + 1) : "-";
                object diff = "";
                if (has && hasBest) diff = mine > best ? Tone.Good("▲ " + N(mine - best)) : mine < best ? (object)Tone.Bad("▼ " + N(best - mine)) : "=";
                lines.Add(Joined.Row(new Pad(d.ToString(Inv), 7), new Pad(bestCell, 16), new Pad(mineCell, 14), diff));
            }
            lines.Add(M("rec.peak", N(s.PeakNetWorth), s.PeakDay, N(Records.NetWorth(s))));
            if (p.Runs.Count <= 1) lines.Add(M("rec.first_run"));
            return Info(r, M("t.records", s.Run, day), lines.ToArray());
        }

        static CommandResult DailyTable(CommandContext c)
        {
            var r = new CommandResult();
            var list = new List<DailyRecord>(c.Profile.Daily);
            if (list.Count == 0) return Info(r, M("t.records_daily"), M("rec.none"));
            list.Sort(delegate(DailyRecord x, DailyRecord y)
            {
                int k = string.CompareOrdinal(y.Date, x.Date);
                return k != 0 ? k : y.Run.CompareTo(x.Run);
            });
            DailyRecord best = null;
            foreach (var d in list) if (d.Earned > 0 && (best == null || d.Earned > best.Earned)) best = d;
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad(M("rec.h.date"), 13), new Pad(M("rec.h.run_col"), 6), new Pad(M("rec.h.networth"), 12), new Pad(M("rec.h.earned"), 10), ""));
            int n = Math.Min(Content.DailyRows, list.Count);
            for (int i = 0; i < n; i++) lines.Add(DailyRow(list[i], list[i] == best));
            if (best != null && list.IndexOf(best) >= n) lines.Add(DailyRow(best, true));
            return Info(r, M("t.records_daily"), lines.ToArray());
        }

        static object DailyRow(DailyRecord d, bool best)
        {
            return Joined.Row(new Pad(d.Date, 13), new Pad("#" + d.Run.ToString(Inv), 6), new Pad(N(d.NetWorth), 12),
                new Pad(N(d.Earned), 10), best ? (object)M("rec.best_day") : "");
        }

        /// <summary>/rank: 판별 최고 총자산 순위 (S16, K7).</summary>
        static CommandResult Rank(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            var list = Records.Ranking(c.Profile);
            if (list.Count == 0) return Info(r, M("t.rank"), M("rec.none"));
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad("#", 5), new Pad(M("rank.h.run"), 7), new Pad(M("rank.h.peak"), 10), new Pad(M("rank.h.days"), 8),
                new Pad(M("rank.h.fert"), 11), M("rank.h.ended")));
            for (int i = 0; i < list.Count && i < Content.RankRows; i++)
            {
                var x = list[i];
                bool cur = x.Run == s.Run;
                object ended = cur ? (object)M("rank.current") : (x.EndedAt ?? "-");
                lines.Add(Joined.Row(new Pad((i + 1).ToString(Inv), 5), new Pad("#" + x.Run.ToString(Inv), 7), new Pad(N(x.Peak), 10),
                    new Pad(x.LastDay.ToString(Inv), 8), new Pad(cur ? "-" : "+" + x.Fertility.ToString(Inv), 11), ended));
            }
            return Info(r, M("t.rank"), lines.ToArray());
        }

        /// <summary>/dex: 작물 도감과 업적 (S20, K14, K15).</summary>
        static CommandResult DexCmd(CommandContext c)
        {
            var r = new CommandResult();
            var p = c.Profile;
            int found = 0;
            var lines = new List<object>();
            foreach (var crop in Content.Crops)
            {
                DexEntry e;
                p.Dex.TryGetValue(crop.Id, out e);
                long total = e == null ? 0 : e.Total;
                if (total > 0) found++;
                object info = total > 0 ? (object)M("dex.line", NumberFormat.Int(total), e.First ?? "-", e.FirstRun) : M("dex.none");
                lines.Add(Joined.Row(new Pad(CropName(crop.Id), 13), new Pad(Records.Stars(Content.DexStars(total)), 6), info));
            }
            lines.Add(M("dex.ach_head"));
            foreach (var id in Content.Achievements)
            {
                string date;
                bool got = p.Achievements.TryGetValue(id, out date);
                lines.Add(Joined.Row(new Pad(got ? "✓" : "·", 3), new Pad(M("ach." + id), 18), new Pad(M("ach." + id + ".desc"), 48), got ? (object)date : ""));
            }
            return Info(r, M("t.dex", found, Content.Crops.Length, p.Achievements.Count, Content.Achievements.Length), lines.ToArray());
        }

        /// <summary>/prestige: 상자와 두 번 확인 (S17, K8). 실행은 화면이 한다 (장면, 새 판).</summary>
        static CommandResult PrestigeCmd(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            var p = c.Profile;
            long gain = Prestige.Gain(s);
            var title = M("t.prestige");
            if (gain <= 0) return Fail(r, title, M("prestige.need", G(Content.FertilityUnit), G(s.PeakNetWorth)), M("prestige.need_hint"));

            int cps = 0, best = 0;
            foreach (var k in s.Checkpoints.Keys)
            {
                long d;
                if (!long.TryParse(k, NumberStyles.None, Inv, out d)) continue;
                cps++;
                bool isBest;
                Records.CheckpointLine(s, p, d, out isBest);
                if (isBest) best++;
            }

            var pr = new PromptRequest();
            pr.Question = M("prestige.q", s.Run + 1);
            pr.Details.Add(M("prestige.this", N(s.PeakNetWorth), s.PeakDay, cps, best));
            pr.Details.Add(M("prestige.gain", gain, p.Fertility + gain));
            pr.Details.Add(M("prestige.reset"));
            pr.Details.Add(M("prestige.keep", s.Rules.Count));
            pr.Options.Add(M("prestige.opt_yes"));
            pr.Options.Add(M("prestige.opt_no"));
            pr.Default = 0;
            pr.OnSelect = delegate(int i)
            {
                if (i != 0) return Cancelled();
                var r2 = new CommandResult();
                var second = new PromptRequest();
                second.Question = M("prestige.sure");
                second.Options.Add(M("opt.yes"));
                second.Options.Add(M("opt.no"));
                second.Default = 1;
                second.OnSelect = delegate(int j)
                {
                    if (j != 0) return Cancelled();
                    var r3 = new CommandResult();
                    r3.Action = UiAction.Prestige;
                    r3.Changed = true;
                    return r3;
                };
                r2.Prompt = second;
                return r2;
            };
            r.Prompt = pr;
            return r;
        }

        /// <summary>/perks, /perks buy &lt;id&gt; (S19, K12, K13).</summary>
        static CommandResult Perks(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            var p = c.Profile;
            var s = c.State;
            if (a.Length >= 2)
            {
                if (a[1].ToLowerInvariant() != "buy" || a.Length != 3) return Usage(r, "perks");
                string id = a[2].ToLowerInvariant();
                if (!Content.IsPerk(id)) return Fail(r, M("t.perks_buy", id), M("err.unknown_perk", id));
                var title = M("t.perks_buy", M("perk." + id));
                int lv = p.PerkLevel(id);
                if (Prestige.AtMax(id, lv)) return Fail(r, title, M("err.perk_max", M("perk." + id), lv));
                long cost;
                if (!Prestige.Buy(s, p, id, out cost)) return Fail(r, title, M("err.no_fertility", cost, p.Fertility));
                return Ok(r, title, M("perks.done", lv, lv + 1, cost, p.Fertility),
                    Prestige.Change(id, lv),
                    M(Content.PerkNextRun(id) ? "perks.next_run" : id == "rich-soil" ? "perks.next_plant" : "perks.now"));
            }
            var lines = new List<object>();
            lines.Add(Joined.Row(new Pad(M("perks.h.id"), 17), new Pad(M("perks.h.level"), 7), new Pad(M("perks.h.next"), 7),
                new Pad(M("perks.h.effect"), 36), M("perks.h.when")));
            foreach (var id in Content.Perks)
            {
                int lv = p.PerkLevel(id);
                bool max = Prestige.AtMax(id, lv);
                object next = max ? (object)M("perks.max") : Content.PerkCost(lv).ToString(Inv);
                object eff = max ? (object)Prestige.Effect(id, lv) : Prestige.Change(id, lv);
                string when = Content.PerkNextRun(id) ? "perks.when_next" : id == "rich-soil" ? "perks.when_plant" : "perks.when_now";
                lines.Add(Joined.Row(new Pad(id, 17), new Pad(lv.ToString(Inv), 7), new Pad(next, 7), new Pad(eff, 36), M(when)));
            }
            lines.Add(M("perks.hint"));
            return Info(r, M("t.perks", p.Fertility), lines.ToArray());
        }

        // ------------------------------------------------------------------ info

        static CommandResult Status(CommandContext c)
        {
            var r = new CommandResult();
            var s = c.State;
            long now = s.GameMinute;
            int open = 0;
            foreach (var f in s.Fields) open += f.Open;
            object storage = s.StorageUsed > 0 ? (object)Engine.CropList(s.Storage) : "-";
            object seeds = s.SeedTotal > 0 ? (object)Engine.CropList(s.Seeds) : "-";
            return Info(r, M("t.status", s.Run, GameClock.Day(now)),
                M("status.gold", G(s.Gold), s.StorageUsed, s.StorageCapacity, s.SeedTotal),
                M("status.storage", storage),
                M("status.seeds", seeds),
                M("status.fields", s.Fields.Count, open, s.Fields.Count * Content.PlotsPerField, s.ToolLevel("can"), s.ToolLevel("bag"), s.ToolLevel("sickle")),
                M("status.time", M("season." + GameClock.Season(now)), GameClock.SeasonDay(now), GameClock.Time(now), c.Profile.Settings.Timescale, new Dur(Engine.OfflineCapHours(s) * 3600)),
                s.Farm.Controller > 0
                    ? M("status.auto", Rules.Active(s).Count, Rules.Slots(s), s.Rules.Count, CheckEvery(c))
                    : M("status.auto_none", s.Rules.Count),
                M("status.records", G(Records.NetWorth(s)), G(s.PeakNetWorth), s.PeakDay, c.Profile.Fertility));
        }

        static CommandResult Help(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            if (a.Length >= 2)
            {
                string name = a[1].TrimStart('/').ToLowerInvariant();
                if (Array.IndexOf(Names, name) >= 0)
                    return Info(r, M("t.help_cmd", "/" + name), M("usage." + name), HelpText(name), M("ex." + name));
                if (Array.IndexOf(Items, name) >= 0) return ItemHelp(c, name);
                if (Array.IndexOf(Later, name) >= 0)
                    return Fail(r, M("t.help_cmd", "/" + name), M("err.not_yet", "/" + name));
                return Fail(r, M("t.unknown"), M("err.unknown_cmd", "/" + name));
            }
            return Info(r, M("t.help"), M("help.farm"), M("help.trade"), M("help.grow"), M("help.automation"), M("help.progress"), M("help.items"), M("help.other"));
        }

        // ------------------------------------------------------------------ /help <장비> (M4 K24, 사용자 요청)

        /// <summary>/help &lt;이름&gt;: 농기구·창고·장비가 하는 일, 가격, 쓰는 법, 지금 상태.</summary>
        static CommandResult ItemHelp(CommandContext c, string id)
        {
            var r = new CommandResult();
            var s = c.State;
            var lines = new List<object>();
            lines.Add(M("item.head", ItemName(id), M("item.kind." + ItemKind(id)), ItemPrice(id)));
            lines.Add(ItemDesc(id));
            lines.Add(M("item_ex." + id));
            lines.Add(ItemNow(s, id));
            bool rule = id == "harvester" || id == "planter" || id == "shipping-bin" || id == "auto-buyer";
            if (rule && s.Farm.Controller <= 0) lines.Add(M("item.needs_controller", G(Content.ControllerCost(0))));
            return Info(r, M("t.help_cmd", id), lines.ToArray());
        }

        /// <summary>하는 일. 수치는 Content에서 넣는다.</summary>
        static Msg ItemDesc(string id)
        {
            switch (id)
            {
                case "storage":
                    return M("item.storage", Content.StorageCapacity(0), Content.StorageCapacity(1), Content.StorageCapacity(2));
                case "sprinkler":
                    return M("item.sprinkler", Farming.Reach(Content.Cols + Content.Cols / 2, Content.MaxSprinklerLevel).Count);
                case "controller":
                    return M("item.controller", Content.ControllerSlots(1), Content.ControllerInterval(1), Content.ControllerInterval(2), Content.ControllerInterval(3));
                case "caretaker":
                    var caps = new List<string>();
                    for (int lv = 0; lv <= Content.MaxCaretakerLevel; lv++) caps.Add(NumberFormat.Num(Content.CaretakerCap(lv)));
                    return M("item.caretaker", string.Join(" → ", caps.ToArray()));
            }
            return M("item." + id);
        }

        /// <summary>농기구·창고는 tool.*, 장비는 eq.* 이름.</summary>
        static Msg ItemName(string id)
        {
            return Content.IsTool(id) || id == "storage" ? ToolName(id) : EqName(id);
        }

        static string ItemKind(string id)
        {
            if (Content.IsTool(id)) return "tool";
            if (id == "storage") return "storage";
            if (id == "sprinkler") return "plot";
            if (Content.IsFieldEquipment(id)) return "field";
            return "farm";
        }

        /// <summary>가격: 레벨형은 "Lv2 200G · Lv3 5,000G"처럼 레벨별로.</summary>
        static object ItemPrice(string id)
        {
            var parts = new List<object>();
            if (Content.IsTool(id))
            {
                for (int lv = 1; lv < Content.MaxToolLevel; lv++) parts.Add(M("item.lv_price", lv + 1, G(Content.ToolUpgradeCost(lv))));
                return new Joined(parts);
            }
            if (id == "storage")
            {
                for (int lv = 0; lv < 3; lv++) parts.Add(M("item.lv_price", lv + 1, G(Content.StorageUpgradeCost(lv))));
                parts.Add(M("item.price_more", Content.MaxStorageLevel));
                return new Joined(parts);
            }
            if (id == "sprinkler")
            {
                parts.Add(G(Content.EquipmentCost(id)));
                for (int lv = 1; lv < Content.MaxSprinklerLevel; lv++) parts.Add(M("item.lv_price", lv + 1, G(Content.SprinklerUpgradeCost(lv))));
                return new Joined(parts);
            }
            if (id == "controller")
            {
                for (int lv = 0; lv < 3; lv++) parts.Add(M("item.lv_price", lv + 1, G(Content.ControllerCost(lv))));
                parts.Add(M("item.price_more", Content.MaxControllerLevel));
                return new Joined(parts);
            }
            if (id == "caretaker")
            {
                for (int lv = 0; lv < Content.MaxCaretakerLevel; lv++) parts.Add(M("item.lv_price", lv + 1, G(Content.CaretakerCost(lv))));
                return new Joined(parts);
            }
            return G(Content.EquipmentCost(id));
        }

        /// <summary>지금 상태: 레벨, 가진 구역, 보관함·설치 수.</summary>
        static Msg ItemNow(GameState s, string id)
        {
            if (Content.IsTool(id)) return M("item.now_level", s.ToolLevel(id));
            if (id == "storage") return M("item.now_storage", s.StorageLevel, s.StorageUsed, s.StorageCapacity);
            if (id == "sprinkler")
            {
                int placed = 0;
                foreach (var f in s.Fields) placed += f.SprinklerCount;
                return M("item.now_sprinkler", s.BoxSprinklers, placed);
            }
            if (Content.IsFieldEquipment(id))
            {
                var have = new List<string>();
                for (int i = 0; i < s.Fields.Count; i++) if (s.Fields[i].Has(id)) have.Add("f" + (i + 1).ToString(Inv));
                if (have.Count == 0) return M("item.now_none");
                return M("item.now_fields", string.Join(", ", have.ToArray()), have.Count, s.Fields.Count);
            }
            if (id == "controller")
            {
                int lv = s.Farm.Controller;
                if (lv <= 0) return M("item.now_none");
                return M("item.now_controller", lv, Rules.Slots(s), Content.ControllerInterval(lv));
            }
            if (id == "caretaker") return M("item.now_caretaker", s.Farm.Caretaker, NumberFormat.Num(Engine.OfflineCapHours(s)));
            return s.Farm.Has(id) ? M("item.now_have") : M("item.now_none");
        }

        /// <summary>/save, /save export [경로], /save import &lt;경로&gt; (M5 K6, K7). 경로는 둘째 낱말 뒤 전부(공백 포함).</summary>
        static CommandResult SaveCmd(string line, string[] a)
        {
            var r = new CommandResult();
            if (a.Length == 1)
            {
                r.Action = UiAction.SaveInfo;
                return r;
            }
            string sub = a[1].ToLowerInvariant();
            if (sub != "export" && sub != "import") return Usage(r, "save");
            int at = line.IndexOf(a[1], line.IndexOf(a[0], StringComparison.Ordinal) + a[0].Length, StringComparison.Ordinal);
            string rest = at >= 0 ? line.Substring(at + a[1].Length).Trim() : "";
            if (sub == "import" && rest.Length == 0) return Usage(r, "save");
            r.Action = sub == "export" ? UiAction.SaveExport : UiAction.SaveImport;
            r.Path = rest;
            return r;
        }

        static CommandResult Config(CommandContext c, string[] a)
        {
            var r = new CommandResult();
            var st = c.Profile.Settings;
            if (a.Length == 1)
            {
                return Info(r, M("t.config"),
                    M("config.lang", st.Lang),
                    M("config.timescale", st.Timescale),
                    M("config.confirm", st.Confirm ? "on" : "off"),
                    M("config.bosskey", st.BossKey));
            }
            string key = a[1].ToLowerInvariant();
            if (a.Length < 3) return Fail(r, M("t.config"), M("err.config"));
            string v = a[2];
            string old;
            switch (key)
            {
                case "lang":
                    v = v.ToLowerInvariant();
                    if (v != "en" && v != "ko") return Fail(r, M("t.config"), M("err.lang"));
                    old = st.Lang;
                    st.Lang = v;
                    r.Action = UiAction.SettingsChanged;
                    return Ok(r, M("t.config"), M("config.changed", "lang", old, v));
                case "timescale":
                    int ts;
                    if (!int.TryParse(v, NumberStyles.None, Inv, out ts) || ts < Content.MinTimescale || ts > Content.MaxTimescale)
                        return Fail(r, M("t.config"), M("err.timescale", Content.MinTimescale, Content.MaxTimescale));
                    old = st.Timescale.ToString(Inv);
                    st.Timescale = ts;
                    r.Action = UiAction.SettingsChanged;
                    return Ok(r, M("t.config"), M("config.changed", "timescale", old, ts.ToString(Inv)));
                case "confirm":
                    v = v.ToLowerInvariant();
                    if (v != "on" && v != "off") return Fail(r, M("t.config"), M("err.config"));
                    old = st.Confirm ? "on" : "off";
                    st.Confirm = v == "on";
                    r.Action = UiAction.SettingsChanged;
                    return Ok(r, M("t.config"), M("config.changed", "confirm", old, v));
                case "bosskey":
                    KeyCombo k;
                    if (!KeyCombo.TryParse(v, out k)) return Fail(r, M("t.config"), M("err.bosskey", v));
                    r.PreviousBossKey = st.BossKey;
                    st.BossKey = k.Display;
                    r.Action = UiAction.BossKeyChanged;
                    return Ok(r, M("t.config"), M("config.changed", "bosskey", r.PreviousBossKey, k.Display));
            }
            return Fail(r, M("t.config"), M("err.config"));
        }

        static CommandResult Reset()
        {
            var r = new CommandResult();
            var first = new PromptRequest();
            first.Question = M("confirm.reset1");
            first.Options.Add(M("opt.yes"));
            first.Options.Add(M("opt.no"));
            first.Default = 1;
            first.OnSelect = delegate(int i)
            {
                if (i != 0) return Cancelled();
                var r2 = new CommandResult();
                var second = new PromptRequest();
                second.Question = M("confirm.reset2");
                second.Options.Add(M("opt.yes"));
                second.Options.Add(M("opt.no"));
                second.Default = 1;
                second.OnSelect = delegate(int j)
                {
                    if (j != 0) return Cancelled();
                    var r3 = new CommandResult();
                    r3.Action = UiAction.Reset;
                    return r3;
                };
                r2.Prompt = second;
                return r2;
            };
            r.Prompt = first;
            return r;
        }
    }
}
