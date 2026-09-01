using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.Extensions.Configuration;
using KHost.Common.Media;

namespace KHost.UserInterface.Services;

/// <summary>
/// Edits the configuration overlay at cache/settings.json. The overlay is registered as the
/// last configuration source with reload-on-change, so options bound through IOptionsMonitor
/// apply as soon as the file lands; startup-only settings flip RestartRequired instead.
/// </summary>
internal sealed class AppSettingsService : IAppSettingsService
{
    internal const string OverlayFileName = "settings.json";

    private readonly IConfiguration _configuration;
    private readonly IUsersService _usersService;
    private readonly string _overlayPath;

    public AppSettingsService(IConfiguration configuration, IUsersService usersService, string? overlayDirectory = null)
    {
        _configuration = configuration;
        _usersService = usersService;
        _overlayPath = Path.Combine(overlayDirectory ?? Path.Combine(AppContext.BaseDirectory, "cache"), OverlayFileName);
    }

    public bool RestartRequired { get; private set; }

    public string DefaultMediaDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "karaoke");

    public AppSettings Current => new()
    {
        RequireLogin = _configuration.GetValue<bool?>("Auth:RequireLogin") ?? true,
        LaunchScreenOnStartup = _configuration.GetValue<bool?>("LocalScreen:LaunchOnStartup") ?? false,
        FFmpegPath = _configuration["FFmpegPath"],
        MediaDirectory = NormalizeMediaDirectory(_configuration["Plugins:MediaDirectory"]),
        StopFadeSeconds = (_configuration.GetValue<TimeSpan?>("Playback:StopFadeDuration") ?? TimeSpan.FromSeconds(5)).TotalSeconds,
        SyncStartLeadMilliseconds = (_configuration.GetValue<TimeSpan?>("Playback:SyncStartLead") ?? TimeSpan.FromMilliseconds(400)).TotalMilliseconds,
        SegmentSeconds = _configuration.GetValue<int?>("MediaStream:SegmentSeconds") ?? 2,
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
        // Parsed rather than cast: a hand-edited word that names no shape falls back to sliders
        // instead of reaching the console as an enum value with no case to render it.
        SongControlStyle = Enum.TryParse<SongControlStyle>(
            _configuration["Console:SongControlStyle"], ignoreCase: true, out var style)
            ? style
            : SongControlStyle.Sliders,
    };

    private int PageSize(string key, int fallback = AppSettings.DefaultPageSize) =>
        PaginationClamp(_configuration.GetValue<int?>($"Pagination:{key}") ?? fallback);

    // Clamped on read as well as on save: a hand-edited zero would end every ad the instant it
    // started, and a hand-edited hour would hold the room until someone restarted the console.
    private static double AdDurationClamp(double seconds) =>
        Math.Clamp(seconds, AppSettings.MinAdDurationSeconds, AppSettings.MaxAdDurationSeconds);

    private static int PaginationClamp(int pageSize) =>
        Math.Clamp(pageSize, AppSettings.MinPageSize, AppSettings.MaxPageSize);

    private static string? NormalizeMediaDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<AppSettingsSaveResult> SaveAsync(AppSettings settings)
    {
        var before = Current;

        if (settings.RequireLogin && !before.RequireLogin && !await _usersService.HasAdminWithPasswordAsync())
        {
            return new AppSettingsSaveResult(false,
                "No admin user has a password yet — requiring sign-in now would lock everyone out. "
                + "Set a password on an admin in the Users Manager first.");
        }

        var overlay = new Dictionary<string, object?>
        {
            ["Auth"] = new Dictionary<string, object?> { ["RequireLogin"] = settings.RequireLogin },
            ["Playback"] = new Dictionary<string, object?>
            {
                ["StopFadeDuration"] = TimeSpan.FromSeconds(settings.StopFadeSeconds).ToString(),
                ["SyncStartLead"] = TimeSpan.FromMilliseconds(settings.SyncStartLeadMilliseconds).ToString(),
                ["DefaultBackingVolume"] = AudioLevels.ClampVolume(settings.BackingVocalVolume),
            },
            ["MediaStream"] = new Dictionary<string, object?> { ["SegmentSeconds"] = settings.SegmentSeconds },
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

        overlay["LocalScreen"] = new Dictionary<string, object?>
        {
            ["LaunchOnStartup"] = settings.LaunchScreenOnStartup,
        };

        if (!string.IsNullOrWhiteSpace(settings.FFmpegPath))
            overlay["FFmpegPath"] = settings.FFmpegPath;

        var mediaDirectory = NormalizeMediaDirectory(settings.MediaDirectory);
        if (mediaDirectory is not null)
            overlay["Plugins"] = new Dictionary<string, object?> { ["MediaDirectory"] = mediaDirectory };

        Directory.CreateDirectory(Path.GetDirectoryName(_overlayPath)!);
        await File.WriteAllTextAsync(
            _overlayPath,
            JsonSerializer.Serialize(overlay, new JsonSerializerOptions { WriteIndented = true }));

        // Read once, on the way up: turning it on now would not open a screen, and turning it
        // off would not close the one already running.
        if (settings.FFmpegPath != before.FFmpegPath
            || settings.LaunchScreenOnStartup != before.LaunchScreenOnStartup)
            RestartRequired = true;

        return new AppSettingsSaveResult(true);
    }
}
