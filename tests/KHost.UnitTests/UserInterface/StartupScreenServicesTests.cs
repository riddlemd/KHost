using System.Text.RegularExpressions;
using System.Reflection;
using KHost.Domain.Services.Screens;

namespace KHost.UnitTests.UserInterface;

/// <summary>
/// Every service that answers a screen connecting has to be constructed before the host serves
/// anything. They wire the event in their constructors, so one nobody has built has wired nothing,
/// and a screen that connects is simply never told — with no error anywhere to say so.
/// </summary>
/// <remarks>
/// <para>
/// Found three times before this: the marquee, the break music card, and the codes. Each was found
/// from the far end, as a screen missing something, rather than from anything that failed.
/// </para>
/// <para>
/// The services are discovered rather than listed. A list here would be the same failure as the
/// list it guards — complete only until somebody writes the next screen service, and silent about
/// it when they do.
/// </para>
/// </remarks>
public class StartupScreenServicesTests
{
    /// <summary>
    /// Every type in the domain whose constructor wires ScreenConnected. Read off the source
    /// because that wiring is a statement inside a constructor body, which reflection cannot see —
    /// unlike the marker it is checked against, which it can.
    /// </summary>
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

    /// <summary>
    /// The marker is what the host builds on the way up, so wearing it is the whole of being
    /// built. Being reachable through somebody else's constructor is not accepted: PlaybackService
    /// was alive only because ScreenMarqueeService takes it, which is the marquee needing playback
    /// for its own reasons — delete that parameter and screens stop being answered, silently.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServicesThatAnswerAScreen))]
    public void AServiceThatAnswersAScreen_StartsWithTheHost(string service)
    {
        var type = typeof(ScreenQrCodeService).Assembly
            .GetTypes()
            .SingleOrDefault(candidate => candidate.Name == service);

        Assert.True(type is not null, $"Could not find {service} in KHost.Domain.");

        Assert.True(typeof(IStartsWithTheHost).IsAssignableFrom(type),
            $"{service} wires ScreenConnected in its constructor but does not implement "
            + $"{nameof(IStartsWithTheHost)}, so nothing builds it and the first screen to connect "
            + "is answered by nobody.");
    }

    /// <summary>
    /// Wearing the marker is no use unless the container can hand it over — and registered against
    /// the singleton that is already there, not as a second copy that would listen while every
    /// other caller holds the first.
    /// </summary>
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

    /// <summary>
    /// Before the host serves anything, and before the hub in particular: a screen can connect the
    /// moment it is mapped, and a service built a line later has already missed it.
    /// </summary>
    [Fact]
    public void TheMarkerIsEnumerated_BeforeTheHubIsMapped()
    {
        var startup = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "KHost.UserInterface", "Program.cs"));

        var built = startup.IndexOf($"GetServices<KHost.Domain.Services.Screens.{nameof(IStartsWithTheHost)}>",
            StringComparison.Ordinal);
        var hub = startup.IndexOf("MapIPCServer", StringComparison.Ordinal);

        Assert.True(built > 0, $"Program.cs never enumerates {nameof(IStartsWithTheHost)}, so none of them are built.");
        Assert.True(hub > 0, "Program.cs no longer maps the IPC server by that name; reread this test.");
        Assert.True(built < hub, "The screen services are built after the hub is mapped, so the first screen misses them.");
    }
}
