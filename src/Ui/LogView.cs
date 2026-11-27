using System;
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

        /// <summary>화면 한 칸(또는 2칸 문자 하나)과 그 색.</summary>
        struct Glyph
        {
            public string Text;
            public int W;
            public int Fg;
        }

        /// <summary>
        /// 문단을 폭에 맞춰 줄로 나눈다. 넘치면 낱말 사이(Indent 칸 뒤의 공백)에서 바꾸고, 한 낱말이 한 줄보다
        /// 길면 글자 단위로 자른다. 이어지는 줄은 Indent칸 들여쓴다 (M5 K4).
        /// </summary>
        internal static void Wrap(Para p, int width, List<List<Seg>> output)
        {
            if (p.NoWrap)
            {
                output.Add(new List<Seg>(p.Segs));
                return;
            }
            int indent = Math.Max(0, Math.Min(p.Indent, width - 1));
            var line = new List<Glyph>();
            int w = 0;
            foreach (var seg in p.Segs)
            {
                string t = seg.Text ?? "";
                for (int i = 0; i < t.Length; i++)
                {
                    var g = new Glyph();
                    g.Fg = seg.Fg;
                    char c = t[i];
                    if (char.IsHighSurrogate(c) && i + 1 < t.Length)
                    {
                        g.Text = t.Substring(i, 2);
                        g.W = 2;
                        i++;
                    }
                    else
                    {
                        g.Text = c.ToString();
                        g.W = TextWidth.IsWide(c) ? 2 : 1;
                    }
                    if (w + g.W <= width)
                    {
                        line.Add(g);
                        w += g.W;
                        continue;
                    }
                    // 넘친다: 넘친 글자가 공백이면 여기서 바꾸고, 아니면 마지막 낱말을 다음 줄로 옮긴다
                    var carry = new List<Glyph>();
                    if (g.Text != " ")
                    {
                        int brk = LastBreak(line, indent);
                        if (brk >= 0)
                        {
                            carry = line.GetRange(brk + 1, line.Count - brk - 1);
                            line.RemoveRange(brk, line.Count - brk);
                        }
                    }
                    output.Add(Merge(line));
                    line = new List<Glyph>();
                    var pad = new Glyph();
                    pad.Text = new string(' ', indent);
                    pad.W = indent;
                    pad.Fg = Colors.Default;
                    if (indent > 0) line.Add(pad);
                    w = indent;
                    foreach (var k in carry)
                    {
                        line.Add(k);
                        w += k.W;
                    }
                    if (g.Text == " ") continue;
                    if (w + g.W > width && line.Count > (indent > 0 ? 1 : 0))
                    {
                        // 옮긴 낱말이 한 줄보다 길다: 그 낱말을 이 줄에서 끊는다
                        output.Add(Merge(line));
                        line = new List<Glyph>();
                        if (indent > 0) line.Add(pad);
                        w = indent;
                    }
                    line.Add(g);
                    w += g.W;
                }
            }
            output.Add(Merge(line));
        }

        /// <summary>줄 안에서 바꿀 수 있는 마지막 공백 위치 (들여쓰기·머리 Indent칸은 빼고). 없으면 -1.</summary>
        static int LastBreak(List<Glyph> line, int indent)
        {
            int col = 0;
            int brk = -1;
            for (int k = 0; k < line.Count; k++)
            {
                if (line[k].Text == " " && col >= indent) brk = k;
                col += line[k].W;
            }
            return brk;
        }

        static List<Seg> Merge(List<Glyph> glyphs)
        {
            var segs = new List<Seg>();
            var sb = new System.Text.StringBuilder();
            int fg = 0;
            bool any = false;
            foreach (var g in glyphs)
            {
                if (any && g.Fg != fg)
                {
                    segs.Add(new Seg(sb.ToString(), fg));
                    sb.Length = 0;
                }
                sb.Append(g.Text);
                fg = g.Fg;
                any = true;
            }
            if (any) segs.Add(new Seg(sb.ToString(), fg));
            return segs;
        }
    }
}
