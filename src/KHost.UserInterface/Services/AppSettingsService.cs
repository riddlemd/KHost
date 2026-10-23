using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.BreakMusic;
using KHost.Domain.Services.FFmpeg;
using KHost.Domain.Services.VideoEncoding;
using KHost.UserInterface.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using KHost.Common.Media;
using KHost.Common.Storage;

namespace KHost.UserInterface.Services;

/// <summary>Edits the overlay at cache/settings.json; options bound via IOptionsMonitor apply live.</summary>
/// <remarks>A startup-only setting flips RestartRequired instead.</remarks>
internal sealed class AppSettingsService : IAppSettingsService
{
    internal const string OverlayFileName = "settings.json";

    private static readonly JsonSerializerOptions OverlayJson = new() { WriteIndented = true };

    private readonly IConfiguration _configuration;
    private readonly IFFmpegService _ffmpeg;
    private readonly IBreakMusicService _breakMusic;
    private readonly string _overlayPath;

    private const string NewVenueBackgroundsSection = "Venues";

    private static readonly AppSettingDescriptor BreakMusicProvider = Text(
        BreakMusicService.ServiceOptions.SectionName, "Provider", s => s.BreakMusicProvider, Blank);

    private static readonly AppSettingDescriptor NewVenueBackgrounds = Choice(
        NewVenueBackgroundsSection, "NewVenueBackgrounds", s => s.NewVenueBackgrounds, VenueBackgrounds.Basic);

    private static readonly AppSettingDescriptor NewVenuePlaceholderImageId = new AppSettingDescriptor<Guid?>(
        NewVenueBackgroundsSection, "NewVenuePlaceholderImageId", s => s.NewVenuePlaceholderImageId,
        (configuration, path) => Guid.TryParse(configuration[path], out var image) ? image : null,
        // Written even when null: clearing it must reach the overlay.
        image => image?.ToString());

    private static readonly AppSettingDescriptor NewVenuePlaceholderImageScaling = new AppSettingDescriptor<ImageScaling?>(
        NewVenueBackgroundsSection, "NewVenuePlaceholderImageScaling", s => s.NewVenuePlaceholderImageScaling,
        // Parsed rather than bound: a hand-edited word that names no scaling reads as the image's own.
        (configuration, path) => Enum.TryParse<ImageScaling>(configuration[path], ignoreCase: true, out var scaling)
            && Enum.IsDefined(scaling) ? scaling : null,
        scaling => scaling?.ToString());

    /// <summary>Every setting the page edits, in the order the overlay is written.</summary>
    /// <remarks>The order is the saved file's shape: sections appear as their first setting does.</remarks>
    private static readonly IReadOnlyList<AppSettingDescriptor> Settings =
    [
        Duration("Playback", "StopFadeDuration", s => s.StopFadeSeconds, TimeSpan.FromSeconds(5), StopFadeChoice),
        // Clamped on read as well as on save: a hand-edited value outside a fader's range would
        // otherwise reach ffmpeg as a volume multiplier nobody can undo from the console.
        Int("Playback", "DefaultBackingVolume", s => s.BackingVocalVolume, AudioMix.DefaultBackingVolume, AudioLevels.ClampVolume),
        Int("Playback", "LeadInGraceSeconds", s => s.LeadInGraceSeconds, 0, LeadInGraceChoice),
        Flag("Playback", "DynamicLeadIns", s => s.DynamicLeadIns),
        Int("Playback", "DynamicLeadInPauseSeconds", s => s.DynamicLeadInPauseSeconds, LeadInGenerator.DefaultLongPauseSeconds, DynamicLeadInPauseChoice),
        Flag("Playback", "ColorBlindFriendlyLyrics", s => s.ColorBlindFriendlyLyrics),
        // Clamped on read as well as save: a hand-edited value would otherwise reach the page outside the field's range.
        Int("Playback", "LyricsOffsetMilliseconds", s => s.LyricsOffsetMilliseconds, 0, LyricsOffset.ClampMilliseconds),

        Int("MediaStream", "SegmentSeconds", s => s.SegmentSeconds, 2, SegmentClamp),
        Int("MediaStream", "GraphicsScaleHeight", s => s.GraphicsScaleHeight, GraphicsScaling.DefaultHeight, GraphicsScaling.SnapToOffered),
        // Parsed rather than bound: a hand-edited word that names no choice reads as Auto.
        Choice("MediaStream", "Encoder", s => s.VideoEncoder, VideoEncoderPreference.Auto),

        Duration("Ads", "DefaultDuration", s => s.AdDefaultDurationSeconds,
            TimeSpan.FromSeconds(AppSettings.DefaultAdDurationSeconds), AdDurationChoice),

        PageSize("Media", s => s.MediaPageSize),
        PageSize("Users", s => s.UsersPageSize),
        PageSize("UserGroups", s => s.UserGroupsPageSize),
        PageSize("Tips", s => s.TipsPageSize),
        PageSize("Venues", s => s.VenuesPageSize),
        PageSize("PerformanceHistory", s => s.PerformanceHistoryPageSize, AppSettings.DefaultPerformanceHistoryPageSize),

        // Unlike the other choices, a number naming no style is kept: SongControlStyle has always read that way.
        Choice("Console", "SongControlStyle", s => s.SongControlStyle, SongControlStyle.Sliders, definedOnly: false),
        Flag("Console", "ShowLyricsOffsetControl", s => s.ShowLyricsOffsetControl),

        Text("Search", "DefaultMode", s => s.DefaultSearchMode, SearchModeOrDefault),

        BreakMusicProvider,
        Duration(BreakMusicService.ServiceOptions.SectionName, "FadeDuration", s => s.BreakMusicFadeSeconds,
            BreakMusicService.ServiceOptions.DefaultFadeDuration, BreakMusicFadeChoice),

        NewVenueBackgrounds,
        NewVenuePlaceholderImageId,
        NewVenuePlaceholderImageScaling,

        Flag("LocalScreen", "LaunchOnStartup", s => s.LaunchScreenOnStartup),

        // Written even when blank: clearing it must reach the overlay.
        Text(null, FFmpegService.ConfigurationKey, s => s.FFmpegPath, Blank),

        // Left out when blank, so the deployment's own folder (or the default) applies.
        Text("Plugins", "MediaDirectory", s => s.MediaDirectory, Blank, omitWhenNull: true),
    ];

    private ConfiguredRead? _read;

    public AppSettingsService(
        IConfiguration configuration, IFFmpegService ffmpeg, IBreakMusicService breakMusic, string? overlayDirectory = null)
    {
        _configuration = configuration;
        _ffmpeg = ffmpeg;
        _breakMusic = breakMusic;
        _overlayPath = Path.Combine(overlayDirectory ?? AppPaths.Cache, OverlayFileName);
    }

    public bool RestartRequired { get; private set; }

    public string DefaultMediaDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "karaoke");

    public AppSettings Current
    {
        get
        {
            // A copy: the page edits what it is handed, and the cached read must not move with it.
            var current = ReadConfigured() with { };
            current.BreakMusicProvider ??= _breakMusic.ActiveProvider?.SourceName;
            return current;
        }
    }

    /// <summary>Whether the overlay names a break music mode; <see cref="Current"/> cannot say, since it
    /// falls back to whichever provider is active.</summary>
    internal bool BreakMusicProviderSaved => ReadConfigured().BreakMusicProvider is not null;

    /// <summary>The configured values as last read, re-read only once the configuration reloads.</summary>
    /// <remarks>Break music's fallback to the active provider is left unapplied: that moves without
    /// any reload.</remarks>
    private AppSettings ReadConfigured()
    {
        var read = _read;
        if (read is not null && !read.Token.HasChanged)
            return read.Settings;

        // Taken before reading, so a reload landing mid-read leaves this snapshot already stale.
        var token = _configuration.GetReloadToken();
        var settings = new AppSettings();
        foreach (var setting in Settings)
            setting.ReadInto(_configuration, settings);

        _read = new ConfiguredRead(token, settings);
        return settings;
    }

    private static AppSettingDescriptor Int(
        string section, string key, Expression<Func<AppSettings, int>> property, int fallback, Func<int, int> normalise)
        => new AppSettingDescriptor<int>(section, key, property,
            (configuration, path) => normalise(configuration.GetValue<int?>(path) ?? fallback),
            value => normalise(value));

    private static AppSettingDescriptor PageSize(
        string key, Expression<Func<AppSettings, int>> property, int fallback = AppSettings.DefaultPageSize)
        => Int("Pagination", key, property, fallback, PaginationClamp);

    private static AppSettingDescriptor Flag(string section, string key, Expression<Func<AppSettings, bool>> property)
        => new AppSettingDescriptor<bool>(section, key, property,
            (configuration, path) => configuration.GetValue<bool?>(path) ?? false,
            value => value);

    /// <summary>Held as seconds, kept in configuration as a TimeSpan.</summary>
    private static AppSettingDescriptor Duration(
        string section, string key, Expression<Func<AppSettings, double>> property, TimeSpan fallback,
        Func<double, double> normalise)
        => new AppSettingDescriptor<double>(section, key, property,
            (configuration, path) => normalise((configuration.GetValue<TimeSpan?>(path) ?? fallback).TotalSeconds),
            seconds => TimeSpan.FromSeconds(normalise(seconds)).ToString());

    private static AppSettingDescriptor Text(
        string? section, string key, Expression<Func<AppSettings, string?>> property, Func<string?, string?> normalise,
        bool omitWhenNull = false)
        => new AppSettingDescriptor<string?>(section, key, property,
            (configuration, path) => normalise(configuration[path]),
            value => normalise(value),
            omitWhenNull);

    private static AppSettingDescriptor Choice<TEnum>(
        string section, string key, Expression<Func<AppSettings, TEnum>> property, TEnum fallback, bool definedOnly = true)
        where TEnum : struct, Enum
        => new AppSettingDescriptor<TEnum>(section, key, property,
            (configuration, path) => Enum.TryParse<TEnum>(configuration[path], ignoreCase: true, out var value)
                && (!definedOnly || Enum.IsDefined(value))
                ? value
                : fallback,
            value => value.ToString());

    // Read as well as save: a hand-edited value the select does not offer would show as none of them.
    private static double AdDurationChoice(double seconds) =>
        AppSettings.AdDurationChoices.MinBy(choice => Math.Abs(choice - seconds));

    // Read as well as save: a hand-edited value the select does not offer would show as none of them.
    private static double StopFadeChoice(double seconds) =>
        AppSettings.StopFadeChoices.MinBy(choice => Math.Abs(choice - seconds));

    // Read as well as save: a hand-edited value the select does not offer would show as none of them.
    private static double BreakMusicFadeChoice(double seconds) =>
        AppSettings.BreakMusicFadeChoices.MinBy(choice => Math.Abs(choice - seconds));

    private static int SegmentClamp(int seconds) =>
        Math.Clamp(seconds, AppSettings.MinSegmentSeconds, AppSettings.MaxSegmentSeconds);

    // Read as well as save: a hand-edited value the select does not offer would show as none of them.
    private static int LeadInGraceChoice(int seconds) =>
        AppSettings.LeadInGraceChoices.LastOrDefault(choice => choice <= seconds);

    // Read as well as save, for the same reason as the grace.
    private static int DynamicLeadInPauseChoice(int seconds) =>
        Math.Clamp(seconds, AppSettings.DynamicLeadInPauseChoices[0], AppSettings.DynamicLeadInPauseChoices[^1]);

    // Read as well as save: a hand-edited zero reaches PaginatedResult as a page that holds no rows
    // and reports no pages.
    private static int PaginationClamp(int pageSize) =>
        Math.Clamp(pageSize, AppSettings.MinPageSize, AppSettings.MaxPageSize);

    /// <summary>Whitespace and empty both read as unset, which is what a cleared field sends.
    /// </summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Read as well as save: a hand-cleared value must not reach the panel as an empty mode name.
    private static string SearchModeOrDefault(string? mode) =>
        string.IsNullOrWhiteSpace(mode) ? AppSettings.LocalSearchMode : mode.Trim();

    public async Task<AppSettingsSaveResult> SaveAsync(AppSettings settings)
    {
        var before = Current;

        var overlay = new Dictionary<string, object?>();
        foreach (var setting in Settings)
        {
            var value = setting.Write(settings);
            if (value is null && setting.OmitWhenNull) continue;

            if (setting.Section is null)
            {
                overlay[setting.Key] = value;
                continue;
            }

            if (overlay.GetValueOrDefault(setting.Section) is not Dictionary<string, object?> section)
                overlay[setting.Section] = section = [];

            section[setting.Key] = value;
        }

        await WriteOverlayAsync(JsonSerializer.Serialize(overlay, OverlayJson));

        // Read once, on the way up: turning it on now would not open a screen, and turning it
        // off would not close the one already running.
        if (settings.LaunchScreenOnStartup != before.LaunchScreenOnStartup)
            RestartRequired = true;

        if (Blank(settings.FFmpegPath) != before.FFmpegPath)
        {
            // Reloaded now rather than when the file watcher gets round to it, so the check below
            // looks in the folder just saved and the next song uses what it finds.
            if (_configuration is IConfigurationRoot root)
                root.Reload();

            await _ffmpeg.CheckAsync();
        }

        // Switched now rather than on the next start: the room should hear the change at once.
        if (Blank(settings.BreakMusicProvider) is { } provider && provider != before.BreakMusicProvider)
            await _breakMusic.SetActiveProviderAsync(provider);

        return new AppSettingsSaveResult(true);
    }

    /// <summary>Saves the break music mode alone, keeping every other key in the overlay as it is.</summary>
    internal Task SaveBreakMusicProviderAsync(string provider)
        => SaveOneAsync((BreakMusicProvider, provider.Trim()));

    public Task SaveNewVenueBackgroundsAsync(VenueBackgrounds backgrounds)
        => SaveOneAsync((NewVenueBackgrounds, backgrounds.ToString()));

    public Task SaveNewVenuePlaceholderImageAsync(Guid? mediaId, ImageScaling? scaling)
        => SaveOneAsync(
            (NewVenuePlaceholderImageId, mediaId?.ToString()),
            (NewVenuePlaceholderImageScaling, scaling?.ToString()));

    /// <summary>Writes the given keys of one section, keeping every other key in the overlay as it is.</summary>
    /// <remarks>Not through <see cref="SaveAsync"/>: that writes every setting, which would pin today's
    /// defaults into the overlay of a host who never opened the page.</remarks>
    private async Task SaveOneAsync(params (AppSettingDescriptor Setting, string? Value)[] values)
    {
        var overlay = File.Exists(_overlayPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(_overlayPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        foreach (var (setting, value) in values)
        {
            var sectionName = setting.Section!;
            if (overlay[sectionName] is not JsonObject section)
                overlay[sectionName] = section = new JsonObject();

            section[setting.Key] = value;
        }

        await WriteOverlayAsync(overlay.ToJsonString(OverlayJson));
    }

    private async Task WriteOverlayAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_overlayPath)!);
        await File.WriteAllTextAsync(_overlayPath, json);

        // Not left to reloadOnChange: its watcher never fires on some filesystems (WSL's /mnt/c,
        // network shares), and Current would read the old file until a restart.
        (_configuration as IConfigurationRoot)?.Reload();
    }

    private sealed record ConfiguredRead(IChangeToken Token, AppSettings Settings);
}
