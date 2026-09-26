using System.Runtime.InteropServices;
using KHost.UserInterface;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.UserInterface;

/// <summary>Covers the source both executables compile; LocalScreen's copy differs only by namespace.</summary>
public sealed class AppIconTests : IDisposable
{
    private readonly string _baseDirectory = Directory.CreateTempSubdirectory("khost-icon-").FullName;
    private readonly RecordingLogger _logger = new();

    public void Dispose() => Directory.Delete(_baseDirectory, recursive: true);

    [Fact]
    public void ResolveWindowIcon_OnWindows_ReturnsTheIco()
    {
        var ico = Touch(AppIcon.WindowsIconFileName);
        Touch(AppIcon.LinuxIconFileName);

        Assert.Equal(ico, AppIcon.ResolveWindowIcon(_baseDirectory, OSPlatform.Windows));
    }

    [Fact]
    public void ResolveWindowIcon_OnLinux_ReturnsThePng()
    {
        Touch(AppIcon.WindowsIconFileName);
        var png = Touch(AppIcon.LinuxIconFileName);

        Assert.Equal(png, AppIcon.ResolveWindowIcon(_baseDirectory, OSPlatform.Linux));
    }

    [Fact]
    public void ResolveWindowIcon_OnMacOS_ReturnsNull()
    {
        Touch(AppIcon.WindowsIconFileName);
        Touch(AppIcon.LinuxIconFileName);
        Touch(AppIcon.MacIconFileName);

        Assert.Null(AppIcon.ResolveWindowIcon(_baseDirectory, OSPlatform.OSX));
    }

    [Theory]
    [MemberData(nameof(WindowPlatforms))]
    public void ResolveWindowIcon_WhenTheFileIsMissing_ReturnsNull(string platform)
    {
        // Photino throws on a path that does not exist, which would stop the window opening.
        Assert.Null(AppIcon.ResolveWindowIcon(_baseDirectory, OSPlatform.Create(platform)));
    }

    [Theory]
    [MemberData(nameof(WindowPlatforms))]
    public void ResolveWindowIcon_FindsTheCopyBesideTheBuild(string platform)
    {
        Assert.NotNull(AppIcon.ResolveWindowIcon(AppContext.BaseDirectory, OSPlatform.Create(platform)));
    }

    [Fact]
    public void MacDockIconCopy_BesideTheBuild()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, AppIcon.DirectoryName, AppIcon.MacIconFileName)));
    }

    [Fact]
    public void MacDockIcon_OffMacOS_DoesNothing()
    {
        var icns = Touch(AppIcon.MacIconFileName);
        var calls = 0;

        var set = MacDockIcon.TrySet(icns, isMacOS: false, _logger, _ => { calls++; return true; });

        Assert.False(set);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void MacDockIcon_WhenTheFileIsMissing_NeverReachesAppKit()
    {
        var calls = 0;

        var set = MacDockIcon.TrySet(Path.Combine(_baseDirectory, "missing.icns"), isMacOS: true, _logger, _ => { calls++; return true; });

        Assert.False(set);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void MacDockIcon_OnMacOS_AppliesTheIcon()
    {
        var icns = Touch(AppIcon.MacIconFileName);
        string? applied = null;

        var set = MacDockIcon.TrySet(icns, isMacOS: true, _logger, path => { applied = path; return true; });

        Assert.True(set);
        Assert.Equal(icns, applied);
    }

    [Fact]
    public void MacDockIcon_WhenAppKitThrows_ReportsFalseAndLogs()
    {
        var icns = Touch(AppIcon.MacIconFileName);

        var set = MacDockIcon.TrySet(icns, isMacOS: true, _logger, _ => throw new DllNotFoundException("libobjc"));

        Assert.False(set);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<DllNotFoundException>(entry.Exception);
    }

    public static TheoryData<string> WindowPlatforms => new() { "WINDOWS", "LINUX" };

    private string Touch(string name)
    {
        var directory = Path.Combine(_baseDirectory, AppIcon.DirectoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
