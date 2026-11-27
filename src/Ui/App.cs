using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LupinFarm.Core;
using LupinFarm.Data;
using LupinFarm.Storage;

namespace LupinFarm.Ui
{
    /// <summary>UI 상태 기계: 보통, 선택지, 보스 모드. 입력 → 명령 → 로그 → 화면 (03 5절).</summary>
    internal sealed class App
    {
        const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;
        const ushort VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
        const ushort VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        const ushort VK_DELETE = 0x2E, VK_F2 = 0x71, VK_C = 0x43;
        const int MaxCandRows = 6;

        static readonly string[] SpinGlyphs = { "✻", "✶", "✳", "✢" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly SaveStore store;
        GameState state;
        Profile profile;
        bool hadSave;
        bool loadedFromBackup;
        bool saveCorrupt;
        bool saveErrorShown;

        int viewField;
        bool folded;
        bool bossMode;
        bool exitRequested;

        readonly LogView log = new LogView();
        readonly InputBox input = new InputBox();
        List<Candidate> cands = new List<Candidate>();
        int candSel;
        int candStart;
        bool candDismissed;
        string candKey;
        PromptRequest prompt;
        int promptSel;

        /// <summary>지금 입력에 한글 자판 글자를 영문 키로 바꿔 넣은 적이 있는지 (안내 줄, M5 K2).</summary>
        bool imeFixed;
        RuleRun lastRun;
        long lastRunAt = -1;

        /// <summary>Lupin Bloom 장면 (M4 K11): 시작 시각과 장면이 끝난 뒤 남길 로그. null이면 장면 없음.</summary>
        DateTime bloomStart;
        List<LogEntry> bloomLogs;

        /// <summary>--frame용 고정 날짜. null이면 오늘(로컬).</summary>
        string todayOverride;

        DateTime lastTickUtc;
        double gameAcc;
        DateTime lastSaveUtc;
        DateTime lastRenderUtc;
        DateTime ctrlCAt = DateTime.MinValue;

        readonly ScreenBuffer buf = new ScreenBuffer();
        readonly Hits hits = new Hits();
        bool dirty = true;
        BossKey boss;
        Native.ConsoleCtrlDelegate ctrlHandler;
        readonly object sync = new object();
        int cursorX, cursorY;
        bool cursorVisible;
        int winTop;

        string Lang
        {
            get { return profile.Settings.Lang; }
        }

        int Timescale
        {
            get { return profile.Settings.Timescale; }
        }

        public App(SaveStore store)
        {
            this.store = store;
            profile = store.LoadProfile() ?? Profile.New();
            hadSave = store.HasSave;
            if (hadSave)
            {
                state = store.LoadSave(out loadedFromBackup);
                if (state == null)
                {
                    saveCorrupt = true;
                    hadSave = false;
                }
            }
            if (state == null) state = GameState.NewRun(profile.CurrentRun, Seed(), profile.Perks);
            // 영구 보너스의 원본은 프로필이다 (M4 K13)
            state.Perks = new Dictionary<string, int>(profile.Perks);
            input.SetHistory(profile.History);
        }

        App(GameState s, Profile p)
        {
            state = s;
            profile = p;
            hadSave = true;
            input.SetHistory(profile.History);
        }

        static long Seed()
        {
            return (DateTime.UtcNow.Ticks & 0x7FFFFFFF) | 1;
        }

        string Today()
        {
            return todayOverride ?? DateTime.Now.ToString("yyyy-MM-dd", Inv);
        }

        /// <summary>게임 상태를 프로필 기록(판·일별·도감·업적)에 옮기고, 생긴 이벤트를 로그에 남긴다 (M4 K5).</summary>
        void Observe()
        {
            foreach (var l in Records.Sync(state, profile, Today())) AddLog(l);
        }

        /// <summary>진행에서 지나간 체크포인트 이벤트 (M4 K3).</summary>
        void CheckpointLogs(AdvanceReport r)
        {
            if (r.Checkpoints.Count == 0) return;
            foreach (var l in Records.CheckpointLogs(state, profile, r, Today())) AddLog(l);
        }

        /// <summary>장면 프레임. 장면이 없으면 -1. 끝났으면 남겨 둔 로그를 내보낸다.</summary>
        int BloomFrame(DateTime nowUtc)
        {
            if (bloomLogs == null) return -1;
            double el = (nowUtc - bloomStart).TotalSeconds;
            if (folded || el < 0 || el >= Content.BloomFrameSeconds * Content.BloomFrames)
            {
                var logs = bloomLogs;
                bloomLogs = null;
                foreach (var l in logs) AddLog(l);
                return -1;
            }
            return (int)(el / Content.BloomFrameSeconds);
        }

        string S(string key, params object[] args)
        {
            return Localizer.S(Lang, key, args);
        }

        string R(object o)
        {
            return Localizer.Resolve(Lang, o);
        }

        double RealSeconds(long gameMinutes)
        {
            return GameClock.RealSecondsFromGame(gameMinutes, Timescale);
        }

        // ================================================================== 실행

        public void Run()
        {
            DateTime now = DateTime.UtcNow;
            double cap = Engine.OfflineCapHours(state);
            AdvanceReport off = null;
            double offReal = 0, offApplied = 0;
            DateTime saved;
            // 오늘 기록을 먼저 만들어 꺼져 있던 동안의 판매가 오늘로 들어가게 한다 (M4 K4)
            var startLogs = Records.Sync(state, profile, Today());
            List<object> extra = null;
            if (hadSave && TryUtc(state.SavedAtUtc, out saved))
            {
                offReal = Math.Max(0, (now - saved).TotalSeconds);
                long gm = Engine.OfflineGameMinutes(offReal, cap, Timescale, out offApplied);
                off = Engine.Advance(state, gm);
                extra = Records.CheckpointSummary(state, profile, off, Today(), startLogs);
            }
            AddWelcome(off, offReal, offApplied, cap, extra);
            foreach (var l in startLogs) AddLog(l);
            if (loadedFromBackup) AddLog(LogEntry.Make(LogKind.Event, new Msg("t.save"), new Msg("save.backup")));
            if (saveCorrupt) AddLog(LogEntry.Make(LogKind.Error, new Msg("t.save"), new Msg("save.corrupt")));
            Observe();
            lastTickUtc = now;
            SaveAll();

            ctrlHandler = OnConsoleCtrl;
            Native.SetConsoleCtrlHandler(ctrlHandler, true);

            Terminal.SetTitle("Windows PowerShell");
            string cwd;
            try
            {
                cwd = Environment.CurrentDirectory;
            }
            catch (Exception)
            {
                cwd = "C:\\";
            }
            Terminal.Write(Terminal.Esc + "[H" + Terminal.Esc + "[2J" + Terminal.Esc + "[3J" + "PS " + cwd + "> ");
            Terminal.EnterAlt();

            boss = new BossKey();
            BindBossKey(null);
            buf.Invalidate();

            while (!exitRequested)
            {
                var events = InputReader.Poll(30);
                lock (sync)
                {
                    foreach (var e in events) Handle(e);
                    if (boss.TakeToggle()) ToggleBoss();
                    Tick();
                    if ((DateTime.UtcNow - lastSaveUtc).TotalSeconds >= 30) SaveAll();
                    if (!bossMode && !exitRequested && (dirty || (DateTime.UtcNow - lastRenderUtc).TotalMilliseconds >= 1000)) Present();
                }
            }

            lock (sync) SaveAll();
            boss.Stop();
            if (Terminal.InAlt) Terminal.LeaveAlt();
            Terminal.Write("\r" + Terminal.Esc + "[2K");
        }

        bool OnConsoleCtrl(uint type)
        {
            if (type == Native.CTRL_CLOSE_EVENT || type == Native.CTRL_LOGOFF_EVENT
                || type == Native.CTRL_SHUTDOWN_EVENT || type == Native.CTRL_BREAK_EVENT)
            {
                try
                {
                    lock (sync) SaveAll();
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        /// <summary>예외로 끝날 때 저장을 시도한다.</summary>
        public void EmergencySave()
        {
            try
            {
                if (boss != null) boss.Stop();
                lock (sync) SaveAll();
            }
            catch (Exception)
            {
            }
        }

        void Tick()
        {
            DateTime now = DateTime.UtcNow;
            double el = (now - lastTickUtc).TotalSeconds;
            lastTickUtc = now;
            if (bloomLogs != null) dirty = true; // 장면 프레임(0.7초)을 놓치지 않게
            if (el <= 0) return;

            if (el > Content.SleepGapSeconds)
            {
                foreach (var l in CatchUp(el)) AddLog(l);
                Observe();
                SaveAll();
                return;
            }

            gameAcc += GameClock.GameMinutesFromReal(el, Timescale);
            long m = (long)Math.Floor(gameAcc);
            if (m <= 0) return;
            gameAcc -= m;
            var r = Engine.Advance(state, m);
            foreach (var l in Engine.ReportLogs(state, r)) AddLog(l);
            foreach (var run in r.Runs) AddRun(run);
            CheckpointLogs(r);
            Observe();
            dirty = true;
        }

        /// <summary>
        /// 실제 el초 동안 멈춰 있던 판을 상한까지 진행하고 부재 중 요약 로그를 돌려준다
        /// (PC 절전에서 깨어남, 가져온 세이브 M5 K7).
        /// </summary>
        List<LogEntry> CatchUp(double el)
        {
            var logs = new List<LogEntry>();
            double cap = Engine.OfflineCapHours(state);
            double applied;
            long gm = Engine.OfflineGameMinutes(el, cap, Timescale, out applied);
            gameAcc = 0;
            var rep = Engine.Advance(state, gm);
            var events = new List<LogEntry>();
            var extra = Records.CheckpointSummary(state, profile, rep, Today(), events);
            logs.Add(LogEntry.Make(LogKind.Event, new Msg("t.offline", new Dur(el)), Engine.OfflineLines(state, el, applied, cap, rep, extra).ToArray()));
            logs.AddRange(events);
            return logs;
        }

        void SaveAll()
        {
            if (store == null) return;
            try
            {
                state.SavedAtUtc = DateTime.UtcNow.ToString("o", Inv);
                profile.CurrentRun = state.Run;
                store.Save(state);
                store.SaveProfile(profile);
                saveErrorShown = false;
            }
            catch (Exception ex)
            {
                if (!saveErrorShown)
                {
                    saveErrorShown = true;
                    store.LogError(ex);
                    AddLog(LogEntry.Make(LogKind.Error, new Msg("t.save"), new Msg("save.failed", ex.Message)));
                }
            }
            lastSaveUtc = DateTime.UtcNow;
        }

        static bool TryUtc(string s, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(s)) return false;
            if (!DateTime.TryParse(s, Inv, DateTimeStyles.RoundtripKind, out utc)) return false;
            utc = utc.ToUniversalTime();
            return true;
        }

        void BindBossKey(string previous)
        {
            if (boss == null) return;
            string err;
            if (boss.Start(profile.Settings.BossKey, out err)) return;
            if (previous != null)
            {
                AddLog(LogEntry.Make(LogKind.Error, new Msg("t.config"), new Msg("config.bosskey_fail", profile.Settings.BossKey, previous)));
                profile.Settings.BossKey = previous;
                boss.Start(previous, out err);
            }
            else
            {
                AddLog(LogEntry.Make(LogKind.Error, new Msg("t.config"), new Msg("bosskey.unavailable", profile.Settings.BossKey)));
            }
        }

        void ToggleBoss()
        {
            if (!bossMode)
            {
                bossMode = true;
                Terminal.LeaveAlt();
            }
            else
            {
                bossMode = false;
                Terminal.EnterAlt();
                buf.Invalidate();
                dirty = true;
            }
        }

        void DoReset()
        {
            if (store != null) store.DeleteAll();
            profile = Profile.New();
            state = GameState.NewRun(1, Seed());
            viewField = 0;
            folded = false;
            prompt = null;
            bloomLogs = null;
            lastRun = null;
            log.Clear();
            input.Clear();
            input.SetHistory(profile.History);
            hadSave = false;
            AddLog(LogEntry.Make(LogKind.Ok, new Msg("t.reset"), new Msg("reset.done")));
            AddWelcome(null, 0, 0, 0, null);
            BindBossKey(null);
            Observe();
            SaveAll();
        }

        /// <summary>프레스티지 실행 (M4 K9, K11): 새 판으로 바꾸고 장면을 시작한다. 이벤트는 장면이 끝난 뒤 남긴다.</summary>
        void DoPrestige()
        {
            Observe();
            var logs = new List<LogEntry>();
            state = Prestige.Bloom(state, profile, Today(), Seed(), logs);
            viewField = 0;
            lastRun = null;
            lastRunAt = -1;
            gameAcc = 0;
            log.ScrollBy(-1000000);
            bloomLogs = logs;
            bloomStart = DateTime.UtcNow;
            SaveAll();
        }

        // ================================================================== 입력

        void Handle(InputEvent e)
        {
            if (bossMode) return;
            if (e.Kind == InputKind.Resize)
            {
                buf.Invalidate();
                dirty = true;
                return;
            }
            // Lupin Bloom 장면 중에는 입력을 받지 않는다 (보스키는 따로 동작, M4 K11)
            if (bloomLogs != null && !folded) return;
            if (e.Kind == InputKind.Mouse)
            {
                HandleMouse(e);
                return;
            }
            HandleKey(e);
        }

        void HandleKey(InputEvent e)
        {
            dirty = true;
            if (prompt != null)
            {
                PromptKey(e);
                return;
            }
            if (e.Ctrl && !e.Alt && e.Vk == VK_C)
            {
                CtrlC();
                return;
            }

            switch (e.Vk)
            {
                case VK_RETURN:
                    Enter();
                    break;
                case VK_TAB:
                    if (CandVisible) Fill();
                    break;
                case VK_ESCAPE:
                    if (CandVisible) candDismissed = true;
                    else input.Clear();
                    break;
                case VK_UP:
                    if (CandVisible) candSel = (candSel - 1 + cands.Count) % cands.Count;
                    else input.HistoryUp();
                    break;
                case VK_DOWN:
                    if (CandVisible) candSel = (candSel + 1) % cands.Count;
                    else input.HistoryDown();
                    break;
                case VK_LEFT: input.Left(); break;
                case VK_RIGHT: input.Right(); break;
                case VK_HOME: input.Home(); break;
                case VK_END: input.End(); break;
                case VK_BACK: input.Backspace(); break;
                case VK_DELETE: input.Delete(); break;
                case VK_PRIOR:
                    if (e.Shift) log.ScrollBy(PageRows());
                    else ChangeField(viewField - 1);
                    break;
                case VK_NEXT:
                    if (e.Shift) log.ScrollBy(-PageRows());
                    else ChangeField(viewField + 1);
                    break;
                case VK_F2:
                    folded = !folded;
                    break;
                default:
                    char ch = e.Ch;
                    if (ch >= ' ' && ch != (char)127 && !(e.Ctrl && !e.Alt))
                    {
                        if (ch == '?' && input.Text.Length == 0) AddLog(Commands.Shortcuts(profile));
                        else input.Insert(TypedKeys(ch));
                    }
                    break;
            }
            UpdateCands();
        }

        /// <summary>
        /// 한/영이 한글일 때 들어온 글자를 두벌식 자리의 영문 키로 바꾼다. `/save export|import` 뒤의 경로는 그대로 (M5 K1).
        /// </summary>
        string TypedKeys(char ch)
        {
            if (!HangulKeys.IsHangul(ch) || HangulKeys.KeepsRawPath(input.Text.Substring(0, input.Caret))) return ch.ToString();
            imeFixed = true;
            return HangulKeys.Keys(ch);
        }

        int PageRows()
        {
            return Math.Max(3, (buf.Height > 0 ? buf.Height : 30) - 22);
        }

        void HandleMouse(InputEvent e)
        {
            int x = e.X, y = e.Y - winTop;
            if (e.Wheel != 0)
            {
                if (hits.Log.Contains(x, y))
                {
                    log.ScrollBy(e.Wheel > 0 ? 3 : -3);
                    dirty = true;
                }
                return;
            }
            if (!e.Click || prompt != null) return;
            if (hits.Prev.Contains(x, y))
            {
                ChangeField(viewField - 1);
                return;
            }
            if (hits.Next.Contains(x, y))
            {
                ChangeField(viewField + 1);
                return;
            }
            foreach (var d in hits.Dots)
            {
                if (d.Key.Contains(x, y))
                {
                    ChangeField(d.Value);
                    return;
                }
            }
            if (hits.Grid.Contains(x, y)) ClickPlot(hits.Grid, hits.GridField, x, y);
            else if (hits.Grid2.Contains(x, y)) ClickPlot(hits.Grid2, hits.Grid2Field, x, y);
        }

        /// <summary>칸을 누르면 칸 이름을 넣는다. 보는 구역이 아니면(넓은 창의 다른 구역) `f2.B3`처럼 (M5 K3).</summary>
        void ClickPlot(Rect grid, int field, int x, int y)
        {
            int col = (x - grid.X) / Sprites.Size;
            int row = (y - grid.Y) / (Sprites.Size / 2);
            int idx = row * Content.Cols + col;
            if (idx < 0 || idx >= Content.PlotsPerField) return;
            int c = input.Caret;
            if (c > 0 && input.Text[c - 1] != ' ') input.Insert(" ");
            string name = RangeParser.CellName(idx);
            if (field >= 0 && field != viewField) name = "f" + (field + 1).ToString(Inv) + "." + name;
            input.Insert(name);
            UpdateCands();
            dirty = true;
        }

        void ChangeField(int i)
        {
            if (i < 0 || i >= state.Fields.Count) return;
            viewField = i;
            dirty = true;
        }

        void CtrlC()
        {
            DateTime now = DateTime.UtcNow;
            if (input.Text.Length > 0)
            {
                input.Clear();
                ctrlCAt = DateTime.MinValue;
                return;
            }
            if ((now - ctrlCAt).TotalSeconds <= 2)
            {
                exitRequested = true;
                return;
            }
            ctrlCAt = now;
        }

        void Enter()
        {
            // 단어를 치는 중일 때만 Enter로 후보를 채운다. 빈 자리(공백 뒤)에서는 실행한다.
            if (CandVisible && input.Text.Length > candStart)
            {
                string token = input.Text.Substring(candStart);
                bool exact = false;
                foreach (var c in cands)
                    if (string.Equals(c.Text, token, StringComparison.OrdinalIgnoreCase)) exact = true;
                if (!exact)
                {
                    Fill();
                    return;
                }
            }
            string line = input.Text.Trim();
            if (line.Length == 0) return;
            input.Clear();
            profile.AddHistory(line);
            input.SetHistory(profile.History);
            Execute(line);
            SaveAll();
        }

        void Execute(string line)
        {
            AddLog(LogEntry.Input(line));
            Observe();
            var ctx = new CommandContext();
            ctx.State = state;
            ctx.Profile = profile;
            ctx.ViewField = viewField;
            Apply(Commands.Execute(ctx, line));
        }

        void Apply(CommandResult res)
        {
            foreach (var l in res.Logs) AddLog(l);
            if (res.ViewField >= 0 && res.ViewField < state.Fields.Count) viewField = res.ViewField;
            if (res.Prompt != null)
            {
                prompt = res.Prompt;
                promptSel = Math.Max(0, Math.Min(prompt.Default, prompt.Options.Count - 1));
            }
            switch (res.Action)
            {
                case UiAction.ToggleHide:
                    folded = !folded;
                    break;
                case UiAction.ClearLog:
                    log.Clear();
                    break;
                case UiAction.Exit:
                    exitRequested = true;
                    break;
                case UiAction.BossKeyChanged:
                    BindBossKey(res.PreviousBossKey);
                    break;
                case UiAction.Reset:
                    DoReset();
                    break;
                case UiAction.Prestige:
                    DoPrestige();
                    break;
                case UiAction.SaveInfo:
                    SaveInfo();
                    break;
                case UiAction.SaveExport:
                    SaveExport(res.Path);
                    break;
                case UiAction.SaveImport:
                    AskImport(res.Path);
                    break;
            }
            if (res.Action != UiAction.Reset && res.Action != UiAction.Prestige) Observe();
            dirty = true;
        }

        void PromptKey(InputEvent e)
        {
            int n = prompt.Options.Count;
            if (e.Vk == VK_UP) promptSel = (promptSel - 1 + n) % n;
            else if (e.Vk == VK_DOWN) promptSel = (promptSel + 1) % n;
            else if (e.Vk == VK_RETURN) Choose(promptSel);
            else if (e.Vk == VK_ESCAPE || (e.Ctrl && e.Vk == VK_C)) Choose(n - 1);
            else if (e.Ch >= '1' && e.Ch <= '9' && e.Ch - '1' < n) Choose(e.Ch - '1');
        }

        void Choose(int i)
        {
            var p = prompt;
            prompt = null;
            Apply(p.OnSelect(i));
            SaveAll();
        }

        bool CandVisible
        {
            get { return !candDismissed && prompt == null && cands.Count > 0; }
        }

        void UpdateCands()
        {
            if (input.Text.Length == 0) imeFixed = false;
            string key = input.Text + "\u0001" + input.Caret.ToString(Inv);
            if (key == candKey) return;
            candKey = key;
            candDismissed = false;
            cands = Completion.Compute(input.Text, input.Caret, state, profile, viewField, ExportFiles, out candStart);
            candSel = 0;
        }

        /// <summary>/save import 자동완성용 내보낸 파일 (최근 순, M5 K7).</summary>
        List<FileInfo> ExportFiles()
        {
            return store != null ? store.ListExports() : new List<FileInfo>();
        }

        void Fill()
        {
            if (!CandVisible) return;
            var c = cands[candSel];
            input.SetText(input.Text.Substring(0, candStart) + c.Text + " ");
            UpdateCands();
        }

        // ================================================================== 내보내기·가져오기 (M5 K6~K8)

        /// <summary>--frame에서 보여 줄 저장 폴더 (실제 폴더는 건드리지 않는다).</summary>
        const string PreviewDir = "C:\\Users\\me\\AppData\\Local\\LupinFarm";

        static Msg M(string key, params object[] args)
        {
            return new Msg(key, args);
        }

        static string Local(DateTime utc)
        {
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv);
        }

        void SaveInfo()
        {
            string dir = store != null ? store.Dir : PreviewDir;
            var lines = new List<object>();
            lines.Add(M("save.folder", dir));
            lines.Add(M("save.saved", lastSaveUtc > DateTime.MinValue ? lastSaveUtc.ToLocalTime().ToString("HH:mm:ss", Inv) : "-"));
            var files = store != null ? store.ListExports() : new List<FileInfo>();
            if (files.Count > 0) lines.Add(M("save.exports", files.Count, files[0].Name, Local(files[0].LastWriteTimeUtc)));
            else lines.Add(M("save.exports_none"));
            lines.Add(M("save.how"));
            AddLog(LogEntry.Make(LogKind.Info, M("t.save"), lines.ToArray()));
        }

        /// <summary>판·기록 요약: "run #3 · Day 31 · net worth 330K · 3 run records".</summary>
        static Msg Summary(GameState s, Profile p)
        {
            s.Perks = new Dictionary<string, int>(p.Perks);
            return M("save.summary", s.Run, GameClock.Day(s.GameMinute), NumberFormat.Num(Records.NetWorth(s)), p.Runs.Count);
        }

        void SaveExport(string arg)
        {
            var title = M("t.save_export");
            if (store == null)
            {
                AddLog(LogEntry.Make(LogKind.Error, title, M("save.err.preview")));
                return;
            }
            SaveAll();
            string path = arg;
            try
            {
                path = store.ExportPath(arg, DateTime.Now);
                bool replaced = store.Export(path, state, profile, DateTime.UtcNow);
                AddLog(LogEntry.Make(LogKind.Ok, title, M(replaced ? "save.exported_over" : "save.exported", path), Summary(state, profile), M("save.exported_hint")));
            }
            catch (Exception ex)
            {
                AddLog(LogEntry.Make(LogKind.Error, title, M("save.err.write", path, ex.Message)));
            }
        }

        void AskImport(string arg)
        {
            var title = M("t.save_import");
            if (store == null)
            {
                AddLog(LogEntry.Make(LogKind.Error, title, M("save.err.preview")));
                return;
            }
            string path = store.ImportPath(arg);
            ExportError err;
            var f = SaveStore.ReadExport(path, out err);
            if (err != ExportError.None)
            {
                Msg why;
                switch (err)
                {
                    case ExportError.Missing: why = M("save.err.missing", path); break;
                    case ExportError.Format: why = M("save.err.format", path); break;
                    case ExportError.Version: why = M("save.err.version", f.Version, ExportFile.CurrentVersion); break;
                    default: why = M("save.err.broken", path); break;
                }
                AddLog(LogEntry.Make(LogKind.Error, title, why));
                return;
            }
            DateTime at;
            string when = TryUtc(f.ExportedAt, out at) ? Local(at) : "-";
            var pr = new PromptRequest();
            pr.Question = M("import.q", Path.GetFileName(path));
            pr.Details.Add(M("import.from", Summary(f.Save, f.Profile), when));
            pr.Details.Add(M("import.now", Summary(state, profile)));
            pr.Details.Add(M("import.note"));
            pr.Options.Add(M("opt.import"));
            pr.Options.Add(M("opt.no"));
            pr.Default = 1;
            pr.OnSelect = delegate(int i)
            {
                if (i == 0) return DoImport(path, f);
                var res = new CommandResult();
                res.Logs.Add(LogEntry.Make(LogKind.Info, M("t.cancelled")));
                return res;
            };
            prompt = pr;
            promptSel = pr.Default;
        }

        /// <summary>
        /// 지금 데이터를 export\before-import-….json으로 남긴 뒤 가져온 판·프로필로 바꾼다. 내보낸 뒤 지난 시간은
        /// 꺼져 있던 시간으로 반영한다(상한 적용). 보스키가 다르면 다시 등록한다 (M5 K7).
        /// </summary>
        CommandResult DoImport(string path, ExportFile f)
        {
            var res = new CommandResult();
            var title = M("t.save_import");
            SaveAll();
            string backup = store.ExportPath(SaveStore.StampName("before-import", DateTime.Now), DateTime.Now);
            try
            {
                store.Export(backup, state, profile, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                res.Logs.Add(LogEntry.Make(LogKind.Error, title, M("save.err.write", backup, ex.Message), M("import.kept")));
                return res;
            }
            string oldKey = profile.Settings.BossKey;
            state = f.Save;
            profile = f.Profile;
            state.Perks = new Dictionary<string, int>(profile.Perks);
            viewField = 0;
            prompt = null;
            bloomLogs = null;
            lastRun = null;
            lastRunAt = -1;
            gameAcc = 0;
            input.SetHistory(profile.History);
            if (profile.Settings.BossKey != oldKey) BindBossKey(oldKey);
            res.Logs.Add(LogEntry.Make(LogKind.Ok, title, M("import.done", Path.GetFileName(path)), Summary(state, profile),
                M("import.backup", Path.Combine("export", Path.GetFileName(backup)))));
            DateTime saved;
            if (TryUtc(state.SavedAtUtc, out saved))
            {
                double el = (DateTime.UtcNow - saved).TotalSeconds;
                if (el >= 60) res.Logs.AddRange(CatchUp(el));
            }
            lastTickUtc = DateTime.UtcNow;
            return res;
        }

        // ================================================================== 로그

        void AddLog(LogEntry e)
        {
            log.Add(Paras(e));
            dirty = true;
        }

        /// <summary>규칙 실행 로그. 바로 앞 묶음이 같은 규칙이면 합친다 (M3 K9).</summary>
        void AddRun(RuleRun run)
        {
            if (lastRun != null && lastRunAt == log.Total && lastRun.CanMerge(run))
            {
                lastRun.Merge(run);
                log.ReplaceLast(Paras(Rules.RunLog(lastRun)));
                dirty = true;
                return;
            }
            AddLog(Rules.RunLog(run));
            lastRun = run.Fail == null ? run.Copy() : null;
            lastRunAt = log.Total;
        }

        List<Para> Paras(LogEntry e)
        {
            var paras = new List<Para>();
            if (e.Kind == LogKind.Input)
            {
                var p = new Para();
                p.Indent = 2;
                p.Add("> ", Colors.Gray).Add(e.Raw, Colors.Default);
                paras.Add(p);
            }
            else
            {
                var t = new Para();
                t.Indent = 2;
                t.Add("● ", KindColor(e.Kind));
                AddMarked(t, e.Title);
                paras.Add(t);
                for (int i = 0; i < e.Lines.Count; i++)
                {
                    var q = new Para();
                    q.Indent = 5;
                    q.Add(i == 0 ? "  ⎿  " : "     ", Colors.Gray);
                    AddMarked(q, e.Lines[i]);
                    paras.Add(q);
                }
            }
            return paras;
        }

        /// <summary>값에 붙은 색(Tone)대로 조각을 나눠 붙인다 (M5 K5).</summary>
        void AddMarked(Para p, object o)
        {
            foreach (var sg in Localizer.Segments(Localizer.ResolveMarked(Lang, o), Colors.Default)) p.Segs.Add(sg);
        }

        static int KindColor(LogKind k)
        {
            switch (k)
            {
                case LogKind.Ok: return Colors.Green;
                case LogKind.Error: return Colors.Red;
                case LogKind.Auto: return Colors.Accent;
                case LogKind.Event: return Colors.Yellow;
                default: return Colors.Default;
            }
        }

        /// <summary>시작 환영 상자 (S02).</summary>
        void AddWelcome(AdvanceReport off, double offReal, double offApplied, double cap, List<object> extra)
        {
            var rows = new List<KeyValuePair<string, int>>();
            long now = state.GameMinute;
            rows.Add(new KeyValuePair<string, int>("✻ " + S(hadSave ? "welcome.back" : "welcome.first"), Colors.Accent));
            rows.Add(new KeyValuePair<string, int>("", Colors.Default));
            if (hadSave)
            {
                rows.Add(new KeyValuePair<string, int>("  " + S("welcome.run", state.Run, GameClock.Day(now),
                    S("season." + GameClock.Season(now)), GameClock.SeasonDay(now)), Colors.Default));
                if (off != null && offReal >= 60)
                {
                    var lines = Engine.OfflineLines(state, offReal, offApplied, cap, off, extra);
                    for (int i = 0; i < lines.Count; i++)
                        rows.Add(new KeyValuePair<string, int>((i == 0 ? "  " : "  ⎿  ") + R(lines[i]), Colors.Default));
                }
            }
            else
            {
                rows.Add(new KeyValuePair<string, int>("  " + S("welcome.start1"), Colors.Default));
                rows.Add(new KeyValuePair<string, int>("  " + S("welcome.start2"), Colors.Default));
                rows.Add(new KeyValuePair<string, int>("  " + S("welcome.start3"), Colors.Default));
            }
            rows.Add(new KeyValuePair<string, int>("", Colors.Default));
            rows.Add(new KeyValuePair<string, int>("  " + S("welcome.tip"), Colors.Gray));

            int inner = 0;
            foreach (var r in rows) inner = Math.Max(inner, TextWidth.Of(r.Key));
            int bw = Math.Max(60, Math.Min(110, inner + 4));
            var paras = new List<Para>();
            paras.Add(BoxEdge("╭", "╮", bw, Colors.Accent));
            foreach (var r in rows) paras.Add(BoxLine(r.Key, r.Value, bw, Colors.Accent));
            paras.Add(BoxEdge("╰", "╯", bw, Colors.Accent));
            log.Add(paras);
            dirty = true;
        }

        static Para BoxEdge(string left, string right, int bw, int color)
        {
            var p = new Para();
            p.NoWrap = true;
            p.Add(left + new string('─', bw - 2) + right, color);
            return p;
        }

        static Para BoxLine(string text, int textColor, int bw, int border)
        {
            var p = new Para();
            p.NoWrap = true;
            p.Add("│ ", border).Add(TextWidth.Fit(text, bw - 4), textColor).Add(" │", border);
            return p;
        }

        // ================================================================== 화면

        void Present()
        {
            int w, h;
            Terminal.GetSize(out w, out h, out winTop);
            if (w <= 0 || h <= 0) return;
            buf.Resize(w, h);
            Render(buf, DateTime.UtcNow);
            var sb = new StringBuilder();
            sb.Append(Terminal.Esc).Append("[?25l");
            sb.Append(buf.Flush());
            if (cursorVisible)
            {
                sb.Append(Terminal.Esc).Append('[').Append(cursorY + 1).Append(';').Append(cursorX + 1).Append('H');
                sb.Append(Terminal.Esc).Append("[?25h");
            }
            Terminal.Write(sb.ToString());
            lastRenderUtc = DateTime.UtcNow;
            dirty = false;
        }

        public void Render(ScreenBuffer b, DateTime nowUtc)
        {
            b.Clear();
            hits.Reset();
            cursorVisible = false;
            int W = b.Width, H = b.Height;
            if (W < Content.MinWidth || H < Content.MinHeight)
            {
                b.Put(0, 0, S("ui.small", W, H, Content.MinWidth, Content.MinHeight), Colors.Gray);
                return;
            }
            if (viewField >= state.Fields.Count) viewField = state.Fields.Count - 1;

            int y = 0;
            if (!folded)
            {
                RenderHeader(b, W);
                FarmView.Render(b, 1, state, viewField, Lang, hits, Timescale, BloomFrame(nowUtc), SprinklerPreviewNow());
                y = 1 + FarmView.Height;
            }

            bool showCands = CandVisible;
            int candRows = showCands ? Math.Min(MaxCandRows, cands.Count) : 0;
            int inputTop = H - Math.Max(1, candRows) - 3;
            int logTop = y;
            int logBottom = inputTop - 1;

            if (prompt != null)
            {
                var pl = PromptLines(W);
                // 로그 영역에 다 들어가지 않으면 농장 영역 위로 올라가 덮는다 (프레스티지 상자, M4 K8)
                int pTop = Math.Max(Math.Min(logTop, 1), logBottom - pl.Count + 1);
                for (int i = 0; i < pl.Count && pTop + i <= logBottom; i++) DrawSegs(b, 0, pTop + i, pl[i]);
                logBottom = pTop - 1;
            }
            else
            {
                DrawSpinner(b, logBottom, nowUtc);
                logBottom--;
            }

            int rows = logBottom - logTop + 1;
            if (rows > 0)
            {
                var lines = log.Lines(W - 1);
                int maxScroll = Math.Max(0, lines.Count - rows);
                if (log.Scroll > maxScroll) log.Scroll = maxScroll;
                int start = Math.Max(0, lines.Count - rows - log.Scroll);
                for (int r = 0; r < rows && start + r < lines.Count; r++) DrawSegs(b, 0, logTop + r, lines[start + r]);
                hits.Log = new Rect(0, logTop, W, rows);
            }

            DrawInput(b, inputTop, W);
            if (showCands) DrawCands(b, inputTop + 3, W, candRows);
            else DrawHint(b, H - 1, W, nowUtc);
        }

        /// <summary>입력 중인 스프링클러 설치·업그레이드 명령의 범위 미리 보기 (M5 K9). 선택지가 떠 있으면 없음.</summary>
        SprinklerPreview SprinklerPreviewNow()
        {
            if (prompt != null || input.Text.Length == 0) return null;
            var ctx = new CommandContext();
            ctx.State = state;
            ctx.Profile = profile;
            ctx.ViewField = viewField;
            return Commands.PreviewSprinkler(ctx, input.Text);
        }

        void RenderHeader(ScreenBuffer b, int W)
        {
            long now = state.GameMinute;
            b.Put(1, 0, "✻", Colors.Accent);
            b.Put(3, 0, S("app.name"), Colors.Accent);
            string right = S("header.right", state.Run, S("season." + GameClock.Season(now)), GameClock.SeasonDay(now),
                GameClock.Time(now), NumberFormat.Gold(state.Gold)) + " ";
            b.Put(W - TextWidth.Of(right), 0, right, Colors.Default);
        }

        void DrawSpinner(ScreenBuffer b, int y, DateTime nowUtc)
        {
            var st = Engine.Stats(state, -1);
            string glyph = SpinGlyphs[(int)((nowUtc.Ticks / TimeSpan.TicksPerSecond) % SpinGlyphs.Length)];
            string text;
            if (st.NextMinutes >= 0)
            {
                string cells = RangeParser.Compress(st.NextCells);
                string where = cells ?? S("ui.cells", st.NextCells.Count);
                if (st.NextField != viewField) where = "f" + (st.NextField + 1).ToString(Inv) + "." + where;
                text = S("ui.spin.growing", S("crop." + st.NextCrop), where, Localizer.Duration(Lang, RealSeconds(st.NextMinutes)));
                if (st.Ripe > 0) text += S("ui.spin.suffix_ready", st.Ripe);
            }
            else if (st.Ripe > 0) text = S("ui.spin.ready", st.Ripe);
            else if (st.Dry > 0) text = S("ui.spin.dry", st.Dry);
            else text = S("ui.spin.idle");
            b.Put(0, y, glyph, Colors.Accent);
            b.Put(2, y, text, Colors.Accent);
        }

        void DrawInput(ScreenBuffer b, int y, int W)
        {
            int g = Colors.Gray;
            bool disabled = prompt != null;
            b.Put(0, y, "╭", g);
            b.Fill(1, y, W - 2, "─", g);
            b.Put(W - 1, y, "╮", g);
            b.Put(0, y + 1, "│", g);
            b.Put(W - 1, y + 1, "│", g);
            b.Put(0, y + 2, "╰", g);
            b.Fill(1, y + 2, W - 2, "─", g);
            b.Put(W - 1, y + 2, "╯", g);
            if (imeFixed && input.Text.Length > 0)
            {
                // 자동완성 목록이 안내 줄을 덮어도 보이게 입력창 위 테두리에 쓴다 (M5 K2)
                string note = " " + S("ui.hint.ime") + " ";
                b.Put(W - 2 - TextWidth.Of(note), y, note, Colors.Accent);
            }

            b.Put(2, y + 1, ">", disabled ? Colors.Dim : Colors.Default);
            int tx = 4;
            int avail = W - tx - 2;
            string t = input.Text;
            if (t.Length == 0)
            {
                if (!disabled) b.Put(tx, y + 1, S("ui.placeholder"), Colors.Dim);
                cursorX = tx;
            }
            else
            {
                int caretW = TextWidth.Of(t.Substring(0, input.Caret));
                int offset = caretW > avail - 1 ? caretW - (avail - 1) : 0;
                DrawClipped(b, tx, y + 1, t, offset, avail, disabled ? Colors.Dim : Colors.Default);
                cursorX = tx + caretW - offset;
            }
            cursorY = y + 1;
            cursorVisible = !disabled;
        }

        static void DrawClipped(ScreenBuffer b, int x, int y, string t, int skip, int width, int fg)
        {
            int w = 0;
            for (int i = 0; i < t.Length; i++)
            {
                string piece;
                int cw;
                if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length)
                {
                    piece = t.Substring(i, 2);
                    cw = 2;
                    i++;
                }
                else
                {
                    piece = t[i].ToString();
                    cw = TextWidth.IsWide(t[i]) ? 2 : 1;
                }
                if (w >= skip)
                {
                    if (w - skip + cw > width) break;
                    b.Put(x + w - skip, y, piece, fg);
                }
                w += cw;
            }
        }

        void DrawCands(ScreenBuffer b, int y, int W, int rows)
        {
            int start = candSel >= rows ? candSel - rows + 1 : 0;
            for (int r = 0; r < rows && start + r < cands.Count; r++)
            {
                int i = start + r;
                var c = cands[i];
                bool sel = i == candSel;
                b.Put(2, y + r, sel ? "❯" : " ", Colors.Accent);
                b.Put(4, y + r, TextWidth.Fit(c.Text, 21), sel ? Colors.Accent : Colors.Default);
                b.Put(26, y + r, c.Desc ?? "", Colors.Gray);
            }
        }

        void DrawHint(ScreenBuffer b, int y, int W, DateTime nowUtc)
        {
            if ((nowUtc - ctrlCAt).TotalSeconds <= 2) b.Put(2, y, S("ui.hint.exit"), Colors.Accent);
            else if (log.Scroll > 0) b.Put(2, y, S("ui.hint.scrolled", log.Scroll), Colors.Accent);
            else b.Put(2, y, S("ui.hint.left"), Colors.Gray);
            if (folded)
            {
                var segs = FoldedStatus();
                int w = 0;
                foreach (var sg in segs) w += TextWidth.Of(sg.Text);
                DrawSegs(b, W - w - 1, y, segs);
                return;
            }
            string right = S("ui.hint.right", Lang) + " ";
            b.Put(W - TextWidth.Of(right), y, right, Colors.Gray);
        }

        /// <summary>농장을 접었을 때 안내 줄 오른쪽의 농장 요약 (M3 K16).</summary>
        List<Seg> FoldedStatus()
        {
            long now = state.GameMinute;
            var st = Engine.Stats(state, -1);
            int g = Colors.Gray;
            string sep = S("list.sep");
            var segs = new List<Seg>();
            segs.Add(new Seg(S("fold.time", S("season." + GameClock.Season(now)), GameClock.SeasonDay(now), GameClock.Time(now)), g));
            segs.Add(new Seg(sep, g));
            segs.Add(new Seg(S("fold.ready", st.Ripe), st.Ripe > 0 ? Colors.Green : g));
            segs.Add(new Seg(sep, g));
            segs.Add(new Seg(S("fold.dry", st.Dry), st.Dry > 0 ? Colors.Accent : g));
            segs.Add(new Seg(sep, g));
            segs.Add(new Seg(S("fold.storage", state.StorageUsed, state.StorageCapacity), state.StorageUsed >= state.StorageCapacity ? Colors.Accent : g));
            segs.Add(new Seg(sep, g));
            segs.Add(new Seg(NumberFormat.Gold(state.Gold), g));
            if (state.Farm.Controller > 0)
            {
                segs.Add(new Seg(sep, g));
                segs.Add(new Seg(S("fold.auto", Rules.Active(state).Count), g));
            }
            return segs;
        }

        List<List<Seg>> PromptLines(int W)
        {
            var rows = new List<KeyValuePair<string, int>>();
            rows.Add(new KeyValuePair<string, int>(R(prompt.Question), Colors.Default));
            rows.Add(new KeyValuePair<string, int>("", Colors.Default));
            if (prompt.Details.Count > 0)
            {
                foreach (var d in prompt.Details) rows.Add(new KeyValuePair<string, int>("  " + R(d), Colors.Default));
                rows.Add(new KeyValuePair<string, int>("", Colors.Default));
            }
            for (int i = 0; i < prompt.Options.Count; i++)
            {
                bool sel = i == promptSel;
                rows.Add(new KeyValuePair<string, int>((sel ? "❯ " : "  ") + (i + 1).ToString(Inv) + ". " + R(prompt.Options[i]),
                    sel ? Colors.Accent : Colors.Default));
            }
            rows.Add(new KeyValuePair<string, int>("", Colors.Default));
            rows.Add(new KeyValuePair<string, int>(S("prompt.hint", prompt.Options.Count), Colors.Gray));

            int inner = 0;
            foreach (var r in rows) inner = Math.Max(inner, TextWidth.Of(r.Key));
            int bw = Math.Min(W - 2, Math.Max(60, inner + 4));
            var lines = new List<List<Seg>>();
            lines.Add(BoxEdge("╭", "╮", bw, Colors.Accent).Segs);
            foreach (var r in rows) lines.Add(BoxLine(r.Key, r.Value, bw, Colors.Accent).Segs);
            lines.Add(BoxEdge("╰", "╯", bw, Colors.Accent).Segs);
            return lines;
        }

        static void DrawSegs(ScreenBuffer b, int x, int y, List<Seg> segs)
        {
            foreach (var s in segs) x = b.Put(x, y, s.Text, s.Fg);
        }

        // ================================================================== 개발용 (--frame)

        /// <summary>--frame 화면 폭: wide는 구역 2개가 나란히 보이는 폭 (M5 K3).</summary>
        public static int PreviewWidth(string mode)
        {
            return mode == "wide" ? Content.WideWidth + 6 : Content.MinWidth;
        }

        /// <summary>예시 상태로 화면 한 장을 만든다. mode: "" | "cands" | "prompt" | "folded" | "first".</summary>
        public static App Preview(string lang, string mode)
        {
            var p = Profile.New();
            p.Settings.Lang = lang == "ko" ? "ko" : "en";
            p.Settings.Confirm = mode == "prompt";
            // 판 #3 예시라 도감·업적은 이미 있는 것으로 둔다 (예시 화면에 첫 수확 이벤트가 끼지 않게)
            foreach (var c in new[] { "turnip", "potato", "corn", "carrot" }) DexAdd(p, c, 120, "2026-11-27", 1);
            foreach (var id in new[] { "first-harvest", "gold-1k", "field-2", "first-sprinkler", "first-rule" }) p.Achievements[id] = "2026-11-27";
            var s = GameState.NewRun(3, 12345);
            s.Gold = 5000;
            s.GameMinute = 2 * 1440 + 8 * 60;
            s.Tools["can"] = 2;
            s.Tools["bag"] = 2;
            s.Tools["sickle"] = 2;
            var f = s.Fields[0];
            f.Open = 20;
            f.Bought = 14;
            long now = s.GameMinute;
            for (int i = 0; i < 4; i++) Set(f.Plots[i], "turnip", 2880, now + 600);
            for (int i = 4; i < 6; i++) Set(f.Plots[i], "turnip", 1200, now + 600);
            Set(f.Plots[6], "corn", 7000, now + 900);
            f.Plots[7].WaterUntil = now + 900;
            f.Plots[8].WaterUntil = now + 900;
            for (int i = 10; i < 16; i++) Set(f.Plots[i], "carrot", 3000, now + 1200);
            for (int i = 16; i < 18; i++) Set(f.Plots[i], "carrot", 100, now + 1200);
            Set(f.Plots[18], "corn", 2000, 0);
            s.Fields.Add(FieldState.Create(6));
            s.Storage["turnip"] = 24;
            s.Seeds["turnip"] = 12;
            s.Seeds["corn"] = 30;

            var app = new App(s, p);
            app.todayOverride = "2026-12-10";
            app.hadSave = mode != "first";
            app.AddWelcome(null, 0, 0, 2, null);
            if (mode != "first")
            {
                app.Execute("/water A8:A9");
                app.Execute("/sell turnip 4");
                app.Execute("/buy seed corn 10");
                app.Execute("/harvest A1:A2");
            }
            s.Gold = mode == "prompt" ? 9000 : 1240;
            s.Unlocked.Add("potato");
            s.Unlocked.Add("strawberry");
            s.Storage["potato"] = 6;
            Engine.Advance(s, 1440);

            // M3 장비·규칙 예시 (진행 뒤에 넣어 화면이 그대로 남게 한다)
            f.Plots[9].Sprinkler = 1;
            f.Harvester = true;
            s.Farm.Controller = 2;
            s.Farm.ShippingBin = true;
            s.EquipmentBox[GameState.BoxKey(1)] = 1;
            AddRule(s, "when f1 ready do harvest", true, 12);
            AddRule(s, "when stock turnip >= 20 do sell turnip", true, 3);
            AddRule(s, "when f1 empty do plant turnip", true, 0);
            AddRule(s, "when price corn >= 180 do sell corn", false, 1);

            bool m4 = mode == "records" || mode == "daily" || mode == "rank" || mode == "prestige" || mode == "bloom"
                || mode == "perks" || mode == "dex" || mode == "status";
            if (m4) PreviewRecords(s, p);

            if (mode == "records") app.Execute("/records");
            else if (mode == "daily") app.Execute("/records daily");
            else if (mode == "rank" || mode == "perks" || mode == "dex" || mode == "crops" || mode == "growing") app.Execute("/" + mode);
            else if (mode == "prestige") app.Execute("/prestige");
            else if (mode == "bloom")
            {
                app.DoPrestige();
                app.bloomStart = DateTime.UtcNow.AddSeconds(-1.5);
                app.input.SetText("");
            }
            else if (mode == "cands") app.input.SetText("/up");
            else if (mode == "save") app.Execute("/save");
            else if (mode == "preview") app.input.SetText("/upgrade sprinkler A10");
            else if (mode == "placelv")
            {
                s.EquipmentBox[GameState.BoxKey(3)] = 1;
                app.input.SetText("/place sprinkler B10 ");
            }
            else if (mode == "ime")
            {
                // 한/영이 한글일 때 "/plant t"를 친 모양 (M5 K1)
                foreach (char ch in "/ㅔㅣ뭇 ㅅ") app.input.Insert(app.TypedKeys(ch));
            }
            else if (mode == "wide")
            {
                var f2 = s.Fields[1];
                for (int i = 0; i < 4; i++) Set(f2.Plots[i], "turnip", 2880, s.GameMinute + 600);
                app.input.SetText("/water f2.A");
            }
            else if (mode == "gear") app.Execute("/help harvester");
            else if (mode == "gearcands") app.input.SetText("/help s");
            else if (mode == "seeds") app.input.SetText("/buy seed ");
            else if (mode == "perkcands")
            {
                PreviewRecords(s, p);
                app.input.SetText("/perks buy ");
            }
            else if (mode == "prompt") app.Execute("/upgrade can");
            else if (mode == "market" || mode == "inv" || mode == "map" || mode == "shop" || mode == "status") app.Execute("/" + mode);
            else if (mode == "auto") app.Execute("/auto list");
            else if (mode == "rule") app.input.SetText("/auto add when f1 ready do ");
            else if (mode == "offline") PreviewOffline(app, s);
            else if (mode == "merge")
            {
                // 검사 세 번: 같은 규칙이 연달아 실행되면 한 묶음(×n)이 된다
                for (int k = 0; k < 3; k++)
                {
                    for (int i = 0; i < 4; i++) Set(f.Plots[i], "turnip", 2880, s.GameMinute + 600);
                    var rep = Engine.Advance(s, 30);
                    foreach (var l in Engine.ReportLogs(s, rep)) app.AddLog(l);
                    foreach (var run in rep.Runs) app.AddRun(run);
                }
            }
            else if (mode != "bloom" && mode != "prestige") app.input.SetText("/water B");
            if (mode == "folded") app.folded = true;
            app.UpdateCands();
            return app;
        }

        /// <summary>M4 기록 예시: 판 1·2는 끝났고 지금은 판 3의 Day 31 (04 S15~S20).</summary>
        static void PreviewRecords(GameState s, Profile p)
        {
            const string today = "2026-12-10";
            s.GameMinute = 30 * 1440 + 8 * 60;
            s.Invested = 300000;
            s.Checkpoints["7"] = 15100;
            s.Checkpoints["14"] = 61300;
            s.Checkpoints["28"] = 310000;
            s.PeakNetWorth = 330000;
            s.PeakDay = 30;
            s.Earned = 42000;
            p.Fertility = 8;
            p.Perks["rich-soil"] = 2;
            p.Perks["night-shift"] = 1;
            p.Perks["rule-memory"] = 4;
            s.Perks = new Dictionary<string, int>(p.Perks);

            p.Runs.Add(RunRec(1, "2026-11-27", "2026-12-04", 8900000, 120, 130, 9,
                new double[] { 11800, 52000, 240000, 2100000, 8200000 }));
            p.Runs.Add(RunRec(2, "2026-12-04", "2026-12-08", 1400000, 45, 45, 3,
                new double[] { 12400, 58000, 280000 }));

            string[] dates = { "2026-12-01", "2026-12-02", "2026-12-03", "2026-12-04", "2026-12-04", "2026-12-05", "2026-12-06", "2026-12-07", "2026-12-08", "2026-12-09" };
            int[] runs = { 1, 1, 1, 1, 2, 2, 2, 2, 2, 3 };
            double[] nw = { 3.1e6, 4.8e6, 7.2e6, 8.9e6, 9800, 61000, 240000, 820000, 1.4e6, 188000 };
            double[] earned = { 410000, 520000, 690000, 380000, 2100, 18000, 52000, 96000, 28000, 31500 };
            for (int i = 0; i < dates.Length; i++)
            {
                var d = new DailyRecord();
                d.Date = dates[i];
                d.Run = runs[i];
                d.NetWorth = nw[i];
                d.Earned = earned[i];
                p.Daily.Add(d);
            }
            var today3 = new DailyRecord();
            today3.Date = today;
            today3.Run = 3;
            p.Daily.Add(today3);

            DexAdd(p, "turnip", 1820, "2026-11-27", 1);
            DexAdd(p, "potato", 340, "2026-11-28", 1);
            DexAdd(p, "corn", 95, "2026-11-29", 1);
            DexAdd(p, "carrot", 12, "2026-12-01", 1);
            DexAdd(p, "tomato", 160, "2026-11-30", 1);
            DexAdd(p, "pumpkin", 4, "2026-12-03", 1);
            foreach (var id in new[] { "first-harvest", "gold-1k", "field-2", "first-sprinkler", "first-rule", "new-best" })
                p.Achievements[id] = "2026-11-28";
            Records.Sync(s, p, today);
        }

        static RunRecord RunRec(int run, string start, string end, double peak, int peakDay, int lastDay, long fert, double[] cps)
        {
            var r = new RunRecord();
            r.Run = run;
            r.StartedAt = start;
            r.EndedAt = end;
            r.Peak = peak;
            r.PeakDay = peakDay;
            r.LastDay = lastDay;
            r.Fertility = fert;
            r.Normalize();
            long day = Content.FirstCheckpointDay;
            foreach (var v in cps)
            {
                r.Checkpoints[Records.Key(day)] = v;
                day *= 2;
            }
            return r;
        }

        static void DexAdd(Profile p, string crop, long total, string first, int run)
        {
            var e = new DexEntry();
            e.Total = total;
            e.First = first;
            e.FirstRun = run;
            p.Dex[crop] = e;
        }

        static void AddRule(GameState s, string text, bool on, int runs)
        {
            var r = new RuleState();
            r.Text = text;
            r.On = on;
            r.Runs = runs;
            s.Rules.Add(r);
        }

        /// <summary>꺼져 있던 3시간(상한 2시간) 동안 전자동 농장이 일한 뒤의 시작 환영 상자.</summary>
        static void PreviewOffline(App app, GameState s)
        {
            var f = s.Fields[0];
            f.Planter = true;
            s.Farm.AutoBuyer = true;
            f.Plots[12].Crop = null;
            f.Plots[12].Sprinkler = 3;
            Farming.SprinklerDay(s);
            s.Seeds["turnip"] = 20;
            s.Rules[3].On = true;
            AddRule(s, "when seed turnip < 10 do buy seed turnip 30", true, 0);
            s.Farm.Controller = 3;
            double cap = Engine.OfflineCapHours(s), applied;
            double real = 3 * 3600;
            long gm = Engine.OfflineGameMinutes(real, cap, app.Timescale, out applied);
            var rep = Engine.Advance(s, gm);
            app.log.Clear();
            app.hadSave = true;
            app.AddWelcome(rep, real, applied, cap, null);
        }

        static void Set(PlotState p, string crop, int growth, long waterUntil)
        {
            p.Crop = crop;
            p.Growth = growth;
            p.WaterUntil = waterUntil;
        }
    }
}
