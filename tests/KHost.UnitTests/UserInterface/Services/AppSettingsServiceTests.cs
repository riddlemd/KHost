using System.Text.Json;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.VideoEncoding;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;

namespace KHost.UnitTests.UserInterface.Services;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-settings-{Guid.NewGuid():n}");
    private readonly IUsersService _users = Substitute.For<IUsersService>();
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();

    private AppSettingsService Service(params KeyValuePair<string, string?>[] config)
        => new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), _users, _ffmpeg, _directory);

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

    /// <summary>Off unless asked: a machine with one display would put the screen over the console.</summary>
    [Fact]
    public void LaunchScreenOnStartup_DefaultsToOff()
        => Assert.False(Service().Current.LaunchScreenOnStartup);

    [Fact]
    public async Task LaunchScreenOnStartup_RoundTripsThroughTheOverlay()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { LaunchScreenOnStartup = true });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));

        Assert.True(overlay.RootElement.GetProperty("LocalScreen").GetProperty("LaunchOnStartup").GetBoolean());
    }

    [Fact]
    public void LaunchScreenOnStartup_ReadsWhatTheOverlayHolds()
        => Assert.True(Service(new KeyValuePair<string, string?>("LocalScreen:LaunchOnStartup", "true")).Current.LaunchScreenOnStartup);

    /// <summary>Read once on start: toggling it opens no screen, so the page must flag a restart.</summary>
    [Fact]
    public async Task SaveAsync_ChangingTheStartupScreen_AsksForARestart()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { LaunchScreenOnStartup = true });

        Assert.True(service.RestartRequired);
    }

    [Fact]
    public async Task SaveAsync_LeavingTheStartupScreenAlone_AsksForNoRestart()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { SegmentSeconds = 4 });

        Assert.False(service.RestartRequired);
    }

    [Fact]
    public async Task BackingVocalVolume_DefaultsToFull_AndRoundTripsThroughTheOverlay()
    {
        var service = Service();

        // A familiar karaoke product's own default: the harmonies are part of the song the singer sings over.
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
    public async Task LeadInGraceSeconds_DefaultsToOff_AndRoundTripsThroughTheOverlay()
    {
        var service = Service();

        Assert.Equal(0, service.Current.LeadInGraceSeconds);

        await service.SaveAsync(new AppSettings { LeadInGraceSeconds = 10 });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal(10, overlay.RootElement.GetProperty("Playback").GetProperty("LeadInGraceSeconds").GetInt32());
    }

    /// <summary>A hand-edited value the select does not offer would show as none of its choices.</summary>
    [Theory]
    [InlineData("5", 5)]
    [InlineData("7", 5)]
    [InlineData("60", 10)]
    [InlineData("-3", 0)]
    public void LeadInGraceSeconds_ReadsAsOneOfTheChoices(string stored, int expected)
        => Assert.Equal(expected, Service(new KeyValuePair<string, string?>("Playback:LeadInGraceSeconds", stored)).Current.LeadInGraceSeconds);

    [Fact]
    public async Task DynamicLeadIns_DefaultsToOffAtThreeSeconds_AndRoundTripsThroughTheOverlay()
    {
        var service = Service();

        Assert.False(service.Current.DynamicLeadIns);
        Assert.Equal(3, service.Current.DynamicLeadInPauseSeconds);

        await service.SaveAsync(new AppSettings { DynamicLeadIns = true, DynamicLeadInPauseSeconds = 2 });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        var playback = overlay.RootElement.GetProperty("Playback");
        Assert.True(playback.GetProperty("DynamicLeadIns").GetBoolean());
        Assert.Equal(2, playback.GetProperty("DynamicLeadInPauseSeconds").GetInt32());
    }

    [Fact]
    public async Task ColorBlindFriendlyLyrics_DefaultsToOff_AndRoundTripsThroughTheOverlay()
    {
        Assert.False(Service().Current.ColorBlindFriendlyLyrics);

        await Service().SaveAsync(new AppSettings { ColorBlindFriendlyLyrics = true });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.True(overlay.RootElement.GetProperty("Playback").GetProperty("ColorBlindFriendlyLyrics").GetBoolean());
        Assert.True(Service(new KeyValuePair<string, string?>("Playback:ColorBlindFriendlyLyrics", "true")).Current.ColorBlindFriendlyLyrics);
    }

    /// <summary>A hand-edited value the select does not offer would show as none of its choices.</summary>
    [Theory]
    [InlineData("4", 4)]
    [InlineData("0", 1)]
    [InlineData("30", 5)]
    public void DynamicLeadInPauseSeconds_ReadsAsOneOfTheChoices(string stored, int expected)
        => Assert.Equal(expected, Service(new KeyValuePair<string, string?>("Playback:DynamicLeadInPauseSeconds", stored)).Current.DynamicLeadInPauseSeconds);

    [Fact]
    public async Task GraphicsScaleHeight_DefaultsToOff_AndRoundTripsThroughTheOverlay()
    {
        var service = Service();

        Assert.Equal(GraphicsScaling.Off, service.Current.GraphicsScaleHeight);

        await service.SaveAsync(new AppSettings { GraphicsScaleHeight = 1080 });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal(1080, overlay.RootElement.GetProperty("MediaStream").GetProperty("GraphicsScaleHeight").GetInt32());
    }

    [Fact]
    public async Task GraphicsScaleHeight_SavesOnlyAnOfferedHeight()
    {
        await Service().SaveAsync(new AppSettings { GraphicsScaleHeight = 900 });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal(720, overlay.RootElement.GetProperty("MediaStream").GetProperty("GraphicsScaleHeight").GetInt32());
    }

    /// <summary>A hand-edited height the select does not offer would show as none of its choices.</summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1080", 1080)]
    [InlineData("900", 720)]
    [InlineData("99999", 2160)]
    [InlineData("-1", 0)]
    public void GraphicsScaleHeight_ReadsAsOneOfTheChoices(string stored, int expected)
        => Assert.Equal(expected, Service(new KeyValuePair<string, string?>("MediaStream:GraphicsScaleHeight", stored)).Current.GraphicsScaleHeight);

    /// <summary>Saved where the stream service's options bind from, so the choice reaches the next song.</summary>
    [Theory]
    [InlineData(VideoEncoderPreference.Hardware)]
    [InlineData(VideoEncoderPreference.Software)]
    public async Task VideoEncoder_DefaultsToAuto_AndRoundTripsToTheStreamOptions(VideoEncoderPreference chosen)
    {
        var service = Service();
        Assert.Equal(VideoEncoderPreference.Auto, service.Current.VideoEncoder);

        await service.SaveAsync(new AppSettings { VideoEncoder = chosen });

        var saved = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_directory, AppSettingsService.OverlayFileName))
            .Build();
        Assert.Equal(chosen, new AppSettingsService(saved, _users, _ffmpeg, _directory).Current.VideoEncoder);
        Assert.Equal(
            chosen,
            saved.GetSection(HlsMediaStreamService.ServiceOptions.SectionName).Get<HlsMediaStreamService.ServiceOptions>()!.Encoder);
    }

    [Theory]
    [InlineData("hardware", VideoEncoderPreference.Hardware)]
    [InlineData("Software", VideoEncoderPreference.Software)]
    [InlineData("gpu", VideoEncoderPreference.Auto)]
    [InlineData("7", VideoEncoderPreference.Auto)]
    [InlineData("", VideoEncoderPreference.Auto)]
    public void VideoEncoder_ReadsAnythingItCannotNameAsAuto(string stored, VideoEncoderPreference expected)
        => Assert.Equal(expected, Service(new KeyValuePair<string, string?>("MediaStream:Encoder", stored)).Current.VideoEncoder);

    [Fact]
    public async Task SongControlStyle_DefaultsToSliders_AndRoundTrips()
    {
        var service = Service();

        Assert.Equal(SongControlStyle.Sliders, service.Current.SongControlStyle);

        await service.SaveAsync(new AppSettings { SongControlStyle = SongControlStyle.Dials });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal("Dials", overlay.RootElement.GetProperty("Console").GetProperty("SongControlStyle").GetString());
    }

    [Theory]
    [InlineData("dials", SongControlStyle.Dials)]
    [InlineData("Sliders", SongControlStyle.Sliders)]
    [InlineData("knobs", SongControlStyle.Sliders)]
    [InlineData("", SongControlStyle.Sliders)]
    public void SongControlStyle_FallsBackToSliders_ForAnythingItCannotRead(string stored, SongControlStyle expected)
    {
        var service = Service(new KeyValuePair<string, string?>("Console:SongControlStyle", stored));

        // A hand-edited word naming no shape must not reach the console as a value with no case
        // to render it, which would leave the panel empty.
        Assert.Equal(expected, service.Current.SongControlStyle);
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

    /// <summary>The folder applies live: the next song and probe look there, so no restart.</summary>
    [Fact]
    public async Task SaveAsync_ChangingTheFfmpegPath_ChecksAgainWithoutARestart()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { FFmpegPath = "/opt/ffmpeg" });

        Assert.False(service.RestartRequired);
        await _ffmpeg.Received(1).CheckAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_LeavingTheFfmpegPathAlone_DoesNotCheckAgain()
    {
        var service = Service(new KeyValuePair<string, string?>("FFmpegPath", "/opt/ffmpeg"));

        await service.SaveAsync(service.Current with { SegmentSeconds = 4 });

        await _ffmpeg.DidNotReceive().CheckAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Cleared, it must reach the overlay as blank, or the old folder stays in force.</summary>
    [Fact]
    public async Task SaveAsync_ClearingTheFfmpegPath_WritesItBlank()
    {
        var service = Service(new KeyValuePair<string, string?>("FFmpegPath", "/opt/ffmpeg"));

        await service.SaveAsync(service.Current with { FFmpegPath = " " });

        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.Equal(JsonValueKind.Null, overlay.RootElement.GetProperty("FFmpegPath").ValueKind);
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
