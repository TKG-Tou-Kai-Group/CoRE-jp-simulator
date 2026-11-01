using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

// [Test] の付いたメソッドをリフレクションで全部呼ぶだけの最小ランナー。
// Unity を起動できない環境で、純 C# のルール部分だけを dotnet で検証する。
public static class Runner
{
    public static int Main()
    {
        int pass = 0, fail = 0;
        var asm = typeof(CoreSim.Tests.MatchStateTests).Assembly;
        foreach (Type t in asm.GetTypes())
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.GetCustomAttribute<TestAttribute>() == null) continue;
                object instance = Activator.CreateInstance(t);
                try
                {
                    m.Invoke(instance, null);
                    pass++;
                    Console.WriteLine($"PASS {t.Name}.{m.Name}");
                }
                catch (TargetInvocationException e)
                {
                    fail++;
                    Console.WriteLine($"FAIL {t.Name}.{m.Name}: {e.InnerException?.Message}");
                }
            }
        }
        Console.WriteLine($"{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}
