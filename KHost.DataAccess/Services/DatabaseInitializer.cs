using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Services;

internal class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<DefaultContext> _contextFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IDbContextFactory<DefaultContext> contextFactory, ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var databaseFilePath = Path.Combine(AppContext.BaseDirectory, "cache", "khost.db");
        var databaseDirectory = Path.GetDirectoryName(databaseFilePath);

        _logger.LogInformation("Ensuring database directory exists at {Path}", databaseDirectory);
        if (!Directory.Exists(databaseDirectory))
            Directory.CreateDirectory(databaseDirectory!);

        _logger.LogInformation("Creating database context");
        using var context = await _contextFactory.CreateDbContextAsync();

        _logger.LogInformation("Running EF Core migrations");
        await context.Database.MigrateAsync();
        _logger.LogInformation("Database initialization complete");
    }
}
