using System.Collections.Generic;
using System.Text;

namespace LupinFarm.Core
{
    /// <summary>
    /// 한/영이 한글일 때 친 글자를 두벌식 자판에서 같은 자리의 영문 키로 바꾼다 (M5 K1).
    /// 음절은 초성·중성·종성으로 나누고, 겹모음·겹받침은 키 두 개, 된소리·ㅒ·ㅖ는 Shift 글자(대문자)다.
    /// </summary>
    public static class HangulKeys
    {
        const int SyllableFirst = 0xAC00;
        const int SyllableLast = 0xD7A3;
        const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        const string Medials = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        const string Finals = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

        static readonly Dictionary<char, string> keys = new Dictionary<char, string>();

        static HangulKeys()
        {
            string table = "ㄱr ㄲR ㄳrt ㄴs ㄵsw ㄶsg ㄷe ㄸE ㄹf ㄺfr ㄻfa ㄼfq ㄽft ㄾfx ㄿfv ㅀfg ㅁa ㅂq ㅃQ ㅄqt ㅅt ㅆT ㅇd ㅈw ㅉW ㅊc ㅋz ㅌx ㅍv ㅎg "
                + "ㅏk ㅐo ㅑi ㅒO ㅓj ㅔp ㅕu ㅖP ㅗh ㅘhk ㅙho ㅚhl ㅛy ㅜn ㅝnj ㅞnp ㅟnl ㅠb ㅡm ㅢml ㅣl";
            foreach (var item in table.Split(' ')) keys[item[0]] = item.Substring(1);
        }

        /// <summary>한글 자모·음절이면 true.</summary>
        public static bool IsHangul(char c)
        {
            return keys.ContainsKey(c) || (c >= SyllableFirst && c <= SyllableLast);
        }

        public static bool HasHangul(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (IsHangul(c)) return true;
            return false;
        }

        /// <summary>글자 하나를 키 글자로. 한글이 아니면 그대로.</summary>
        public static string Keys(char c)
        {
            string k;
            if (keys.TryGetValue(c, out k)) return k;
            if (c < SyllableFirst || c > SyllableLast) return c.ToString();
            int idx = c - SyllableFirst;
            var sb = new StringBuilder(6);
            sb.Append(keys[Initials[idx / 588]]);
            sb.Append(keys[Medials[(idx % 588) / 28]]);
            int fin = idx % 28;
            if (fin > 0) sb.Append(keys[Finals[fin]]);
            return sb.ToString();
        }

        /// <summary>문자열 전체를 키 글자로.</summary>
        public static string ToKeys(string s)
        {
            if (!HasHangul(s)) return s ?? "";
            var sb = new StringBuilder(s.Length * 2);
            foreach (char c in s) sb.Append(Keys(c));
            return sb.ToString();
        }

        /// <summary>
        /// 명령 줄을 고친다. `/save export|import` 뒤의 파일 경로는 그대로 둔다 (한글 폴더 이름, K1).
        /// </summary>
        public static string FixLine(string line)
        {
            if (!HasHangul(line)) return line ?? "";
            var sb = new StringBuilder(line.Length * 2);
            int token = 0;
            bool inSpace = true;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                bool space = c == ' ' || c == '\t';
                if (!space && inSpace)
                {
                    if (token == 2 && KeepsRawPath(sb.ToString()))
                    {
                        sb.Append(line.Substring(i));
                        return sb.ToString();
                    }
                    token++;
                }
                inSpace = space;
                sb.Append(Keys(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 이미 바꾼 앞부분이 `/save export ` 또는 `/save import `로 끝나 그 뒤가 경로인지 (입력창에서 한 글자씩 넣을 때도 쓴다).
        /// </summary>
        public static bool KeepsRawPath(string head)
        {
            if (head == null) return false;
            var parts = head.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].ToLowerInvariant() != "/save") return false;
            string sub = parts[1].ToLowerInvariant();
            if (sub != "export" && sub != "import") return false;
            // 하위 명령 뒤에 공백이 있어야 경로 자리다
            return parts.Length > 2 || head.EndsWith(" ") || head.EndsWith("\t");
        }
    }
}
