using System.Text.Json;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Configuration;

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

    public AppSettings Current => new()
    {
        RequireLogin = _configuration.GetValue<bool?>("Auth:RequireLogin") ?? true,
        FFmpegPath = _configuration["FFmpegPath"],
        StopFadeSeconds = (_configuration.GetValue<TimeSpan?>("Playback:StopFadeDuration") ?? TimeSpan.FromSeconds(5)).TotalSeconds,
        SyncStartLeadMilliseconds = (_configuration.GetValue<TimeSpan?>("Playback:SyncStartLead") ?? TimeSpan.FromMilliseconds(400)).TotalMilliseconds,
        SegmentSeconds = _configuration.GetValue<int?>("MediaStream:SegmentSeconds") ?? 2,
        MediaPageSize = PageSize("Media"),
        UsersPageSize = PageSize("Users"),
        UserGroupsPageSize = PageSize("UserGroups"),
        TipsPageSize = PageSize("Tips"),
        VenuesPageSize = PageSize("Venues"),
        PerformanceHistoryPageSize = PageSize("PerformanceHistory", AppSettings.DefaultPerformanceHistoryPageSize),
    };

    // Clamped on read as well as on save: the overlay is a plain JSON file an operator can edit
    // by hand, and a 0 there would leave every list permanently empty.
    private int PageSize(string key, int fallback = AppSettings.DefaultPageSize) =>
        PaginationClamp(_configuration.GetValue<int?>($"Pagination:{key}") ?? fallback);

    private static int PaginationClamp(int pageSize) =>
        Math.Clamp(pageSize, AppSettings.MinPageSize, AppSettings.MaxPageSize);

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
            },
            ["MediaStream"] = new Dictionary<string, object?> { ["SegmentSeconds"] = settings.SegmentSeconds },
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

        if (!string.IsNullOrWhiteSpace(settings.FFmpegPath))
            overlay["FFmpegPath"] = settings.FFmpegPath;

        Directory.CreateDirectory(Path.GetDirectoryName(_overlayPath)!);
        await File.WriteAllTextAsync(
            _overlayPath,
            JsonSerializer.Serialize(overlay, new JsonSerializerOptions { WriteIndented = true }));

        if (settings.FFmpegPath != before.FFmpegPath)
            RestartRequired = true;

        return new AppSettingsSaveResult(true);
    }
}
