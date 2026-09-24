using System.Reflection;
using System.Runtime.CompilerServices;

namespace KHost.UnitTests.Conventions;

// An exception after the first await of an async void on a circuit has no Task to land on and
// takes the whole circuit down. bUnit's dispatcher swallows it instead, so a component test
// cannot see the crash; the declaration is what can be checked.
public class NoAsyncVoidInUserInterfaceTests
{
    [Fact]
    public void Methods_InTheUserInterfaceAssembly_AreNeverAsyncVoid()
    {
        // Compiler-generated types are included on purpose: `Subscribe<T>(async _ => ...)` is an
        // async void lambda, compiled onto a closure class.
        var offenders = typeof(global::KHost.UserInterface.Services.ThemeService).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(void) && m.IsDefined(typeof(AsyncStateMachineAttribute), false))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .Distinct()
            .Order()
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"async void in the UI; dispatch with `_ = InvokeAsync(async () => ...)` instead:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }
}
