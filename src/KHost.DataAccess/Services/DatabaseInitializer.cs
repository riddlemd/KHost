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

        await SeedDefaultAdminUserAsync();
        await SeedDefaultVenueAsync();
        await SeedDefaultMediaAsync();

        _logger.LogInformation("Database initialization complete");
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
