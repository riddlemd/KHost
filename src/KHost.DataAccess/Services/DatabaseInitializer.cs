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

        await SweepStalledDownloadsAsync();
        await RefoldStoredTextAsync();
        await SeedDefaultAdminUserAsync();
        await SeedDefaultVenueAsync();
        await SeedDefaultMediaAsync();

        _logger.LogInformation("Database initialization complete");
    }

    /// <summary>
    /// Rewrites folded columns whose source text no longer folds to what is stored. Needed because
    /// a migration can only seed them with SQLite's lower(), which folds ASCII and nothing else,
    /// and because changing the folding rule itself has to reach rows already in the database.
    /// Saving is what refolds them — the context folds on the way out.
    /// </summary>
    internal async Task RefoldStoredTextAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var repaired =
            Repair(context, context.Users, u => u.NameFolded, u => EntityFolding.Fold(u.Name), "user name") +
            Repair(context, context.Venues, v => v.NameFolded, v => EntityFolding.Fold(v.Name), "venue name") +
            Repair(context, context.UserGroups, g => g.NameFolded, g => EntityFolding.Fold(g.Name), "user group name") +
            Repair(context, context.Tips, t => t.NotesFolded, t => EntityFolding.Fold(t.Notes), "tip note") +
            Repair(context, context.Media, m => m.SearchFolded,
                m => EntityFolding.FoldMedia($"{m.Title} {m.Artist}"), "media search text");

        if (repaired == 0)
            return;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Two rows the old rule kept apart now fold together, and a unique index refuses the
            // write. Nothing is lost: the rows stay as they are, one of them still folded the old
            // way, and it remains reachable by its exact spelling.
            _logger.LogError(ex, "Could not refold stored text: two rows fold to the same value");
        }
    }

    /// <summary>
    /// Flips every still-Downloading row to Broken — the host process that owned those downloads'
    /// cancellation tokens is gone, so nothing will ever settle them otherwise. Never deleted: the
    /// file a download left behind may still be on disk, and a deleted row would let a later
    /// import silently re-register that partial file as Ready.
    /// </summary>
    internal async Task SweepStalledDownloadsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var stalled = await context.Media.Where(m => m.Status == MediaStatus.Downloading).ToListAsync();
        if (stalled.Count == 0)
            return;

        // NoTracking context: mutating the property alone leaves nothing for SaveChanges to see,
        // so each row has to be attached and marked Modified explicitly.
        foreach (var media in stalled)
        {
            media.Status = MediaStatus.Broken;
            context.Update(media);
        }

        await context.SaveChangesAsync();

        _logger.LogWarning("Swept {Count} stalled download(s) left Downloading by an unclean shutdown to Broken", stalled.Count);
    }

    private int Repair<T>(
        DefaultContext context,
        DbSet<T> set,
        Func<T, string> stored,
        Func<T, string> expected,
        string what)
        where T : class
    {
        var stale = set.AsEnumerable().Where(entity => stored(entity) != expected(entity)).ToList();
        if (stale.Count == 0)
            return 0;

        // Attaching is enough: the context folds every changed entity as it saves.
        foreach (var entity in stale)
            context.Update(entity);

        _logger.LogInformation("Refolding {Count} {What}(s)", stale.Count, what);
        return stale.Count;
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
