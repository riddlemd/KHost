using System.Reflection;
using System.Runtime.CompilerServices;

namespace KHost.UnitTests.Conventions;

// A default compiles into the call site, so an implementation whose defaults differ from its
// interface's behaves differently depending on the static type the caller happened to hold.
public class DefaultParameterConventionTests
{
    [Fact]
    public void ImplementationDefaults_AcrossAllAssemblies_MatchTheirInterfaces()
    {
        var offenders = Find(HostAssemblies.All.SelectMany(a => a.GetTypes()));

        Assert.True(
            offenders.Length == 0,
            $"Implementation defaults must match the interface's:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Find_FlagsADefaultThatDiffersFromTheInterface()
    {
        var offenders = Find([typeof(Mismatched)]);

        Assert.Equal([$"{typeof(Mismatched).FullName}.Read({nameof(IReader)}) parameter 'count': interface 1, implementation 2"], offenders);
    }

    [Fact]
    public void Find_PassesADefaultThatMatchesTheInterface()
    {
        Assert.Empty(Find([typeof(Matched)]));
    }

    internal static string[] Find(IEnumerable<Type> types) => types
        .Where(t => t.IsClass)
        .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false) && !t.ContainsGenericParameters)
        .SelectMany(t => t.GetInterfaces()
            .Where(i => !i.ContainsGenericParameters)
            .SelectMany(i => Mismatches(t, i)))
        .Distinct()
        .Order()
        .ToArray();

    private static IEnumerable<string> Mismatches(Type type, Type contract)
    {
        InterfaceMapping map;
        try
        {
            map = type.GetInterfaceMap(contract);
        }
        catch (ArgumentException)
        {
            yield break;
        }

        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var declared = map.InterfaceMethods[i].GetParameters();
            var target = map.TargetMethods[i];

            // An explicit implementation cannot be called by its own name, so its defaults are dead.
            if (target.IsPrivate) continue;

            var actual = target.GetParameters();

            for (var p = 0; p < declared.Length; p++)
            {
                var expected = Describe(declared[p]);
                var found = Describe(actual[p]);

                if (expected != found)
                    yield return $"{target.DeclaringType!.FullName}.{target.Name}({contract.Name}) parameter '{declared[p].Name}': interface {expected}, implementation {found}";
            }
        }
    }

    private static string Describe(ParameterInfo parameter)
        => !parameter.HasDefaultValue ? "none" : parameter.DefaultValue is null ? "null" : Convert.ToString(parameter.DefaultValue, System.Globalization.CultureInfo.InvariantCulture)!;

    public interface IReader
    {
        int Read(int count = 1);
    }

    private sealed class Mismatched : IReader
    {
        public int Read(int count = 2) => count;
    }

    private sealed class Matched : IReader
    {
        public int Read(int count = 1) => count;
    }
}
