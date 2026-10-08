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
using KHost.Common.Media;

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

    public AppSettingsService(
        IConfiguration configuration, IFFmpegService ffmpeg, IBreakMusicService breakMusic, string? overlayDirectory = null)
    {
        _configuration = configuration;
        _ffmpeg = ffmpeg;
        _breakMusic = breakMusic;
        _overlayPath = Path.Combine(overlayDirectory ?? Path.Combine(AppContext.BaseDirectory, "cache"), OverlayFileName);
    }

    public bool RestartRequired { get; private set; }

    public string DefaultMediaDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "karaoke");

    public AppSettings Current => new()
    {
        // RequireLogin is config-only: no App Settings checkbox writes it, so it is read here
        // from the layered configuration (appsettings.json/env/overlay).
        RequireLogin = _configuration.GetValue<bool?>("Auth:RequireLogin") ?? false,
        LaunchScreenOnStartup = _configuration.GetValue<bool?>("LocalScreen:LaunchOnStartup") ?? false,
        FFmpegPath = Blank(_configuration[FFmpegService.ConfigurationKey]),
        MediaDirectory = NormalizeMediaDirectory(_configuration["Plugins:MediaDirectory"]),
        StopFadeSeconds = StopFadeClamp(
            (_configuration.GetValue<TimeSpan?>("Playback:StopFadeDuration") ?? TimeSpan.FromSeconds(5)).TotalSeconds),
        SegmentSeconds = SegmentClamp(_configuration.GetValue<int?>("MediaStream:SegmentSeconds") ?? 2),
        GraphicsScaleHeight = GraphicsScaling.SnapToOffered(
            _configuration.GetValue<int?>("MediaStream:GraphicsScaleHeight") ?? GraphicsScaling.DefaultHeight),
        // Parsed rather than bound: a hand-edited word that names no choice reads as Auto.
        VideoEncoder = Enum.TryParse<VideoEncoderPreference>(
            _configuration["MediaStream:Encoder"], ignoreCase: true, out var encoder)
            && Enum.IsDefined(encoder)
            ? encoder
            : VideoEncoderPreference.Auto,
        AdDefaultDurationSeconds = AdDurationClamp(
            (_configuration.GetValue<TimeSpan?>("Ads:DefaultDuration")
                ?? TimeSpan.FromSeconds(AppSettings.DefaultAdDurationSeconds)).TotalSeconds),
        MediaPageSize = PageSize("Media"),
        UsersPageSize = PageSize("Users"),
        UserGroupsPageSize = PageSize("UserGroups"),
        TipsPageSize = PageSize("Tips"),
        VenuesPageSize = PageSize("Venues"),
        PerformanceHistoryPageSize = PageSize("PerformanceHistory", AppSettings.DefaultPerformanceHistoryPageSize),
        // Clamped on read as well as on save: a hand-edited value outside a fader's range would
        // otherwise reach ffmpeg as a volume multiplier nobody can undo from the console.
        BackingVocalVolume = AudioLevels.ClampVolume(
            _configuration.GetValue<int?>("Playback:DefaultBackingVolume") ?? AudioMix.DefaultBackingVolume),
        LeadInGraceSeconds = LeadInGraceChoice(_configuration.GetValue<int?>("Playback:LeadInGraceSeconds") ?? 0),
        DynamicLeadIns = _configuration.GetValue<bool?>("Playback:DynamicLeadIns") ?? false,
        DynamicLeadInPauseSeconds = DynamicLeadInPauseChoice(
            _configuration.GetValue<int?>("Playback:DynamicLeadInPauseSeconds") ?? LeadInGenerator.DefaultLongPauseSeconds),
        ColorBlindFriendlyLyrics = _configuration.GetValue<bool?>("Playback:ColorBlindFriendlyLyrics") ?? false,
        // Parsed rather than cast: a hand-edited word that names no shape falls back to sliders
        // instead of reaching the console as an enum value with no case to render it.
        SongControlStyle = Enum.TryParse<SongControlStyle>(
            _configuration["Console:SongControlStyle"], ignoreCase: true, out var style)
            ? style
            : SongControlStyle.Sliders,
        DefaultSearchMode = SearchModeOrDefault(_configuration["Search:DefaultMode"]),
        BreakMusicProvider = Blank(_configuration[BreakMusicProviderKey]) ?? _breakMusic.ActiveProvider?.SourceName,
    };

    private const string BreakMusicProviderKey = BreakMusicService.ServiceOptions.SectionName + ":Provider";

    /// <summary>Whether the overlay names a break music mode; <see cref="Current"/> cannot say, since it
    /// falls back to whichever provider is active.</summary>
    internal bool BreakMusicProviderSaved => Blank(_configuration[BreakMusicProviderKey]) is not null;

    private int PageSize(string key, int fallback = AppSettings.DefaultPageSize) =>
        PaginationClamp(_configuration.GetValue<int?>($"Pagination:{key}") ?? fallback);

    // Clamped on read as well as on save: a hand-edited zero would end every ad the instant it
    // started, and a hand-edited hour would hold the room until someone restarted the console.
    private static double AdDurationClamp(double seconds) =>
        Math.Clamp(seconds, AppSettings.MinAdDurationSeconds, AppSettings.MaxAdDurationSeconds);

    // Read as well as save, for the same reason as the ad duration.
    private static double StopFadeClamp(double seconds) =>
        Math.Clamp(seconds, AppSettings.MinStopFadeSeconds, AppSettings.MaxStopFadeSeconds);

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

    private static string? NormalizeMediaDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<AppSettingsSaveResult> SaveAsync(AppSettings settings)
    {
        var before = Current;

        var overlay = new Dictionary<string, object?>
        {
            ["Playback"] = new Dictionary<string, object?>
            {
                ["StopFadeDuration"] = TimeSpan.FromSeconds(StopFadeClamp(settings.StopFadeSeconds)).ToString(),
                ["DefaultBackingVolume"] = AudioLevels.ClampVolume(settings.BackingVocalVolume),
                ["LeadInGraceSeconds"] = LeadInGraceChoice(settings.LeadInGraceSeconds),
                ["DynamicLeadIns"] = settings.DynamicLeadIns,
                ["DynamicLeadInPauseSeconds"] = DynamicLeadInPauseChoice(settings.DynamicLeadInPauseSeconds),
                ["ColorBlindFriendlyLyrics"] = settings.ColorBlindFriendlyLyrics,
            },
            ["MediaStream"] = new Dictionary<string, object?>
            {
                ["SegmentSeconds"] = SegmentClamp(settings.SegmentSeconds),
                ["GraphicsScaleHeight"] = GraphicsScaling.SnapToOffered(settings.GraphicsScaleHeight),
                ["Encoder"] = settings.VideoEncoder.ToString(),
            },
            ["Ads"] = new Dictionary<string, object?>
            {
                ["DefaultDuration"] = TimeSpan.FromSeconds(AdDurationClamp(settings.AdDefaultDurationSeconds)).ToString(),
            },
            ["Pagination"] = new Dictionary<string, object?>
            {
                ["Media"] = PaginationClamp(settings.MediaPageSize),
                ["Users"] = PaginationClamp(settings.UsersPageSize),
                ["UserGroups"] = PaginationClamp(settings.UserGroupsPageSize),
                ["Tips"] = PaginationClamp(settings.TipsPageSize),
                ["Venues"] = PaginationClamp(settings.VenuesPageSize),
                ["PerformanceHistory"] = PaginationClamp(settings.PerformanceHistoryPageSize),
            },
        };

        overlay["Console"] = new Dictionary<string, object?>
        {
            ["SongControlStyle"] = settings.SongControlStyle.ToString(),
        };

        overlay["Search"] = new Dictionary<string, object?>
        {
            ["DefaultMode"] = SearchModeOrDefault(settings.DefaultSearchMode),
        };

        overlay[BreakMusicService.ServiceOptions.SectionName] = new Dictionary<string, object?>
        {
            ["Provider"] = Blank(settings.BreakMusicProvider),
        };

        overlay["LocalScreen"] = new Dictionary<string, object?>
        {
            ["LaunchOnStartup"] = settings.LaunchScreenOnStartup,
        };

        // Written even when blank: clearing it must reach the overlay.
        overlay[FFmpegService.ConfigurationKey] = Blank(settings.FFmpegPath);

        var mediaDirectory = NormalizeMediaDirectory(settings.MediaDirectory);
        if (mediaDirectory is not null)
            overlay["Plugins"] = new Dictionary<string, object?> { ["MediaDirectory"] = mediaDirectory };

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
    /// <remarks>Not through <see cref="SaveAsync"/>: that writes every setting, which would pin today's
    /// defaults into the overlay of a host who never opened the page.</remarks>
    internal async Task SaveBreakMusicProviderAsync(string provider)
    {
        var overlay = File.Exists(_overlayPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(_overlayPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        if (overlay[BreakMusicService.ServiceOptions.SectionName] is not JsonObject section)
            overlay[BreakMusicService.ServiceOptions.SectionName] = section = new JsonObject();

        section["Provider"] = provider.Trim();

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
}
