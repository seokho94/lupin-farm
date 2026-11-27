using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LupinFarm.Core;
using LupinFarm.Data;
using LupinFarm.Storage;
using LupinFarm.Ui;

namespace LupinFarm.Tests
{
    /// <summary>M5 편의·밸런스 (M5_편의·밸런스 기능 정의서).</summary>
    public static class M5Tests
    {
        /// <summary>K1: 한글 자판으로 친 글자 → 두벌식 자리의 영문 키.</summary>
        public static void TestHangulKeys()
        {
            Assert.Equal("plant", HangulKeys.ToKeys("ㅔㅣ뭇"), "syllable with final consonant");
            Assert.Equal("/plant turnip A1", HangulKeys.FixLine("/ㅔㅣ뭇 셔구ㅑㅔ A1"), "whole command");
            Assert.Equal("hk", HangulKeys.ToKeys("ㅘ"), "compound vowel = two keys");
            Assert.Equal("rt", HangulKeys.ToKeys("ㄳ"), "compound final = two keys");
            Assert.Equal("ekfr", HangulKeys.ToKeys("닭"), "syllable with compound final");
            Assert.Equal("R", HangulKeys.ToKeys("ㄲ"), "double consonant = Shift");
            Assert.Equal("P", HangulKeys.ToKeys("ㅖ"), "ㅖ = Shift+p");
            Assert.Equal("/plant turnip A1", HangulKeys.FixLine("/plant turnip A1"), "English unchanged");
            Assert.Equal("/save export D:\\백업", HangulKeys.FixLine("/ㄴㅁㅍㄷ ㄷ테ㅐㄳ D:\\백업"), "export path stays as typed");
            Assert.True(HangulKeys.KeepsRawPath("/save import "), "after /save import comes a path");
            Assert.True(!HangulKeys.KeepsRawPath("/save import"), "no space yet: still the subcommand");
            Assert.True(!HangulKeys.KeepsRawPath("/plant "), "other commands convert");

            Game.New();
            Assert.Equal(LogKind.Info, Game.Kind(Game.Run("/ㅗ디ㅔ")), "/help typed with the Korean keyboard");
        }

        static string Flat(List<Seg> line)
        {
            var sb = new StringBuilder();
            foreach (var s in line) sb.Append(s.Text);
            return sb.ToString();
        }

        static List<List<Seg>> Wrap(Para p, int width)
        {
            var lines = new List<List<Seg>>();
            LogView.Wrap(p, width, lines);
            return lines;
        }

        /// <summary>K4: 낱말 사이에서 줄을 바꾸고, 긴 낱말은 글자 단위로.</summary>
        public static void TestWordWrap()
        {
            var p = new Para();
            p.Indent = 5;
            p.Add("  ⎿  ", Colors.Gray).Add("aaa bbb ccc ddd", Colors.Default);
            var lines = Wrap(p, 14);
            Assert.Equal(2, lines.Count, "two lines");
            Assert.Equal("  ⎿  aaa bbb", Flat(lines[0]), "breaks before the word that does not fit");
            Assert.Equal("     ccc ddd", Flat(lines[1]), "next line is indented");

            var q = new Para();
            q.Indent = 5;
            q.Add("  ⎿  ", Colors.Gray).Add(new string('x', 30), Colors.Default);
            lines = Wrap(q, 20);
            Assert.Equal("  ⎿  " + new string('x', 15), Flat(lines[0]), "a word longer than the line is cut");
            Assert.Equal("     " + new string('x', 15), Flat(lines[1]), "rest on the next line");

            var c = new Para();
            c.Indent = 2;
            c.Add("● ", Colors.Green).Add("up ", Colors.Green).Add("down", Colors.Red);
            lines = Wrap(c, 8);
            Assert.Equal("● up", Flat(lines[0]), "colored words wrap too");
            Assert.Equal("  down", Flat(lines[1]), "second line");
            Assert.Equal(Colors.Red, lines[1][lines[1].Count - 1].Fg, "keeps the color");

            var k = new Para();
            k.Indent = 2;
            k.Add("> ", Colors.Gray).Add("가나다 라마바", Colors.Default);
            lines = Wrap(k, 12);
            Assert.Equal("> 가나다", Flat(lines[0]), "Korean is 2 columns wide");
            Assert.Equal("  라마바", Flat(lines[1]), "Korean word moves whole");
        }

        /// <summary>K5: Tone은 로그에서만 색 조각이 되고, 폭 계산과 다른 곳의 글자에는 영향이 없다.</summary>
        public static void TestToneSegments()
        {
            var row = Joined.Row(new Pad(Tone.Good("▲ 5"), 6), "x");
            string marked = Localizer.ResolveMarked("en", row);
            Assert.Equal(7, TextWidth.Of(marked), "marks take no width");
            Assert.Equal("▲ 5   x", Localizer.Resolve("en", row), "plain text has no marks");
            var segs = Localizer.Segments(marked, Colors.Default);
            Assert.Equal(2, segs.Count, "two pieces");
            Assert.Equal("▲ 5", segs[0].Text, "colored piece");
            Assert.Equal(Colors.Green, segs[0].Fg, "good = green");
            Assert.Equal("   x", segs[1].Text, "rest");
            Assert.Equal(Colors.Default, segs[1].Fg, "back to the base color");
            Assert.Equal("x", Localizer.Resolve("en", Tone.BySign(0, "x")), "zero has no tone");
        }

        /// <summary>K11: /place sprinkler 칸 [레벨].</summary>
        public static void TestPlaceLevel()
        {
            Auto.Setup();
            Game.Ok("/buy sprinkler 2");
            Auto.S.EquipmentBox[GameState.BoxKey(3)] = 1;
            Game.Ok("/place sprinkler B2 1");
            Assert.Equal(1, Game.Plot(0, "B2").Sprinkler, "level 1 as asked");
            Game.Ok("/place sprinkler B5");
            Assert.Equal(3, Game.Plot(0, "B5").Sprinkler, "highest level by default");
            var r = Game.Run("/place sprinkler B8 2");
            Assert.Equal(LogKind.Error, Game.Kind(r), "no Lv2 in the box");
            Assert.Equal("err.no_sprinkler_lv", ((Msg)r.Logs[0].Lines[0]).Key, "says which levels are there");
            Game.Ok("/place sprinkler B8 lv1");
            Assert.Equal(1, Game.Plot(0, "B8").Sprinkler, "lv1 works too");
            Game.Err("/place sprinkler B9 4");
        }

        static SprinklerPreview Preview(string line)
        {
            var c = new CommandContext();
            c.State = Game.State;
            c.Profile = Game.Profile;
            return Commands.PreviewSprinkler(c, line);
        }

        static int Count(bool[] a)
        {
            int n = 0;
            foreach (bool b in a) if (b) n++;
            return n;
        }

        /// <summary>K9: 놓거나 올릴 수 있을 때만 범위 미리 보기.</summary>
        public static void TestSprinklerPreview()
        {
            Auto.Setup();
            Game.Ok("/buy sprinkler");
            var pv = Preview("/place sprinkler B5");
            Assert.True(pv != null, "can place");
            Assert.Equal(1, pv.Level, "box level");
            Assert.Equal(5, Count(pv.Range), "self + 4 around");
            Assert.True(Preview("/place sprinkler B5 2") == null, "no Lv2 in the box");
            Assert.True(Preview("/place sprinkler") == null, "no plot yet");
            Auto.Ripe("B6", "turnip");
            Assert.True(Preview("/place sprinkler B6") == null, "plot has a crop");
            Game.Ok("/place sprinkler B5");
            pv = Preview("/upgrade sprinkler B5");
            Assert.Equal(2, pv.Level, "next level");
            Assert.Equal(9, Count(pv.Range), "self + 8 around");
            Assert.True(Preview("/upgrade sprinkler B4") == null, "no sprinkler there");
            Assert.True(Preview("/ㅔㅣㅁㅊㄷ ㄴㅔ갸ㅜㅏㅣㄷㄱ B5") == null, "Korean keys are read too (B5 is taken now)");
            Game.Ok("/buy sprinkler");
            Assert.True(Preview("/ㅔㅣㅁㅊㄷ ㄴㅔ갸ㅜㅏㅣㄷㄱ B3") != null, "Korean keys: /place sprinkler B3");
            Auto.S.Fields[0].Open = 6;
            Assert.True(Preview("/place sprinkler B3") == null, "locked plot");
        }

        /// <summary>K6~K8: 내보내기 파일 왕복과 읽기 오류.</summary>
        public static void TestExportRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(dir);
                Game.New();
                Game.State.Gold = 1234;
                Game.Profile.Settings.Lang = "ko";
                Game.Profile.Fertility = 3;
                var now = new DateTime(2026, 11, 27, 16, 10, 2);
                string path = store.ExportPath("", now);
                Assert.Equal(Path.Combine(store.ExportDir, "lupin-20261127-161002.json"), path, "default name in the export folder");
                Assert.True(!store.Export(path, Game.State, Game.Profile, now.ToUniversalTime()), "new file");
                Assert.True(store.Export(path, Game.State, Game.Profile, now.ToUniversalTime()), "second time replaces");

                ExportError err;
                var f = SaveStore.ReadExport(path, out err);
                Assert.Equal(ExportError.None, err, "reads back");
                Assert.Equal(1234.0, f.Save.Gold, "same gold");
                Assert.Equal("ko", f.Profile.Settings.Lang, "settings included");
                Assert.Equal(3L, f.Profile.Fertility, "records included");
                Assert.Equal(1, store.ListExports().Count, "listed");

                Assert.Equal(Path.Combine(store.ExportDir, "mine.json"), store.ExportPath("mine", now), "relative name → export folder, .json added");
                Assert.Equal(path, store.ImportPath("lupin-20261127-161002"), "import finds .json");
                string other = Path.Combine(dir, "usb");
                Directory.CreateDirectory(other);
                Assert.Equal(Path.Combine(other, "lupin-20261127-161002.json"), store.ExportPath(other, now), "a folder gets the default name");

                string bad = Path.Combine(dir, "bad.json");
                File.WriteAllText(bad, "{}");
                SaveStore.ReadExport(bad, out err);
                Assert.Equal(ExportError.Format, err, "not an export");
                File.WriteAllText(bad, "not json");
                SaveStore.ReadExport(bad, out err);
                Assert.Equal(ExportError.Broken, err, "broken");
                File.WriteAllText(bad, "{\"Format\":\"lupin-farm-export\",\"Version\":99}");
                SaveStore.ReadExport(bad, out err);
                Assert.Equal(ExportError.Version, err, "newer version");
                SaveStore.ReadExport(Path.Combine(dir, "none.json"), out err);
                Assert.Equal(ExportError.Missing, err, "missing");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        static object Call(object o, string method, params object[] args)
        {
            var m = o.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return m.Invoke(o, args);
        }

        static object Field(object o, string name)
        {
            return o.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(o);
        }

        /// <summary>K7: 화면에서 내보내고 → 바꾸고 → 가져오면 내보낸 상태로 돌아가고, 바꾼 상태는 before-import로 남는다.</summary>
        public static void TestImportFlow()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lupin-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(dir);
                var s = GameState.NewRun(1, 9);
                s.Gold = 111;
                store.Save(s);
                store.SaveProfile(Profile.New());

                var app = new App(store);
                Call(app, "Execute", "/save export mine");
                Assert.True(File.Exists(Path.Combine(store.ExportDir, "mine.json")), "exported");
                ((GameState)Field(app, "state")).Gold = 999;

                Call(app, "Execute", "/save import mine");
                Assert.True(Field(app, "prompt") != null, "asks first");
                Assert.Equal(999.0, ((GameState)Field(app, "state")).Gold, "nothing changes before the answer");
                Call(app, "Choose", 0);
                Assert.Equal(111.0, ((GameState)Field(app, "state")).Gold, "imported");
                bool backup = false;
                foreach (var f in store.ListExports()) if (f.Name.StartsWith("before-import-")) backup = true;
                Assert.True(backup, "current data was exported first");

                Call(app, "Execute", "/save import mine");
                Call(app, "Choose", 1);
                Assert.Equal(111.0, ((GameState)Field(app, "state")).Gold, "No keeps the data");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        /// <summary>K6, K7: /save 하위 명령과 경로 인자.</summary>
        public static void TestSaveCommand()
        {
            Game.New();
            Assert.Equal(UiAction.SaveInfo, Game.Run("/save").Action, "/save");
            var r = Game.Run("/save export");
            Assert.Equal(UiAction.SaveExport, r.Action, "export");
            Assert.Equal("", r.Path, "default place");
            r = Game.Run("/save export D:\\my backups\\x.json");
            Assert.Equal("D:\\my backups\\x.json", r.Path, "path with spaces");
            r = Game.Run("/save import lupin.json");
            Assert.Equal(UiAction.SaveImport, r.Action, "import");
            Assert.Equal("lupin.json", r.Path, "file name");
            Game.Err("/save import");
            Game.Err("/save foo");
            r = Game.Run("/ㄴㅁㅍㄷ ㄷ테ㅐㄳ D:\\백업");
            Assert.Equal(UiAction.SaveExport, r.Action, "Korean keys → /save export");
            Assert.Equal("D:\\백업", r.Path, "Korean folder name kept");
        }

        /// <summary>K3: 넓은 창이면 구역 2개, 보는 구역이 마지막이면 이전과 마지막.</summary>
        public static void TestWideLayout()
        {
            var s = GameState.NewRun(1, 3);
            Assert.True(!FarmView.IsWide(Content.WideWidth, s), "one field: never wide");
            s.Fields.Add(FieldState.Create(6));
            s.Fields.Add(FieldState.Create(6));
            Assert.True(FarmView.IsWide(Content.WideWidth, s), "wide at 204");
            Assert.True(!FarmView.IsWide(Content.WideWidth - 1, s), "not at 203");
            Assert.Equal(0, FarmView.LeftField(210, s, 0), "field 1 + 2");
            Assert.Equal(1, FarmView.LeftField(210, s, 1), "field 2 + 3");
            Assert.Equal(1, FarmView.LeftField(210, s, 2), "last: field 2 + 3");
            Assert.Equal(2, FarmView.LeftField(150, s, 2), "narrow: just the viewed field");

            var b = new ScreenBuffer();
            b.Resize(210, 30);
            var hits = new Hits();
            FarmView.Render(b, 1, s, 2, "en", hits, 60);
            Assert.Equal(1, hits.GridField, "left grid shows field 2");
            Assert.Equal(2, hits.Grid2Field, "right grid shows field 3");
            Assert.Equal(FarmView.Grid2X, hits.Grid2.X, "second grid position");
            Assert.True(FarmView.Grid2X + Content.Cols * Sprites.Size < 210 - 32, "second grid clears the info panel");
        }

        /// <summary>K12: 보통 유형(하루마다)의 진행이 정의서 3장 목표 안에 든다.</summary>
        public static void TestBalancePacing()
        {
            var sim = BalanceSim.Play(1440, 110);
            Within(sim, "tool2", 1, 3);
            Within(sim, "sprinkler", 15, 30);
            Within(sim, "auto", 20, 35);
            Within(sim, "field2", 30, 50);
            Within(sim, "nw100k", 60, 100);
        }

        static void Within(BalanceSim sim, string step, int lo, int hi)
        {
            int d;
            Assert.True(sim.Reached.TryGetValue(step, out d), step + " reached");
            Assert.True(d >= lo && d <= hi, step + " on day " + d + " (target " + lo + "~" + hi + ")");
        }
    }
}
