using System.Text.Json.Nodes;
using KHost.Abstractions.Services;
using KHost.DataAccess.Services;
using KHost.UnitTests.DataAccess;
using KHost.UnitTests.DataAccess.Services;
using KHost.UserInterface.Services;
using KHost.UserInterface.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Startup;

/// <summary>Real overlay file, real configuration reload, real venue row: the step is only as good
/// as what the next read of BreakMusic:Provider sees.</summary>
public class VenueBreakMusicModeMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-bm-migration-{Guid.NewGuid():n}");
    private readonly SqliteTestDatabase _database = new();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private IConfigurationRoot? _configuration;

    public VenueBreakMusicModeMigrationTests() => Directory.CreateDirectory(_directory);

    private string OverlayPath => Path.Combine(_directory, AppSettingsService.OverlayFileName);

    public void Dispose()
    {
        (_configuration as IDisposable)?.Dispose();
        _database.Dispose();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private async Task SelectVenueAsync(string? oldProvider)
    {
        var id = await RetiredVenueSettingsReaderTests.SeedVenueAsync(_database, oldProvider);
        _venues.SelectedVenueId.Returns(id);
    }

    private VenueBreakMusicModeMigration Migration()
    {
        // Rebuilt per run, as a restart would: the file is re-read, not carried over in memory.
        (_configuration as IDisposable)?.Dispose();
        _configuration = new ConfigurationBuilder().AddJsonFile(OverlayPath, optional: true, reloadOnChange: false).Build();

        var appSettings = new AppSettingsService(
            _configuration, Substitute.For<IFFmpegService>(), Substitute.For<IBreakMusicService>(), _directory);

        return new VenueBreakMusicModeMigration(
            appSettings, _venues, new RetiredVenueSettingsReader(_database),
            NullLogger<VenueBreakMusicModeMigration>.Instance);
    }

    [Fact]
    public async Task RunAsync_OverlayNamesNoMode_WritesTheVenuesOldOneAndNothingElse()
    {
        await File.WriteAllTextAsync(OverlayPath, """{ "Playback": { "StopFadeDuration": "00:00:09" } }""");
        await SelectVenueAsync("JukeboxProvider");

        await Migration().RunAsync();

        Assert.Equal("JukeboxProvider", _configuration!["BreakMusic:Provider"]);
        var overlay = JsonNode.Parse(await File.ReadAllTextAsync(OverlayPath))!.AsObject();
        Assert.Equal(["BreakMusic", "Playback"], overlay.Select(p => p.Key).Order());
        Assert.Equal("00:00:09", (string?)overlay["Playback"]!["StopFadeDuration"]);
    }

    [Fact]
    public async Task RunAsync_NoOverlayYet_CreatesOneHoldingOnlyTheMode()
    {
        await SelectVenueAsync("JukeboxProvider");

        await Migration().RunAsync();

        Assert.Equal("JukeboxProvider", _configuration!["BreakMusic:Provider"]);
        var overlay = JsonNode.Parse(await File.ReadAllTextAsync(OverlayPath))!.AsObject();
        Assert.Equal(["BreakMusic"], overlay.Select(p => p.Key));
    }

    [Fact]
    public async Task RunAsync_OverlayAlreadyNamesAMode_LeavesItAlone()
    {
        const string saved = """{ "BreakMusic": { "Provider": "LibraryBreakMusicProvider" } }""";
        await File.WriteAllTextAsync(OverlayPath, saved);
        await SelectVenueAsync("JukeboxProvider");

        await Migration().RunAsync();

        Assert.Equal(saved, await File.ReadAllTextAsync(OverlayPath));
    }

    /// <summary>Leaving it unset lets the built-in default apply, and keeps today's default out of
    /// the overlay.</summary>
    [Fact]
    public async Task RunAsync_VenueHadNoMode_WritesNothing()
    {
        await SelectVenueAsync(null);

        await Migration().RunAsync();

        Assert.False(File.Exists(OverlayPath));
    }

    /// <summary>The venue's stored value outlives the first run (EF drops it only when the venue is
    /// next saved), so a second run must not take it again over a mode picked since.</summary>
    [Fact]
    public async Task RunAsync_Twice_KeepsTheFirstRunsMode()
    {
        await SelectVenueAsync("JukeboxProvider");
        await Migration().RunAsync();
        var afterFirst = await File.ReadAllTextAsync(OverlayPath);

        await SelectVenueAsync("SomeOtherProvider");
        await Migration().RunAsync();

        Assert.Equal(afterFirst, await File.ReadAllTextAsync(OverlayPath));
        Assert.Equal("JukeboxProvider", _configuration!["BreakMusic:Provider"]);
    }
}
