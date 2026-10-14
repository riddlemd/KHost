using KHost.Abstractions.Models;
using System.Reflection;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;

namespace KHost.UnitTests.UserInterface.Services;

/// <summary>Every setting the page edits reads from and writes to the configuration path the rest of
/// the host binds, so a misspelt key cannot silently lose one.</summary>
public class AppSettingsServiceRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-settings-{Guid.NewGuid():n}");
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();

    /// <summary>Path, a value the page offers (none of them the default), the property it lands in,
    /// and how that property reads.</summary>
    public static TheoryData<string, string, string, string> Paths => new()
    {
        { "Playback:StopFadeDuration", "00:00:02", nameof(AppSettings.StopFadeSeconds), "2" },
        { "Playback:DefaultBackingVolume", "40", nameof(AppSettings.BackingVocalVolume), "40" },
        { "Playback:LeadInGraceSeconds", "5", nameof(AppSettings.LeadInGraceSeconds), "5" },
        { "Playback:DynamicLeadIns", "True", nameof(AppSettings.DynamicLeadIns), "True" },
        { "Playback:DynamicLeadInPauseSeconds", "4", nameof(AppSettings.DynamicLeadInPauseSeconds), "4" },
        { "Playback:ColorBlindFriendlyLyrics", "True", nameof(AppSettings.ColorBlindFriendlyLyrics), "True" },
        { "Playback:LyricsOffsetMilliseconds", "-250", nameof(AppSettings.LyricsOffsetMilliseconds), "-250" },
        { "MediaStream:SegmentSeconds", "4", nameof(AppSettings.SegmentSeconds), "4" },
        { "MediaStream:GraphicsScaleHeight", "1080", nameof(AppSettings.GraphicsScaleHeight), "1080" },
        { "MediaStream:Encoder", "Software", nameof(AppSettings.VideoEncoder), "Software" },
        { "Ads:DefaultDuration", "00:00:15", nameof(AppSettings.AdDefaultDurationSeconds), "15" },
        { "Pagination:Media", "30", nameof(AppSettings.MediaPageSize), "30" },
        { "Pagination:Users", "31", nameof(AppSettings.UsersPageSize), "31" },
        { "Pagination:UserGroups", "32", nameof(AppSettings.UserGroupsPageSize), "32" },
        { "Pagination:Tips", "33", nameof(AppSettings.TipsPageSize), "33" },
        { "Pagination:Venues", "34", nameof(AppSettings.VenuesPageSize), "34" },
        { "Pagination:PerformanceHistory", "35", nameof(AppSettings.PerformanceHistoryPageSize), "35" },
        { "Console:SongControlStyle", "Dials", nameof(AppSettings.SongControlStyle), "Dials" },
        { "Console:ShowLyricsOffsetControl", "True", nameof(AppSettings.ShowLyricsOffsetControl), "True" },
        { "Search:DefaultMode", "OtherProvider", nameof(AppSettings.DefaultSearchMode), "OtherProvider" },
        { "BreakMusic:Provider", "JukeboxProvider", nameof(AppSettings.BreakMusicProvider), "JukeboxProvider" },
        { "BreakMusic:FadeDuration", "00:00:03", nameof(AppSettings.BreakMusicFadeSeconds), "3" },
        { "Venues:NewVenueBackgrounds", "Advanced", nameof(AppSettings.NewVenueBackgrounds), "Advanced" },
        { "Venues:NewVenuePlaceholderImageId", "0f8fad5b-d9cb-469f-a165-70867728950e", nameof(AppSettings.NewVenuePlaceholderImageId), "0f8fad5b-d9cb-469f-a165-70867728950e" },
        { "Venues:NewVenuePlaceholderImageScaling", "Fill", nameof(AppSettings.NewVenuePlaceholderImageScaling), "Fill" },
        { "LocalScreen:LaunchOnStartup", "True", nameof(AppSettings.LaunchScreenOnStartup), "True" },
        { "FFmpegPath", "/opt/ffmpeg", nameof(AppSettings.FFmpegPath), "/opt/ffmpeg" },
        { "Plugins:MediaDirectory", "/media/karaoke", nameof(AppSettings.MediaDirectory), "/media/karaoke" },
    };

public AppSettingsServiceRoundTripTests()
    {
        // An unstubbed substitute provider answers "" for its name, not null: no provider is active.
        _breakMusic.ActiveProvider.Returns((IBreakMusicProvider?)null);
    }

    private string OverlayPath => Path.Combine(_directory, AppSettingsService.OverlayFileName);

    private AppSettingsService Service(IEnumerable<KeyValuePair<string, string?>> config)
        => new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), _ffmpeg, _breakMusic, _directory);

    private AppSettingsService OverlayService()
        => new(new ConfigurationBuilder().AddJsonFile(OverlayPath, optional: true, reloadOnChange: false).Build(),
            _ffmpeg, _breakMusic, _directory);

    private static string? Read(AppSettings settings, string property)
        => typeof(AppSettings).GetProperty(property)!.GetValue(settings)?.ToString();

    private static IEnumerable<KeyValuePair<string, string?>> EveryPath()
        => Paths.Select(row => new KeyValuePair<string, string?>((string)row[0], (string)row[1]));

    [Theory]
    [MemberData(nameof(Paths))]
    public void EachSetting_ReadsFromItsConfigurationPath(string path, string raw, string property, string expected)
    {
        var current = Service([new(path, raw)]).Current;

        Assert.Equal(expected, Read(current, property));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task EachSetting_IsSavedToItsConfigurationPath(string path, string raw, string property, string expected)
    {
        _ = property;
        _ = expected;
        var settings = Service([new(path, raw)]).Current;

        await Service([]).SaveAsync(settings);

        var saved = new ConfigurationBuilder().AddJsonFile(OverlayPath).Build();
        Assert.Equal(raw, saved[path], ignoreCase: true);
    }

    [Fact]
    public async Task EverySetting_RoundTripsThroughTheOverlay()
    {
        var settings = Service(EveryPath()).Current;

        await Service([]).SaveAsync(settings);

        Assert.Equal(settings, OverlayService().Current);
    }

    /// <summary>Holds the rows above to the model: a property with no row would round-trip unchecked.</summary>
    [Fact]
    public void EveryEditableProperty_HasAPathRow()
    {
        var covered = Paths.Select(row => (string)row[2]).Order();
        var editable = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .Order();

        Assert.Equal(editable, covered);
    }

    /// <summary>The rows are chosen so every one moves its property off the default; otherwise the
    /// round trip could pass on a setting that was never written.</summary>
    [Fact]
    public void EveryRow_DiffersFromTheDefault()
    {
        var defaults = new AppSettings();

        foreach (var row in Paths)
            Assert.NotEqual(Read(defaults, (string)row[2]), (string)row[3]);
    }

    [Fact]
    public void EverySetting_ReadsItsDefault_WhenMissing()
        => Assert.Equal(new AppSettings(), Service([]).Current);

    /// <summary>The saved file's shape is what a host's existing overlay and the deployment's own
    /// appsettings already hold: section order, key order, nulls written, a blank folder left out.</summary>
    [Fact]
    public async Task SaveAsync_TheDefaults_WritesTheKnownShape()
    {
        await Service([]).SaveAsync(new AppSettings());

        Assert.Equal(
            """
            {
              "Playback": {
                "StopFadeDuration": "00:00:05",
                "DefaultBackingVolume": 100,
                "LeadInGraceSeconds": 0,
                "DynamicLeadIns": false,
                "DynamicLeadInPauseSeconds": 3,
                "ColorBlindFriendlyLyrics": false,
                "LyricsOffsetMilliseconds": 0
              },
              "MediaStream": {
                "SegmentSeconds": 2,
                "GraphicsScaleHeight": 0,
                "Encoder": "Auto"
              },
              "Ads": {
                "DefaultDuration": "00:00:10"
              },
              "Pagination": {
                "Media": 25,
                "Users": 25,
                "UserGroups": 25,
                "Tips": 25,
                "Venues": 25,
                "PerformanceHistory": 10
              },
              "Console": {
                "SongControlStyle": "Sliders",
                "ShowLyricsOffsetControl": false
              },
              "Search": {
                "DefaultMode": "LocalMediaProvider"
              },
              "BreakMusic": {
                "Provider": null,
                "FadeDuration": "00:00:01.5000000"
              },
              "Venues": {
                "NewVenueBackgrounds": "Basic",
                "NewVenuePlaceholderImageId": null,
                "NewVenuePlaceholderImageScaling": null
              },
              "LocalScreen": {
                "LaunchOnStartup": false
              },
              "FFmpegPath": null
            }
            """.ReplaceLineEndings("\n"),
            (await File.ReadAllTextAsync(OverlayPath)).ReplaceLineEndings("\n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
