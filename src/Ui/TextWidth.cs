namespace LupinFarm.Ui
{
    /// <summary>화면 폭 계산. 한글·전각 문자는 2칸 (NFR-08). 기호(▀ ◀ ▶ ● ⎿ ✻)는 1칸으로 본다.</summary>
    internal static class TextWidth
    {
        public static bool IsWide(char c)
        {
            if (c < 0x1100) return false;
            return (c >= 0x1100 && c <= 0x115F)
                || (c >= 0x2E80 && c <= 0x303E)
                || (c >= 0x3041 && c <= 0x33FF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0xA000 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE4F)
                || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        public static int Of(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length)
                {
                    w += 2;
                    i++;
                    continue;
                }
                w += IsWide(c) ? 2 : 1;
            }
            return w;
        }

        /// <summary>폭이 width보다 작으면 공백으로 채운다. 길면 자르지 않고 공백 하나를 붙인다 (표 열 맞춤).</summary>
        public static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            int w = Of(s);
            if (w >= width) return s + " ";
            return s + new string(' ', width - w);
        }

        /// <summary>폭 width에 맞게 자르고 모자라면 공백으로 채운다.</summary>
        public static string Fit(string s, int width)
        {
            if (s == null) s = "";
            var sb = new System.Text.StringBuilder();
            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int cw;
                string piece;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length)
                {
                    cw = 2;
                    piece = s.Substring(i, 2);
                    i++;
                }
                else
                {
                    cw = IsWide(c) ? 2 : 1;
                    piece = c.ToString();
                }
                if (w + cw > width) break;
                sb.Append(piece);
                w += cw;
            }
            while (w < width)
            {
                sb.Append(' ');
                w++;
            }
            return sb.ToString();
        }
    }
}
