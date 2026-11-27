using System;

namespace LupinFarm.Core
{
    /// <summary>상태를 세이브에 저장할 수 있는 난수 (xorshift32). 같은 상태면 같은 결과 (D10, FR-MKT-06).</summary>
    public static class Rng
    {
        public const long DefaultSeed = 2463534242L;

        public static uint Next(ref long state)
        {
            uint x = (uint)state;
            if (x == 0) x = (uint)DefaultSeed;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        /// <summary>(0, 1) 구간. 0과 1은 나오지 않는다.</summary>
        public static double NextDouble(ref long state)
        {
            return (Next(ref state) + 1.0) / 4294967297.0;
        }

        /// <summary>표준 정규분포 (Box–Muller).</summary>
        public static double Normal(ref long state)
        {
            double u1 = NextDouble(ref state);
            double u2 = NextDouble(ref state);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
