using KHost.Abstractions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.DataAccess.Services;

internal class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<DefaultContext> _contextFactory;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly IOptionsMonitor<ServiceOptions> _options;
    private readonly IUsersService _usersService;
    private readonly IUserGroupsService _userGroupsService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IVenuesService _venuesService;
    private readonly IMediaService _mediaService;
    private readonly IMediaFileParsingService _mediaFileParsingService;

    public DatabaseInitializer(
        IDbContextFactory<DefaultContext> contextFactory,
        ILogger<DatabaseInitializer> logger,
        IOptionsMonitor<ServiceOptions> options,
        IUsersService usersService,
        IUserGroupsService userGroupsService,
        IPasswordHasher passwordHasher,
        IVenuesService venuesService,
        IMediaService mediaService,
        IMediaFileParsingService mediaFileParsingService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _options = options;
        _usersService = usersService;
        _userGroupsService = userGroupsService;
        _passwordHasher = passwordHasher;
        _venuesService = venuesService;
        _mediaService = mediaService;
        _mediaFileParsingService = mediaFileParsingService;
    }

    public async Task InitializeAsync()
    {
        var databaseDirectory = DatabaseLocation.DirectoryPath;

        _logger.LogInformation("Ensuring database directory exists at {Path}", databaseDirectory);
        if (!Directory.Exists(databaseDirectory))
            Directory.CreateDirectory(databaseDirectory!);

        _logger.LogInformation("Creating database context");
        using var context = await _contextFactory.CreateDbContextAsync();

        _logger.LogInformation("Running EF Core migrations");
        await context.Database.MigrateAsync();

        await RefoldStoredTextAsync();
        await SeedDefaultAdminUserAsync();
        await SeedDefaultVenueAsync();
        await SeedDefaultMediaAsync();

        _logger.LogInformation("Database initialization complete");
    }

    /// <summary>
    /// Repairs folded names that SQL could not compute. The migration seeds NameFolded with
    /// SQLite's lower(), which leaves non-ASCII case alone, so a singer stored as "Ándre" would
    /// never be found again. Cheap because the roster is small, and self-healing if the folding
    /// rule itself ever changes.
    /// </summary>
    internal async Task RefoldStoredTextAsync()
    {
        await RefoldUserNamesAsync();
        await RefoldVenueNamesAsync();
        await RefoldUserGroupNamesAsync();
        await RefoldTipNotesAsync();
        await RefoldMediaSearchTextAsync();
    }

    internal async Task RefoldUserNamesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        // Projected, not materialised: setting Name on a loaded entity refolds it in memory, so a
        // KHostUser always looks correct and the stored value can only be read as a raw column.
        var stored = await context.Users
            .Select(user => new { user.Id, user.Name, user.NameFolded })
            .ToListAsync();

        var stale = stored.Where(row => row.NameFolded != TextFolding.Fold(row.Name)).ToList();
        if (stale.Count == 0)
            return;

        foreach (var row in stale)
        {
            var folded = TextFolding.Fold(row.Name);
            try
            {
                await context.Users
                    .Where(user => user.Id == row.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(user => user.NameFolded, folded));
            }
            catch (DbUpdateException ex)
            {
                // Two singers whose names differed only outside ASCII already existed - the
                // duplicate the old ASCII-only index should never have allowed through.
                _logger.LogError(ex, "Could not refold '{Name}': another user already folds to the same name", row.Name);
            }
        }

        _logger.LogInformation("Refolded {Count} user name(s) for comparison", stale.Count);
    }

    internal async Task RefoldVenueNamesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var stale = (await context.Venues.Select(v => new { v.Id, v.Name, v.NameFolded }).ToListAsync())
            .Where(row => row.NameFolded != TextFolding.Fold(row.Name))
            .ToList();

        foreach (var row in stale)
        {
            var folded = TextFolding.Fold(row.Name);
            await context.Venues.Where(v => v.Id == row.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(v => v.NameFolded, folded));
        }

        LogRefold("venue name", stale.Count);
    }

    internal async Task RefoldUserGroupNamesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var stale = (await context.UserGroups.Select(g => new { g.Id, g.Name, g.NameFolded }).ToListAsync())
            .Where(row => row.NameFolded != TextFolding.Fold(row.Name))
            .ToList();

        foreach (var row in stale)
        {
            var folded = TextFolding.Fold(row.Name);
            await context.UserGroups.Where(g => g.Id == row.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(g => g.NameFolded, folded));
        }

        LogRefold("user group name", stale.Count);
    }

    internal async Task RefoldTipNotesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var stale = (await context.Tips.Select(t => new { t.Id, t.Notes, t.NotesFolded }).ToListAsync())
            .Where(row => row.NotesFolded != TextFolding.Fold(row.Notes))
            .ToList();

        foreach (var row in stale)
        {
            var folded = TextFolding.Fold(row.Notes);
            await context.Tips.Where(t => t.Id == row.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.NotesFolded, folded));
        }

        LogRefold("tip note", stale.Count);
    }

    /// <summary>
    /// The library is the one table here big enough to notice. Only the folded text is read back,
    /// and only rows that actually differ are written, so a library that is already correct costs
    /// one scan of three columns at startup.
    /// </summary>
    internal async Task RefoldMediaSearchTextAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var stale = (await context.Media
                .Select(m => new { m.Id, m.Title, m.Artist, m.Notes, m.SearchFolded })
                .ToListAsync())
            .Select(row => new { row.Id, Folded = TextFolding.Fold($"{row.Title} {row.Artist} {row.Notes}"), row.SearchFolded })
            .Where(row => row.SearchFolded != row.Folded)
            .ToList();

        foreach (var row in stale)
        {
            var folded = row.Folded;
            await context.Media.Where(m => m.Id == row.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.SearchFolded, folded));
        }

        LogRefold("media search text", stale.Count);
    }

    private void LogRefold(string what, int count)
    {
        if (count > 0)
            _logger.LogInformation("Refolded {Count} {What}(s) for comparison", count, what);
    }

    internal async Task SeedDefaultAdminUserAsync()
    {
        var options = _options.CurrentValue;
        if (options.DefaultAdminUser is null)
            return;

        var hasAdminUser = await _usersService.HasAdminUserAsync();
        if (hasAdminUser)
        {
            _logger.LogInformation("Admin user already exists, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default admin user");
        var passwordHash = await _passwordHasher.HashAsync(options.DefaultAdminUser.Password);
        var adminUser = new KHostUser { Name = options.DefaultAdminUser.Username, PasswordHash = passwordHash };
        var createdUser = await _usersService.CreateAsync(adminUser);
        await _userGroupsService.AddUserToGroupAsync(createdUser.Id, KHostUserGroup.AdminGroupId);
        _logger.LogInformation("Default admin user created: {Username}", options.DefaultAdminUser.Username);
    }

    internal async Task SeedDefaultVenueAsync()
    {
        var options = _options.CurrentValue;
        if (options.DefaultVenue is null)
            return;

        var hasVenue = await _venuesService.HasAnyAsync();
        if (hasVenue)
        {
            _logger.LogInformation("Venue already exists, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default venue");
        var venue = new Venue { Name = options.DefaultVenue.Name, Enabled = true };
        var createdVenue = await _venuesService.CreateAsync(venue);
        await _venuesService.SelectVenueAsync(createdVenue.Id);
        _logger.LogInformation("Default venue created: {VenueName}", options.DefaultVenue.Name);
    }

    internal async Task SeedDefaultMediaAsync()
    {
        var options = _options.CurrentValue;
        if (options.DefaultMedia is null || options.DefaultMedia.Count == 0)
            return;

        var hasMedia = await _mediaService.HasAnyAsync();
        if (hasMedia)
        {
            _logger.LogInformation("Media already exists, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default media ({Count} files)", options.DefaultMedia.Count);
        foreach (var entry in options.DefaultMedia)
            await SeedOneMediaFileAsync(entry);
    }

    private async Task SeedOneMediaFileAsync(ServiceOptions.DefaultMediaOptions entry)
    {
        try
        {
            var media = await _mediaFileParsingService.LoadAndParseAsync(entry.FilePath);
            await _mediaService.CreateAsync(media);
            _logger.LogInformation("Seeded media: {FilePath}", entry.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed media file: {FilePath}", entry.FilePath);
        }
    }

    public class ServiceOptions
    {
        public const string SectionName = "DatabaseInitializer";
        public DefaultAdminUserOptions? DefaultAdminUser { get; set; }
        public DefaultVenueOptions? DefaultVenue { get; set; }
        public List<DefaultMediaOptions>? DefaultMedia { get; set; }

        public class DefaultAdminUserOptions
        {
            public string Username { get; set; } = "admin";
            public string Password { get; set; } = "admin";
        }

        public class DefaultVenueOptions
        {
            public string Name { get; set; } = "Default Venue";
        }

        public class DefaultMediaOptions
        {
            public required string FilePath { get; set; }
        }
    }
}
