using System.Collections.Generic;
using LupinFarm.Data;

namespace LupinFarm.Ui
{
    internal sealed class Seg
    {
        public readonly string Text;
        public readonly int Fg;

        public Seg(string text, int fg)
        {
            Text = text;
            Fg = fg;
        }
    }

    /// <summary>논리 줄(문단). 화면 폭에 맞춰 줄바꿈되고, 이어지는 줄은 Indent칸 들여쓴다.</summary>
    internal sealed class Para
    {
        public readonly List<Seg> Segs = new List<Seg>();
        public int Indent = 5;
        public bool NoWrap;

        public Para Add(string text, int fg)
        {
            Segs.Add(new Seg(text, fg));
            return this;
        }
    }

    /// <summary>로그 영역. 문구는 추가될 때의 언어로 고정된다 (K5).</summary>
    internal sealed class LogView
    {
        readonly List<List<Para>> items = new List<List<Para>>();
        List<List<Seg>> cache;
        int cacheWidth = -1;

        /// <summary>아래에서부터 몇 줄 올려 보고 있는지.</summary>
        public int Scroll;

        public int Count
        {
            get { return items.Count; }
        }

        /// <summary>지금까지 추가한 묶음 수 (지운 뒤에도 줄지 않는다). 마지막 묶음이 그대로인지 확인할 때 쓴다.</summary>
        public long Total { get; private set; }

        public void Add(List<Para> paras)
        {
            Total++;
            items.Add(paras);
            while (items.Count > Content.LogMax) items.RemoveAt(0);
            cacheWidth = -1;
            Scroll = 0;
        }

        /// <summary>마지막 묶음을 바꾼다 (같은 규칙 실행 합치기, M3 K9).</summary>
        public void ReplaceLast(List<Para> paras)
        {
            if (items.Count == 0)
            {
                Add(paras);
                return;
            }
            items[items.Count - 1] = paras;
            cacheWidth = -1;
        }

        public void Clear()
        {
            Total++;
            items.Clear();
            cacheWidth = -1;
            Scroll = 0;
        }

        public void ScrollBy(int lines)
        {
            Scroll += lines;
            if (Scroll < 0) Scroll = 0;
        }

        public List<List<Seg>> Lines(int width)
        {
            if (cacheWidth == width && cache != null) return cache;
            var lines = new List<List<Seg>>();
            foreach (var item in items)
                foreach (var p in item)
                    Wrap(p, width, lines);
            cache = lines;
            cacheWidth = width;
            return lines;
        }

        static void Wrap(Para p, int width, List<List<Seg>> output)
        {
            var line = new List<Seg>();
            int w = 0;
            if (p.NoWrap)
            {
                output.Add(new List<Seg>(p.Segs));
                return;
            }
            foreach (var seg in p.Segs)
            {
                var sb = new System.Text.StringBuilder();
                string t = seg.Text ?? "";
                for (int i = 0; i < t.Length; i++)
                {
                    char c = t[i];
                    string piece;
                    int cw;
                    if (char.IsHighSurrogate(c) && i + 1 < t.Length)
                    {
                        piece = t.Substring(i, 2);
                        cw = 2;
                        i++;
                    }
                    else
                    {
                        piece = c.ToString();
                        cw = TextWidth.IsWide(c) ? 2 : 1;
                    }
                    if (w + cw > width)
                    {
                        if (sb.Length > 0) line.Add(new Seg(sb.ToString(), seg.Fg));
                        sb.Length = 0;
                        output.Add(line);
                        line = new List<Seg>();
                        line.Add(new Seg(new string(' ', p.Indent), Colors.Default));
                        w = p.Indent;
                        if (piece == " ") continue;
                    }
                    sb.Append(piece);
                    w += cw;
                }
                if (sb.Length > 0) line.Add(new Seg(sb.ToString(), seg.Fg));
            }
            output.Add(line);
        }
    }
}
