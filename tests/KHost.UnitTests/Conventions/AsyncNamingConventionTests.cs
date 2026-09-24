using System.Reflection;
using System.Runtime.CompilerServices;

namespace KHost.UnitTests.Conventions;

// .editorconfig cannot express this: naming rules have no return-type predicate, so they key
// off the `async` keyword and never see `Task Foo() => Bar()`.
public class AsyncNamingConventionTests
{
    private static readonly Assembly[] Assemblies = HostAssemblies.All;

    [Fact]
    public void TaskReturningMethods_AcrossAllAssemblies_EndInAsync()
    {
        var offenders = Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName
                     && !m.IsDefined(typeof(CompilerGeneratedAttribute), false)
                     && !m.Name.Contains('<'))
            .Where(ReturnsTask)
            .Where(m => !m.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .Distinct()
            .Order()
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Task-returning methods must end in 'Async':{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static bool ReturnsTask(MethodInfo method)
    {
        var returnType = method.ReturnType;

        if (returnType == typeof(Task) || returnType == typeof(ValueTask)) return true;
        if (!returnType.IsGenericType) return false;

        var definition = returnType.GetGenericTypeDefinition();

        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }
}
