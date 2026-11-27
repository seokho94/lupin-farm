using System;
using System.Globalization;

namespace LupinFarm.Core
{
    /// <summary>큰 숫자 표기 (02 9절). 10,000 미만은 쉼표, 이상은 유효 숫자 3자리 + 단위.</summary>
    public static class NumberFormat
    {
        static readonly string[] Units = { "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "?";
            string sign = v < 0 ? "-" : "";
            double a = Math.Abs(v);
            if (a < 10000) return sign + Math.Floor(a + 0.5).ToString("#,0", Inv);

            int u = -1;
            double x = a;
            while (x >= 1000 && u < Units.Length - 1)
            {
                x /= 1000;
                u++;
            }
            if (x >= 999.5)
            {
                if (u >= Units.Length - 1) return sign + a.ToString("0.0e0", Inv);
                x /= 1000;
                u++;
            }
            return sign + Sig3(x) + Units[u];
        }

        static string Sig3(double x)
        {
            if (x >= 99.95) return Math.Round(x).ToString("0", Inv);
            if (x >= 9.995) return x.ToString("0.0", Inv);
            return x.ToString("0.00", Inv);
        }

        /// <summary>
        /// 골드. 10,000 미만은 "1,240G", 줄여 쓰면 "200K G" (단위와 붙어 kg처럼 읽히지 않게). 사이는 줄바꿈하지 않는
        /// 공백(U+00A0)이라 로그 낱말 줄바꿈(M5 K4)에서 "200K"와 "G"가 갈라지지 않는다.
        /// </summary>
        public static string Gold(double v)
        {
            string n = Num(v);
            return Math.Abs(v) < 10000 ? n + "G" : n + "\u00A0G";
        }

        public static string Int(long v)
        {
            return v.ToString("#,0", Inv);
        }
    }
}
