using System;
using System.Reflection;

namespace LupinFarm.Tests
{
    public sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message)
        {
        }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new AssertException(message + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }

    /// <summary>public static void Test*() 메서드를 모두 실행한다. 실패가 있으면 종료 코드 1.</summary>
    public static class TestMain
    {
        public static int Main(string[] args)
        {
            // bin\tests.exe balance [일수]: 밸런스 시뮬레이션 보고 (M5 K12)
            if (args.Length > 0 && args[0] == "balance")
            {
                int days = args.Length > 1 ? int.Parse(args[1]) : 250;
                Console.Write(BalanceSim.Report(days));
                if (args.Length > 2) Console.Write(BalanceSim.Trace(int.Parse(args[2]), days));
                return 0;
            }
            int pass = 0, fail = 0;
            foreach (var type in typeof(TestMain).Assembly.GetTypes())
            {
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!m.Name.StartsWith("Test", StringComparison.Ordinal) || m.GetParameters().Length != 0) continue;
                    string name = type.Name + "." + m.Name;
                    try
                    {
                        m.Invoke(null, null);
                        pass++;
                    }
                    catch (TargetInvocationException e)
                    {
                        fail++;
                        var inner = e.InnerException ?? e;
                        Console.WriteLine("[FAIL] " + name + ": " + inner.Message);
                        if (!(inner is AssertException)) Console.WriteLine(inner.StackTrace);
                    }
                }
            }
            Console.WriteLine((fail == 0 ? "[OK] " : "[FAILED] ") + pass + " passed, " + fail + " failed");
            return fail == 0 ? 0 : 1;
        }
    }
}
