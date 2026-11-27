using System;
using System.Collections.Generic;
using System.Globalization;
using LupinFarm.Core;
using LupinFarm.Data;

namespace LupinFarm.Ui
{
    internal struct Rect
    {
        public int X, Y, W, H;

        public Rect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public bool Contains(int x, int y)
        {
            return W > 0 && H > 0 && x >= X && x < X + W && y >= Y && y < Y + H;
        }
    }

    /// <summary>마우스 판정 영역. 그릴 때마다 다시 채운다 (04 3.3절).</summary>
    internal sealed class Hits
    {
        public Rect Prev, Next, Grid, Grid2, Log;

        /// <summary>Grid·Grid2에 그린 구역 번호(0부터). Grid2는 넓은 창일 때만 (M5 K3).</summary>
        public int GridField, Grid2Field;
        public readonly List<KeyValuePair<Rect, int>> Dots = new List<KeyValuePair<Rect, int>>();

        public void Reset()
        {
            Prev = Next = Grid = Grid2 = Log = new Rect();
            GridField = Grid2Field = -1;
            Dots.Clear();
        }
    }

    /// <summary>농장 영역 14줄: 위 테두리(칸 번호), 도트 12줄, 아래 테두리(구역·페이지 점) (04 3절).</summary>
    internal static class FarmView
    {
        public const int Height = 14;
        public const int GridX = 6;
        const int PlotW = Sprites.Size;
        const int PlotRows = Sprites.Size / 2;

        /// <summary>넓은 창의 두 번째 구역 그림 x (줄 이름 2칸 + 여백 2칸, M5 K3).</summary>
        public const int Grid2X = GridX + Content.Cols * PlotW + 4;

        /// <summary>구역 2개를 나란히 그릴 폭인지 (M5 K3).</summary>
        public static bool IsWide(int width, GameState s)
        {
            return width >= Content.WideWidth && s.Fields.Count >= 2;
        }

        /// <summary>왼쪽에 그릴 구역: 넓은 창이면 보는 구역과 다음 구역, 보는 구역이 마지막이면 이전과 마지막.</summary>
        public static int LeftField(int width, GameState s, int fi)
        {
            return IsWide(width, s) ? Math.Max(0, Math.Min(fi, s.Fields.Count - 2)) : fi;
        }

        public static void Render(ScreenBuffer b, int top, GameState s, int fi, string lang, Hits hits, int timescale)
        {
            Render(b, top, s, fi, lang, hits, timescale, -1, null);
        }

        /// <summary>
        /// bloomFrame ≥ 0이면 모든 칸에 Lupin Bloom 장면 프레임을 그린다 (M4 K11). preview가 있으면 그 구역에
        /// 설치·업그레이드 뒤의 범위를 노란 테두리로 그린다 (M5 K9).
        /// </summary>
        public static void Render(ScreenBuffer b, int top, GameState s, int fi, string lang, Hits hits, int timescale, int bloomFrame, SprinklerPreview preview)
        {
            int W = b.Width;
            int count = s.Fields.Count;
            int g = Colors.Gray;
            bool wide = IsWide(W, s);
            int left = LeftField(W, s, fi);

            // 위 테두리 + 칸 번호
            b.Put(0, top, "┌", g);
            b.Fill(1, top, W - 2, "─", g);
            b.Put(W - 1, top, "┐", g);

            // 옆 테두리, ◀ ▶
            for (int r = 1; r <= 12; r++)
            {
                b.Put(0, top + r, "│", g);
                b.Put(W - 1, top + r, "│", g);
            }
            int mid = top + 7;
            b.Put(2, mid, "◀", fi > 0 ? Colors.Default : Colors.Dim);
            b.Put(W - 4, mid, "▶", fi < count - 1 ? Colors.Default : Colors.Dim);
            hits.Prev = new Rect(1, top + 1, 3, 12);
            hits.Next = new Rect(W - 5, top + 1, 4, 12);

            DrawField(b, GridX, top, s, left, wide && left == fi, bloomFrame, preview);
            hits.Grid = new Rect(GridX, top + 1, Content.Cols * PlotW, Content.Rows * PlotRows);
            hits.GridField = left;
            if (wide)
            {
                DrawField(b, Grid2X, top, s, left + 1, left + 1 == fi, bloomFrame, preview);
                hits.Grid2 = new Rect(Grid2X, top + 1, Content.Cols * PlotW, Content.Rows * PlotRows);
                hits.Grid2Field = left + 1;
            }

            int panelX = W - 32;
            InfoPanel(b, panelX, top + 1, s, fi, lang, timescale);

            // 아래 테두리: 구역 표시 + 페이지 점
            int by = top + 13;
            b.Put(0, by, "└", g);
            b.Fill(1, by, W - 2, "─", g);
            b.Put(W - 1, by, "┘", g);
            if (wide)
            {
                // 그림마다 구역 이름. 보는 구역은 강조색 (M5 K3)
                b.Put(1, by, "─ " + Localizer.S(lang, "farm.page", left + 1, count) + " ", left == fi ? Colors.Accent : g);
                b.Put(Grid2X - 2, by, " " + Localizer.S(lang, "farm.page", left + 2, count) + " ", left + 1 == fi ? Colors.Accent : g);
                Dots(b, panelX, 32, by, count, fi, hits);
            }
            else
            {
                b.Put(1, by, "─ " + Localizer.S(lang, "farm.page", fi + 1, count) + " ", g);
                Dots(b, 0, W, by, count, fi, hits);
            }
        }

        /// <summary>페이지 점(구역이 12개 넘으면 "n/N")을 x부터 width칸 가운데에.</summary>
        static void Dots(ScreenBuffer b, int x0, int width, int by, int count, int fi, Hits hits)
        {
            int g = Colors.Gray;
            if (count <= 12)
            {
                int w = count * 2 + 1;
                int x = x0 + (width - w) / 2;
                b.Put(x, by, " ", g);
                for (int k = 0; k < count; k++)
                {
                    int dx = x + 1 + k * 2;
                    b.Put(dx, by, k == fi ? "●" : "○", k == fi ? Colors.Accent : g);
                    b.Put(dx + 1, by, " ", g);
                    hits.Dots.Add(new KeyValuePair<Rect, int>(new Rect(dx, by, 1, 1), k));
                }
            }
            else
            {
                string t = " " + (fi + 1).ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture) + " ";
                b.Put(x0 + (width - t.Length) / 2, by, t, Colors.Accent);
            }
        }

        /// <summary>구역 하나의 칸 번호(위 테두리), 줄 이름, 칸 그림. current면 줄 이름을 강조색으로 (넓은 창).</summary>
        static void DrawField(ScreenBuffer b, int gx, int top, GameState s, int fi, bool current, int bloomFrame, SprinklerPreview preview)
        {
            var f = s.Fields[fi];
            int g = Colors.Gray;
            for (int c = 0; c < Content.Cols; c++)
                b.Put(gx + c * PlotW + 3, top, (c + 1).ToString(CultureInfo.InvariantCulture), g);
            for (int row = 0; row < Content.Rows; row++)
                b.Put(gx - 2, top + 1 + row * PlotRows + 1, ((char)('A' + row)).ToString(), current ? Colors.Accent : g);

            // 도트 (계절 풀 색, 밤에는 어둡게)
            long now = s.GameMinute;
            int season = GameClock.Season(now);
            bool night = GameClock.IsNight(now);
            bool[] range = f.SprinklerCount > 0 && bloomFrame < 0 ? SprinklerRange(f) : null;
            bool[] pv = preview != null && preview.Field == fi && bloomFrame < 0 ? preview.Range : null;
            for (int i = 0; i < Content.PlotsPerField; i++)
            {
                int px = gx + (i % Content.Cols) * PlotW;
                int py = top + 1 + (i / Content.Cols) * PlotRows;
                DrawPlot(b, px, py, f, i, now, season, night, bloomFrame, range, pv);
            }
        }

        /// <summary>
        /// 칸 하나. range(스프링클러 + 물 받는 칸)가 있으면 그 영역의 바깥 선을 물빛으로 긋고, 스프링클러 칸은
        /// 오른쪽 아래에 레벨 숫자를 쓴다 (M4 K23, 사용자 요청).
        /// </summary>
        static void DrawPlot(ScreenBuffer b, int px, int py, FieldState f, int i, long now, int season, bool night, int bloomFrame, bool[] range, bool[] preview)
        {
            var px8 = new int[Sprites.Size, Sprites.Size];
            string[] baseArt;
            string[] over = null;
            int level = 0;
            if (bloomFrame >= 0)
            {
                baseArt = Sprites.SoilDry;
                over = Sprites.Bloom(bloomFrame);
            }
            else if (i >= f.Open)
            {
                baseArt = Sprites.Locked;
            }
            else
            {
                var p = f.Plots[i];
                baseArt = Engine.IsWet(p, now) ? Sprites.SoilWet : Sprites.SoilDry;
                int stage = Engine.Stage(p);
                if (p.Sprinkler > 0)
                {
                    over = Sprites.Sprinkler(p.Sprinkler);
                    level = p.Sprinkler;
                }
                else if (stage >= 0) over = Sprites.Crop(p.Crop, stage);
            }
            for (int y = 0; y < Sprites.Size; y++)
            {
                for (int x = 0; x < Sprites.Size; x++)
                {
                    char ch = baseArt[y][x];
                    if (over != null && over[y][x] != '.') ch = over[y][x];
                    if (range != null && RangeEdge(range, i, x, y)) ch = Sprites.RangeEdge;
                    // 미리 보기 선은 지금 범위 위에, 밤에도 그대로 (M5 K9)
                    bool pvEdge = preview != null && RangeEdge(preview, i, x, y);
                    if (pvEdge) ch = Sprites.PreviewEdge;
                    int color = Sprites.Color(ch, season);
                    px8[y, x] = night && !pvEdge ? Sprites.Darken(color, Sprites.NightFactor) : color;
                }
            }
            for (int r = 0; r < PlotRows; r++)
            {
                for (int x = 0; x < Sprites.Size; x++)
                {
                    int topC = px8[r * 2, x];
                    int botC = px8[r * 2 + 1, x];
                    if (topC == botC) b.Set(px + x, py + r, " ", Colors.Default, topC);
                    else b.Set(px + x, py + r, "▀", topC, botC);
                }
            }
            if (level > 0)
            {
                // 그림 오른쪽 아래 빈 흙 자리 (도트 5열, 4~5줄). 6열은 범위 선이 지날 수 있어 비운다.
                // 밤에도 읽히게 글자는 어둡게 하지 않는다.
                b.Set(px + LevelX, py + LevelRow, level.ToString(CultureInfo.InvariantCulture), LevelColor, px8[LevelRow * 2 + 1, LevelX]);
            }
        }

        const int LevelX = 5;
        const int LevelRow = 2;
        const int LevelColor = 0xFFFFFF;

        /// <summary>스프링클러 범위 영역: 물을 받는 칸(Farming.Covered)과 스프링클러 칸.</summary>
        static bool[] SprinklerRange(FieldState f)
        {
            var range = Farming.Covered(f);
            for (int i = 0; i < f.Open && i < f.Plots.Count; i++)
                if (f.Plots[i].Sprinkler > 0) range[i] = true;
            return range;
        }

        /// <summary>
        /// 칸 i의 도트 (x, y)가 범위 영역의 바깥 선인지. 영역은 칸의 흙(7×7)에, 이웃한 영역 칸 사이의 풀 줄까지 더한
        /// 도트 모양이다. 그 모양 안에서 상하좌우 중 하나라도 밖이면 선이다. 그래서 선은 흙 가장자리를 따라가고
        /// 이웃한 영역 칸 사이에서는 끊기지 않는다.
        /// </summary>
        static bool RangeEdge(bool[] range, int i, int x, int y)
        {
            int gx = (i % Content.Cols) * Sprites.Size + x;
            int gy = (i / Content.Cols) * Sprites.Size + y;
            if (!InRange(range, gx, gy)) return false;
            return !InRange(range, gx - 1, gy) || !InRange(range, gx + 1, gy)
                || !InRange(range, gx, gy - 1) || !InRange(range, gx, gy + 1);
        }

        static bool InRange(bool[] range, int gx, int gy)
        {
            if (gx < 0 || gy < 0 || gx >= Content.Cols * Sprites.Size || gy >= Content.Rows * Sprites.Size) return false;
            int c = gx / Sprites.Size, r = gy / Sprites.Size;
            bool right = gx % Sprites.Size == Sprites.Size - 1;
            bool down = gy % Sprites.Size == Sprites.Size - 1;
            if (!RangeAt(range, r, c)) return false;
            if (right && !RangeAt(range, r, c + 1)) return false;
            if (down && !RangeAt(range, r + 1, c)) return false;
            if (right && down && !RangeAt(range, r + 1, c + 1)) return false;
            return true;
        }

        static bool RangeAt(bool[] range, int r, int c)
        {
            return r >= 0 && r < Content.Rows && c >= 0 && c < Content.Cols && range[r * Content.Cols + c];
        }

        static void InfoPanel(ScreenBuffer b, int x, int y, GameState s, int fi, string lang, int timescale)
        {
            var st = Engine.Stats(s, fi);
            int g = Colors.Gray;
            b.Put(x, y, Localizer.S(lang, "field.label", fi + 1), Colors.Accent);
            b.Put(x + 15, y, Localizer.S(lang, "panel.plots", st.Open, Content.PlotsPerField), Colors.Default);

            Pair(b, x, y + 2, Localizer.S(lang, "panel.dry"), st.Dry, st.Dry > 0 ? Colors.Accent : Colors.Default);
            Pair(b, x + 15, y + 2, Localizer.S(lang, "panel.ready"), st.Ripe, st.Ripe > 0 ? Colors.Green : Colors.Default);
            Pair(b, x, y + 3, Localizer.S(lang, "panel.growing"), st.Growing, Colors.Default);
            Pair(b, x + 15, y + 3, Localizer.S(lang, "panel.empty"), st.Empty, Colors.Default);

            b.Put(x, y + 5, Localizer.S(lang, "panel.storage"), g);
            b.Put(x + 9, y + 5, s.StorageUsed.ToString(CultureInfo.InvariantCulture) + "/" + s.StorageCapacity.ToString(CultureInfo.InvariantCulture),
                s.StorageUsed >= s.StorageCapacity ? Colors.Accent : Colors.Default);
            b.Put(x, y + 6, Localizer.S(lang, "panel.seeds"), g);
            b.Put(x + 9, y + 6, s.SeedTotal.ToString(CultureInfo.InvariantCulture), Colors.Default);
            // 아직 놓지 않은 스프링클러 (M5 K10). 창고·씨앗 값이 길어도 겹치지 않게 한 줄을 쓴다
            int box = s.BoxSprinklers;
            b.Put(x, y + 7, Localizer.S(lang, "panel.box"), g);
            b.Put(x + 9, y + 7, box > 0 ? TextWidth.Fit(Localizer.S(lang, "panel.box_n", box), 19) : "-", box > 0 ? Colors.Default : Colors.Dim);

            b.Put(x, y + 8, Localizer.S(lang, "panel.next"), g);
            string next;
            int color = Colors.Default;
            if (st.NextMinutes >= 0)
            {
                next = Localizer.S(lang, "crop." + st.NextCrop) + " " + Localizer.Duration(lang, GameClock.RealSecondsFromGame(st.NextMinutes, timescale));
            }
            else if (st.Dry > 0)
            {
                next = Localizer.S(lang, "panel.needs_water");
                color = Colors.Accent;
            }
            else
            {
                next = "-";
            }
            b.Put(x + 9, y + 8, TextWidth.Fit(next, 19), color);

            // 장비, 규칙 (M3 K17)
            var f = s.Fields[fi];
            string on = Localizer.S(lang, "panel.on");
            int sp = f.SprinklerCount;
            Gear(b, x, y + 9, Localizer.S(lang, "panel.sprinkler"), sp > 0 ? sp.ToString(CultureInfo.InvariantCulture) : "-", sp > 0);
            Gear(b, x + 15, y + 9, Localizer.S(lang, "panel.harvester"), f.Harvester ? on : "-", f.Harvester);
            Gear(b, x, y + 10, Localizer.S(lang, "panel.planter"), f.Planter ? on : "-", f.Planter);
            Gear(b, x + 15, y + 10, Localizer.S(lang, "panel.greenhouse"), f.Greenhouse ? on : "-", f.Greenhouse);

            b.Put(x, y + 11, Localizer.S(lang, "panel.auto"), g);
            var related = Rules.Related(s, fi);
            string rules = "-";
            if (related.Count > 0)
            {
                var parts = new List<string>();
                foreach (int n in related) parts.Add("#" + n.ToString(CultureInfo.InvariantCulture));
                rules = string.Join(" ", parts.ToArray());
            }
            b.Put(x + 9, y + 11, TextWidth.Fit(rules, 19), related.Count > 0 ? Colors.Accent : Colors.Default);
        }

        /// <summary>장비 한 칸: 회색 이름 + 값. 이름 길이에 맞춰 값이 붙는다 (한글 이름도 열 안에 들어간다).</summary>
        static void Gear(ScreenBuffer b, int x, int y, string label, string value, bool has)
        {
            b.Put(x, y, label, Colors.Gray);
            b.Put(x + TextWidth.Of(label) + 1, y, value, has ? Colors.Default : Colors.Dim);
        }

        static void Pair(ScreenBuffer b, int x, int y, string label, int value, int color)
        {
            b.Put(x, y, label, Colors.Gray);
            b.Put(x + 9, y, value.ToString(CultureInfo.InvariantCulture), color);
        }
    }
}
