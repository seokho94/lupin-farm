using System.Collections.Generic;
using System.Globalization;
using LupinFarm.Data;

namespace LupinFarm.Core
{
    /// <summary>해석한 밭 범위. Cells는 구역 안 칸 인덱스(A1 = 0, C10 = 29).</summary>
    public sealed class Target
    {
        public int Field;
        public bool Ready;
        public readonly List<int> Cells = new List<int>();

        public bool SingleCell
        {
            get { return !Ready && Cells.Count == 1; }
        }

        public bool SingleRow
        {
            get
            {
                if (Cells.Count == 0) return true;
                int row = Cells[0] / Content.Cols;
                foreach (int c in Cells) if (c / Content.Cols != row) return false;
                return true;
            }
        }
    }

    /// <summary>범위 문법 (02 6.1절): [fN.] ( A1 | A1:C3 | A | all ), harvest 전용 ready.</summary>
    public static class RangeParser
    {
        public static string CellName(int index)
        {
            return ((char)('A' + index / Content.Cols)).ToString() + (index % Content.Cols + 1).ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseCell(string s, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(s) || s.Length < 2 || s.Length > 3) return false;
            char r = char.ToUpperInvariant(s[0]);
            if (r < 'A' || r >= 'A' + Content.Rows) return false;
            int c;
            if (!int.TryParse(s.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out c)) return false;
            if (c < 1 || c > Content.Cols) return false;
            index = (r - 'A') * Content.Cols + (c - 1);
            return true;
        }

        /// <summary>"f2" → 2. 형식이 아니면 -1.</summary>
        public static int FieldNumber(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2) return -1;
            if (s[0] != 'f' && s[0] != 'F') return -1;
            int n;
            if (!int.TryParse(s.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n)) return -1;
            return n;
        }

        public static bool TryParse(string text, int viewField, int fieldCount, bool allowReady, out Target target, out Msg error)
        {
            target = null;
            error = null;
            string s = (text ?? "").Trim();
            int field = viewField;

            int dot = s.IndexOf('.');
            if (dot >= 0)
            {
                int n = FieldNumber(s.Substring(0, dot));
                if (n < 0)
                {
                    error = new Msg("err.range", text);
                    return false;
                }
                if (n < 1 || n > fieldCount)
                {
                    error = new Msg("err.no_field", n, fieldCount);
                    return false;
                }
                field = n - 1;
                s = s.Substring(dot + 1);
            }

            var t = new Target();
            t.Field = field;
            string low = s.ToLowerInvariant();

            if (allowReady && low == "ready")
            {
                t.Ready = true;
                for (int i = 0; i < Content.PlotsPerField; i++) t.Cells.Add(i);
                target = t;
                return true;
            }

            if (low == "all")
            {
                for (int i = 0; i < Content.PlotsPerField; i++) t.Cells.Add(i);
                target = t;
                return true;
            }

            if (s.Length == 1)
            {
                char r = char.ToUpperInvariant(s[0]);
                if (r >= 'A' && r < 'A' + Content.Rows)
                {
                    int row = r - 'A';
                    for (int c = 0; c < Content.Cols; c++) t.Cells.Add(row * Content.Cols + c);
                    target = t;
                    return true;
                }
                error = new Msg("err.range", text);
                return false;
            }

            int colon = s.IndexOf(':');
            if (colon >= 0)
            {
                int a, b;
                if (!TryParseCell(s.Substring(0, colon), out a) || !TryParseCell(s.Substring(colon + 1), out b))
                {
                    error = new Msg("err.range", text);
                    return false;
                }
                int r0 = System.Math.Min(a / Content.Cols, b / Content.Cols);
                int r1 = System.Math.Max(a / Content.Cols, b / Content.Cols);
                int c0 = System.Math.Min(a % Content.Cols, b % Content.Cols);
                int c1 = System.Math.Max(a % Content.Cols, b % Content.Cols);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                        t.Cells.Add(r * Content.Cols + c);
                target = t;
                return true;
            }

            int one;
            if (TryParseCell(s, out one))
            {
                t.Cells.Add(one);
                target = t;
                return true;
            }

            error = new Msg("err.range", text);
            return false;
        }

        /// <summary>범위에 필요한 농기구 레벨: 한 칸 1, 한 줄 안 2, 그 밖 3.</summary>
        public static int ToolLevelNeeded(Target t)
        {
            if (t.SingleCell) return 1;
            if (t.SingleRow) return 2;
            return 3;
        }

        /// <summary>칸 목록을 줄마다 이어진 구간으로: "A1:A4, B2, B5:B6" (/growing 표시용).</summary>
        public static string Runs(IList<int> cells)
        {
            var sorted = new List<int>(cells);
            sorted.Sort();
            var parts = new List<string>();
            int i = 0;
            while (i < sorted.Count)
            {
                int j = i;
                while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1 && sorted[j + 1] / Content.Cols == sorted[i] / Content.Cols) j++;
                parts.Add(i == j ? CellName(sorted[i]) : CellName(sorted[i]) + ":" + CellName(sorted[j]));
                i = j + 1;
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>같은 줄에 이어진 칸이면 "A1:A4", 한 칸이면 "A1", 아니면 null.</summary>
        public static string Compress(IList<int> cells)
        {
            if (cells == null || cells.Count == 0) return null;
            if (cells.Count == 1) return CellName(cells[0]);
            var sorted = new List<int>(cells);
            sorted.Sort();
            int row = sorted[0] / Content.Cols;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] / Content.Cols != row) return null;
                if (i > 0 && sorted[i] != sorted[i - 1] + 1) return null;
            }
            return CellName(sorted[0]) + ":" + CellName(sorted[sorted.Count - 1]);
        }
    }
}
