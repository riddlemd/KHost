using System.Reflection;

namespace KHost.UnitTests.Conventions;

// Derived rather than hand-listed: a type moving to a new src/tools project stays covered the
// moment that project becomes a ProjectReference of KHost.UnitTests, with nothing to edit here.
internal static class HostAssemblies
{
    public static readonly Assembly[] All = Assembly.GetExecutingAssembly()
        .GetReferencedAssemblies()
        .Where(n => n.Name is { } name
                 && name.StartsWith("KHost.", StringComparison.Ordinal)
                 && !name.EndsWith("Tests", StringComparison.Ordinal)
                 && name != "KHost.Analyzers")
        .Select(Assembly.Load)
        .ToArray();
}
