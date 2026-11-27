using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LupinFarm.Core;
using LupinFarm.Data;

namespace LupinFarm.Ui
{
    /// <summary>Msg(문구 키 + 값) → 화면 문구. 언어는 영어 기본, 한국어 선택.</summary>
    internal static class Localizer
    {
        /// <summary>색 조각 표시 (M5 K5): 시작 = ToneStart + 종류, 끝 = ToneEnd. 화면 폭은 0이다 (TextWidth).</summary>
        public const char ToneStart = '';
        public const char ToneEnd = '';

        public static bool IsMark(char c)
        {
            return c >= '' && c <= '';
        }

        public static string S(string lang, string key, params object[] args)
        {
            return Format(lang, new Msg(key, args), false);
        }

        /// <summary>색 표시 없는 글자 (시작 상자, 선택지, 패널).</summary>
        public static string Resolve(string lang, object o)
        {
            return Resolve(lang, o, false);
        }

        /// <summary>색 표시를 넣은 글자. Segments로 조각을 나눈다 (로그 줄).</summary>
        public static string ResolveMarked(string lang, object o)
        {
            return Resolve(lang, o, true);
        }

        static string Resolve(string lang, object o, bool marks)
        {
            if (o == null) return "";
            var m = o as Msg;
            if (m != null) return Format(lang, m, marks);
            var d = o as Dur;
            if (d != null) return Duration(lang, d.Seconds);
            var j = o as Joined;
            if (j != null)
            {
                var parts = new List<string>();
                foreach (var item in j.Items) parts.Add(Resolve(lang, item, marks));
                return string.Join(j.Sep ?? Strings.Get(lang, "list.sep"), parts.ToArray());
            }
            var pad = o as Pad;
            if (pad != null) return TextWidth.PadRight(Resolve(lang, pad.Inner, marks), pad.Width);
            var t = o as Tone;
            if (t != null)
            {
                string inner = Resolve(lang, t.Inner, marks);
                if (!marks || inner.Length == 0) return inner;
                return ((char)(ToneStart + (int)t.Kind)).ToString() + inner + ToneEnd;
            }
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        static string Format(string lang, Msg m, bool marks)
        {
            string fmt = Strings.Get(lang, m.Key);
            if (m.Args.Length == 0) return fmt;
            var args = new object[m.Args.Length];
            for (int i = 0; i < args.Length; i++) args[i] = Resolve(lang, m.Args[i], marks);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, fmt, args);
            }
            catch (FormatException)
            {
                return fmt;
            }
        }

        public static int ToneColor(ToneKind k)
        {
            switch (k)
            {
                case ToneKind.Good: return Colors.Green;
                case ToneKind.Bad: return Colors.Red;
                case ToneKind.Warn: return Colors.Accent;
                default: return Colors.Gray;
            }
        }

        /// <summary>색 표시가 든 글자를 조각으로 나눈다. 표시 밖은 baseFg.</summary>
        public static List<Seg> Segments(string marked, int baseFg)
        {
            var segs = new List<Seg>();
            if (string.IsNullOrEmpty(marked)) return segs;
            var sb = new StringBuilder();
            int fg = baseFg;
            foreach (char c in marked)
            {
                if (!IsMark(c))
                {
                    sb.Append(c);
                    continue;
                }
                if (sb.Length > 0) segs.Add(new Seg(sb.ToString(), fg));
                sb.Length = 0;
                fg = c == ToneEnd ? baseFg : ToneColor((ToneKind)(c - ToneStart));
            }
            if (sb.Length > 0) segs.Add(new Seg(sb.ToString(), fg));
            return segs;
        }

        /// <summary>실제 시간: 1d 3h / 1h 12m / 2h / 12m / 45s (한국어: 1일 3시간 …).</summary>
        public static string Duration(string lang, double seconds)
        {
            if (seconds < 0) seconds = 0;
            long s = (long)Math.Round(seconds);
            long d = s / 86400, h = (s % 86400) / 3600, m = (s % 3600) / 60, sec = s % 60;
            var sb = new StringBuilder();
            if (d > 0)
            {
                sb.Append(S(lang, "dur.d", d));
                if (h > 0) sb.Append(' ').Append(S(lang, "dur.h", h));
            }
            else if (h > 0)
            {
                sb.Append(S(lang, "dur.h", h));
                if (m > 0) sb.Append(' ').Append(S(lang, "dur.m", m));
            }
            else if (m > 0)
            {
                sb.Append(S(lang, "dur.m", m));
            }
            else
            {
                sb.Append(S(lang, "dur.s", sec));
            }
            return sb.ToString();
        }
    }
}
