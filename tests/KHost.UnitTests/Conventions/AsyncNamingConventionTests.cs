using System.Reflection;
using System.Runtime.CompilerServices;

namespace KHost.UnitTests.Conventions;

// .editorconfig cannot express this — naming rules have no return-type predicate, so they key
// off the `async` keyword and never see `Task Foo() => Bar()`.
public class AsyncNamingConventionTests
{
    // Bare KHost.Domain.X would bind to this project's mirrored namespace, not the real one.
    private static readonly Assembly[] Assemblies =
    [
        typeof(global::KHost.Abstractions.MediaPlayer.IMediaPlayer).Assembly,
        typeof(global::KHost.DataAccess.ProjectExtensions).Assembly,
        typeof(global::KHost.Domain.ProjectExtensions).Assembly,
        typeof(global::KHost.IPC.SignalR.ProjectExtensions).Assembly,
        typeof(global::KHost.LrcLib.ILrcLibClient).Assembly,
        typeof(global::KHost.Plugins.Sdk.PluginApi).Assembly,
        typeof(global::KHost.Telemetry.KHostActivitySource).Assembly,
        typeof(global::KHost.UserInterface.Services.ThemeService).Assembly,
        typeof(global::KHost.CatalogSync.GitHubClient).Assembly,
    ];

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
