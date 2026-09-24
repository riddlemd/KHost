using System.Text.RegularExpressions;
using System.Reflection;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface;

/// <summary>A service wiring ScreenConnected in its constructor answers nothing until built.</summary>
/// <remarks>Services are discovered, not hardcoded, so a new screen service isn't silently missed.</remarks>
public class StartupScreenServicesTests
{
    /// <summary>Read off the source: wiring ScreenConnected is a statement reflection cannot see.</summary>
    public static TheoryData<string> ServicesThatAnswerAScreen()
    {
        var domain = Path.Combine(RepositoryRoot(), "src", "KHost.Domain");
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(domain, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            if (File.ReadAllText(file).Contains("ScreenConnected +=", StringComparison.Ordinal))
                found.Add(Path.GetFileNameWithoutExtension(file));
        }

        Assert.NotEmpty(found);

        var data = new TheoryData<string>();

        foreach (var service in found)
            data.Add(service);

        return data;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }

    /// <summary>Reachable via another constructor doesn't count; losing it drops the answer.</summary>
    [Theory]
    [MemberData(nameof(ServicesThatAnswerAScreen))]
    public void AServiceThatAnswersAScreen_StartsWithTheHost(string service)
    {
        var type = typeof(QrCodeService).Assembly
            .GetTypes()
            .SingleOrDefault(candidate => candidate.Name == service);

        Assert.True(type is not null, $"Could not find {service} in KHost.Domain.");

        Assert.True(typeof(IStartsWithTheHost).IsAssignableFrom(type),
            $"{service} wires ScreenConnected in its constructor but does not implement "
            + $"{nameof(IStartsWithTheHost)}, so nothing builds it and the first screen to connect "
            + "is answered by nobody.");
    }

    /// <summary>Must point at the existing singleton; a fresh instance would listen alone.</summary>
    [Theory]
    [MemberData(nameof(ServicesThatAnswerAScreen))]
    public void AServiceThatAnswersAScreen_IsRegisteredUnderTheMarker(string service)
    {
        var registrations = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "KHost.Domain", "ProjectExtensions.cs"));

        // Collapsed, because a registration is written across two lines and the name of what it
        // points at is on the second of them.
        var flattened = Regex.Replace(registrations, @"\s+", " ");
        var marker = nameof(IStartsWithTheHost);

        var pointedAt = Regex.Matches(flattened, $@"AddSingleton<[\w.]*{marker}>\((.*?)\);")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.True(pointedAt.Count > 0, $"Nothing at all is registered under {marker}.");

        Assert.True(
            pointedAt.Any(factory => Regex.IsMatch(factory, $@"GetRequiredService<[\w.]*I?{Regex.Escape(service)}>")),
            $"{service} is not registered under {marker}, so enumerating the marker on the way up "
            + "never builds it — whatever interfaces it says it implements.");
    }

    /// <summary>A screen can connect once the hub is mapped; a late-built service has missed it.</summary>
    [Fact]
    public void TheMarkerIsEnumerated_BeforeTheHubIsMapped()
    {
        var startup = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "KHost.UserInterface", "Program.cs"));

        var built = startup.IndexOf($"GetServices<KHost.Domain.Services.{nameof(IStartsWithTheHost)}>",
            StringComparison.Ordinal);
        var hub = startup.IndexOf("MapIPCServer", StringComparison.Ordinal);

        Assert.True(built > 0, $"Program.cs never enumerates {nameof(IStartsWithTheHost)}, so none of them are built.");
        Assert.True(hub > 0, "Program.cs no longer maps the IPC server by that name; reread this test.");
        Assert.True(built < hub, "The screen services are built after the hub is mapped, so the first screen misses them.");
    }
}
