using System.Text.Json;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;

namespace KHost.UnitTests.UserInterface.Services;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-settings-{Guid.NewGuid():n}");
    private readonly IUsersService _users = Substitute.For<IUsersService>();

    private AppSettingsService Service(params KeyValuePair<string, string?>[] config)
        => new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), _users, _directory);

    [Fact]
    public async Task SaveAsync_WritesAConfigShapedOverlay()
    {
        var service = Service();

        var result = await service.SaveAsync(new AppSettings { RequireLogin = false, SegmentSeconds = 4 });

        Assert.True(result.Saved);
        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.False(overlay.RootElement.GetProperty("Auth").GetProperty("RequireLogin").GetBoolean());
        Assert.Equal(4, overlay.RootElement.GetProperty("MediaStream").GetProperty("SegmentSeconds").GetInt32());
        Assert.Equal("00:00:05", overlay.RootElement.GetProperty("Playback").GetProperty("StopFadeDuration").GetString());
    }

    [Fact]
    public async Task BackingVocalVolume_DefaultsToFull_AndRoundTripsThroughTheOverlay()
    {
        var service = Service();

        Assert.Equal(100, service.Current.BackingVocalVolume);

        await service.SaveAsync(new AppSettings { BackingVocalVolume = 60 });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal(60, overlay.RootElement.GetProperty("Playback").GetProperty("DefaultBackingVolume").GetInt32());
    }

    [Theory]
    [InlineData("240", 100)]
    [InlineData("-30", 0)]
    public async Task BackingVocalVolume_IsClampedOnReadAsWellAsOnSave(string stored, int expected)
    {
        var service = Service(new KeyValuePair<string, string?>("Playback:DefaultBackingVolume", stored));

        // A hand-edited overlay reaches ffmpeg as a volume multiplier, and nothing on the console
        // would undo a song mixed at 240%.
        Assert.Equal(expected, service.Current.BackingVocalVolume);

        await service.SaveAsync(new AppSettings { BackingVocalVolume = int.Parse(stored) });
        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal(expected, overlay.RootElement.GetProperty("Playback").GetProperty("DefaultBackingVolume").GetInt32());
    }

    [Fact]
    public async Task SaveAsync_RefusesRequiringLogin_WhileNoAdminHasAPassword()
    {
        _users.HasAdminWithPasswordAsync().Returns(false);
        var service = Service(new KeyValuePair<string, string?>("Auth:RequireLogin", "false"));

        var result = await service.SaveAsync(new AppSettings { RequireLogin = true });

        Assert.False(result.Saved);
        Assert.Contains("lock everyone out", result.Error);
        Assert.False(File.Exists(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
    }

    [Fact]
    public async Task SaveAsync_AllowsRequiringLogin_OnceAnAdminHasAPassword()
    {
        _users.HasAdminWithPasswordAsync().Returns(true);
        var service = Service(new KeyValuePair<string, string?>("Auth:RequireLogin", "false"));

        var result = await service.SaveAsync(new AppSettings { RequireLogin = true });

        Assert.True(result.Saved);
    }

    [Fact]
    public async Task SaveAsync_FlagsARestart_OnlyForTheFfmpegPath()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { RequireLogin = false });
        Assert.False(service.RestartRequired);

        await service.SaveAsync(new AppSettings { FFmpegPath = "/opt/ffmpeg" });
        Assert.True(service.RestartRequired);
    }

    [Fact]
    public async Task SaveAsync_LeavesRestartAlone_ForLiveSettings()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { StopFadeSeconds = 3, SegmentSeconds = 4 });

        Assert.False(service.RestartRequired);
    }

    [Fact]
    public void Current_FallsBackToTheDefaultPageSizes_WhenTheOverlayHasNone()
    {
        var current = Service().Current;

        Assert.Equal(AppSettings.DefaultPageSize, current.MediaPageSize);
        Assert.Equal(AppSettings.DefaultPageSize, current.UsersPageSize);
        Assert.Equal(AppSettings.DefaultPageSize, current.UserGroupsPageSize);
        Assert.Equal(AppSettings.DefaultPageSize, current.TipsPageSize);
        Assert.Equal(AppSettings.DefaultPageSize, current.VenuesPageSize);
        Assert.Equal(AppSettings.DefaultPerformanceHistoryPageSize, current.PerformanceHistoryPageSize);
    }

    [Fact]
    public void Current_ReadsEachPageSizeFromItsOwnKey()
    {
        var service = Service(
            new KeyValuePair<string, string?>("Pagination:Media", "11"),
            new KeyValuePair<string, string?>("Pagination:Users", "12"),
            new KeyValuePair<string, string?>("Pagination:UserGroups", "13"),
            new KeyValuePair<string, string?>("Pagination:Tips", "14"),
            new KeyValuePair<string, string?>("Pagination:Venues", "15"),
            new KeyValuePair<string, string?>("Pagination:PerformanceHistory", "16"));

        var current = service.Current;

        Assert.Equal(11, current.MediaPageSize);
        Assert.Equal(12, current.UsersPageSize);
        Assert.Equal(13, current.UserGroupsPageSize);
        Assert.Equal(14, current.TipsPageSize);
        Assert.Equal(15, current.VenuesPageSize);
        Assert.Equal(16, current.PerformanceHistoryPageSize);
    }

    [Theory]
    [InlineData("0", AppSettings.MinPageSize)]
    [InlineData("-5", AppSettings.MinPageSize)]
    [InlineData("100000", AppSettings.MaxPageSize)]
    public void Current_ClampsAHandEditedPageSize(string configured, int expected)
    {
        var service = Service(new KeyValuePair<string, string?>("Pagination:Media", configured));

        Assert.Equal(expected, service.Current.MediaPageSize);
    }

    [Fact]
    public async Task SaveAsync_WritesThePageSizes_Clamped()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { MediaPageSize = 50, UsersPageSize = 0 });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        var pagination = overlay.RootElement.GetProperty("Pagination");
        Assert.Equal(50, pagination.GetProperty("Media").GetInt32());
        Assert.Equal(AppSettings.MinPageSize, pagination.GetProperty("Users").GetInt32());
    }

    [Fact]
    public void Current_FallsBackToNull_WhenTheOverlayHasNoMediaDirectory()
    {
        Assert.Null(Service().Current.MediaDirectory);
    }

    [Fact]
    public void DefaultMediaDirectory_IsUserProfileKaraoke()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "karaoke");

        Assert.Equal(expected, Service().DefaultMediaDirectory);
    }

    [Fact]
    public void Current_TrimsAHandEditedMediaDirectory()
    {
        var service = Service(new KeyValuePair<string, string?>("Plugins:MediaDirectory", "  /data/karaoke  "));

        Assert.Equal("/data/karaoke", service.Current.MediaDirectory);
    }

    [Fact]
    public async Task SaveAsync_WritesTheMediaDirectoryTrimmed()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { MediaDirectory = "  /data/karaoke  " });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal("/data/karaoke", overlay.RootElement.GetProperty("Plugins").GetProperty("MediaDirectory").GetString());
    }

    [Fact]
    public async Task SaveAsync_OmitsTheMediaDirectory_WhenLeftBlank()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { MediaDirectory = "   " });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.False(overlay.RootElement.TryGetProperty("Plugins", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
