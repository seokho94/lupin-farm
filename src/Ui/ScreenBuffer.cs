using System.Globalization;
using System.Text;

namespace LupinFarm.Ui
{
    /// <summary>화면 색 (04 2절). -1은 터미널 기본색.</summary>
    internal static class Colors
    {
        public const int Default = -1;
        public const int Accent = 0xE08A5E;
        public const int Gray = 0x8C8C8C;
        public const int Dim = 0x4E4E4E;
        public const int Green = 0x7BC47F;
        public const int Red = 0xE57373;
        public const int Yellow = 0xE5C07B;
    }

    internal struct Cell
    {
        /// <summary>글자. 빈 문자열이면 앞 칸 2칸 문자의 이어짐.</summary>
        public string Ch;
        public int Fg;
        public int Bg;

        public bool Same(Cell o)
        {
            return Ch == o.Ch && Fg == o.Fg && Bg == o.Bg;
        }
    }

    /// <summary>셀 버퍼. 이전 프레임과 비교해 바뀐 칸만 VT로 출력한다 (D6).</summary>
    internal sealed class ScreenBuffer
    {
        Cell[] back = new Cell[0];
        Cell[] front = new Cell[0];
        bool full = true;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public void Resize(int width, int height)
        {
            if (width == Width && height == Height) return;
            Width = width;
            Height = height;
            back = new Cell[width * height];
            front = new Cell[width * height];
            full = true;
        }

        public void Invalidate()
        {
            full = true;
        }

        public void Clear()
        {
            for (int i = 0; i < back.Length; i++)
            {
                back[i].Ch = " ";
                back[i].Fg = Colors.Default;
                back[i].Bg = Colors.Default;
            }
        }

        public void Set(int x, int y, string ch, int fg, int bg)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int i = y * Width + x;
            back[i].Ch = ch;
            back[i].Fg = fg;
            back[i].Bg = bg;
        }

        /// <summary>글자를 쓰고 다음 x를 돌려준다. 폭을 넘으면 자른다.</summary>
        public int Put(int x, int y, string text, int fg, int bg = Colors.Default)
        {
            if (string.IsNullOrEmpty(text) || y < 0 || y >= Height) return x;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string piece;
                int w;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length)
                {
                    piece = text.Substring(i, 2);
                    w = 2;
                    i++;
                }
                else
                {
                    piece = c.ToString();
                    w = TextWidth.IsWide(c) ? 2 : 1;
                }
                if (x + w > Width) break;
                if (x >= 0)
                {
                    Set(x, y, piece, fg, bg);
                    if (w == 2) Set(x + 1, y, "", fg, bg);
                }
                x += w;
            }
            return x;
        }

        public void Fill(int x, int y, int count, string ch, int fg, int bg = Colors.Default)
        {
            for (int i = 0; i < count; i++) Set(x + i, y, ch, fg, bg);
        }

        public Cell Get(int x, int y)
        {
            return back[y * Width + x];
        }

        /// <summary>바뀐 칸만 담은 VT 문자열을 만들고 front를 갱신한다.</summary>
        public string Flush()
        {
            var sb = new StringBuilder();
            int curFg = int.MinValue, curBg = int.MinValue;
            for (int y = 0; y < Height; y++)
            {
                int x = 0;
                while (x < Width)
                {
                    int i = y * Width + x;
                    if (!full && back[i].Same(front[i]))
                    {
                        x++;
                        continue;
                    }
                    // 이어짐 칸이 바뀌었으면 앞의 2칸 문자부터 다시 쓴다
                    if (back[i].Ch == "" && x > 0)
                    {
                        x--;
                        i--;
                    }
                    sb.Append(Terminal.Esc).Append('[').Append(y + 1).Append(';').Append(x + 1).Append('H');
                    bool first = true;
                    while (x < Width)
                    {
                        i = y * Width + x;
                        if (!first && !full && back[i].Same(front[i]) && back[i].Ch != "") break;
                        first = false;
                        var c = back[i];
                        if (c.Ch == "")
                        {
                            front[i] = c;
                            x++;
                            continue;
                        }
                        if (c.Fg != curFg)
                        {
                            AppendColor(sb, c.Fg, true);
                            curFg = c.Fg;
                        }
                        if (c.Bg != curBg)
                        {
                            AppendColor(sb, c.Bg, false);
                            curBg = c.Bg;
                        }
                        sb.Append(c.Ch);
                        front[i] = c;
                        x++;
                    }
                }
            }
            full = false;
            if (sb.Length > 0) sb.Append(Terminal.Esc).Append("[0m");
            return sb.ToString();
        }

        static void AppendColor(StringBuilder sb, int rgb, bool fg)
        {
            if (rgb < 0)
            {
                sb.Append(Terminal.Esc).Append(fg ? "[39m" : "[49m");
                return;
            }
            sb.Append(Terminal.Esc).Append(fg ? "[38;2;" : "[48;2;")
              .Append(((rgb >> 16) & 0xFF).ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(((rgb >> 8) & 0xFF).ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append((rgb & 0xFF).ToString(CultureInfo.InvariantCulture)).Append('m');
        }

        /// <summary>개발용: 글자만 뽑는다 (--frame).</summary>
        public string PlainText()
        {
            var sb = new StringBuilder();
            for (int y = 0; y < Height; y++)
            {
                var line = new StringBuilder();
                for (int x = 0; x < Width; x++) line.Append(back[y * Width + x].Ch);
                sb.Append(line.ToString().TrimEnd()).Append('\n');
            }
            return sb.ToString();
        }
    }
}
