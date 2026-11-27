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
        public static string S(string lang, string key, params object[] args)
        {
            return Format(lang, new Msg(key, args));
        }

        public static string Resolve(string lang, object o)
        {
            if (o == null) return "";
            var m = o as Msg;
            if (m != null) return Format(lang, m);
            var d = o as Dur;
            if (d != null) return Duration(lang, d.Seconds);
            var j = o as Joined;
            if (j != null)
            {
                var parts = new List<string>();
                foreach (var item in j.Items) parts.Add(Resolve(lang, item));
                return string.Join(j.Sep ?? Strings.Get(lang, "list.sep"), parts.ToArray());
            }
            var pad = o as Pad;
            if (pad != null) return TextWidth.PadRight(Resolve(lang, pad.Inner), pad.Width);
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        static string Format(string lang, Msg m)
        {
            string fmt = Strings.Get(lang, m.Key);
            if (m.Args.Length == 0) return fmt;
            var args = new object[m.Args.Length];
            for (int i = 0; i < args.Length; i++) args[i] = Resolve(lang, m.Args[i]);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, fmt, args);
            }
            catch (FormatException)
            {
                return fmt;
            }
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
