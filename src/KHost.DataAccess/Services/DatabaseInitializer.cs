using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Services;

internal class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<DefaultContext> _contextFactory;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly IVisualisationPlaylistService _visualisationPlaylistService;
    private readonly IVisualiserPresetService _visualiserPresetService;

    public DatabaseInitializer(
        IDbContextFactory<DefaultContext> contextFactory,
        ILogger<DatabaseInitializer> logger,
        IVisualisationPlaylistService visualisationPlaylistService,
        IVisualiserPresetService visualiserPresetService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _visualisationPlaylistService = visualisationPlaylistService;
        _visualiserPresetService = visualiserPresetService;
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
        await ReconcileFileLifetimeAsync();
        await EnsureShippedVisualisationPlaylistsAsync();

        _logger.LogInformation("Database initialization complete");
    }

    internal async Task SweepStalledDownloadsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        // MediaStatuses.Acquiring rather than IsAcquiring(): this has to reach SQL as an IN clause,
        // and a row left mid-render is as stalled as one left mid-download.
        var stalled = await context.Media.Where(m => MediaStatuses.Acquiring.Contains(m.Status)).ToListAsync();
        if (stalled.Count == 0)
            return;

        // NoTracking context: mutating the property alone leaves nothing for SaveChanges to see,
        // so each row has to be attached and marked Modified explicitly.
        foreach (var media in stalled)
        {
            // A row its provider asked the host to keep waits to be fetched again rather than for a host to mend it.
            media.Status = media.HasFileLifetime() && !File.Exists(media.FilePath) ? MediaStatus.NotDownloaded : MediaStatus.Broken;
            context.Update(media);
        }

        await context.SaveChangesAsync();

        _logger.LogWarning("Swept {Count} stalled download(s) left mid-download by an unclean shutdown to Broken", stalled.Count);
    }

    /// <summary>Squares a marked row's status with its file: a file gone while KHost was not looking
    /// (a kill before the close pass, a hand delete) waits to be fetched again, and one put back is playable.</summary>
    internal async Task ReconcileFileLifetimeAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var marked = await context.Media.Where(m => m.IsEphemeral || m.IsSingleUse).ToListAsync();
        var moved = 0;

        foreach (var media in marked)
        {
            var next = media.Status switch
            {
                MediaStatus.Ready when !File.Exists(media.FilePath) => MediaStatus.NotDownloaded,
                MediaStatus.NotDownloaded when File.Exists(media.FilePath) => MediaStatus.Ready,
                var same => same,
            };

            if (next == media.Status) continue;

            media.Status = next;
            context.Update(media);
            moved++;
        }

        if (moved == 0) return;

        await context.SaveChangesAsync();
        _logger.LogInformation("Squared {Count} ephemeral or single-use row(s) with their files at startup", moved);
    }

    /// <summary>Puts either shipped playlist back if it is somehow gone. The migrations seed new
    /// and upgraded databases once; this covers a row deleted out from under the app some other way,
    /// since the service itself refuses to delete them.</summary>
    internal async Task EnsureShippedVisualisationPlaylistsAsync()
    {
        // Reads the live ambient set rather than a copy, so an ambient scene added later is
        // covered by a restore without this method needing to change.
        var ambient = _visualiserPresetService.ReadAll()
            .Where(p => p.Source == VisualiserPresetSource.BuiltIn && p.Name.StartsWith("ambient-", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        await EnsurePlaylistAsync(ShippedVisualisationPlaylists.BasicId, ShippedVisualisationPlaylists.BasicName,
            ambient.Where(name => !ShippedVisualisationPlaylists.AdvancedScenes.Contains(name)));
        await EnsurePlaylistAsync(ShippedVisualisationPlaylists.AdvancedId, ShippedVisualisationPlaylists.AdvancedName,
            ambient.Where(ShippedVisualisationPlaylists.AdvancedScenes.Contains));
    }

    private async Task EnsurePlaylistAsync(Guid id, string name, IEnumerable<string> scenes)
    {
        if (await _visualisationPlaylistService.ReadWithEntriesAsync(id) is not null)
            return;

        _logger.LogWarning("The shipped visualisation playlist {Name} was missing; recreating it", name);

        var entries = scenes.Select(scene => new VisualisationEntry { PresetSource = VisualiserPresetSource.BuiltIn, PresetName = scene }).ToList();
        await _visualisationPlaylistService.CreateAsync(new VisualisationPlaylist { Id = id, Name = name });
        await _visualisationPlaylistService.ReplaceEntriesAsync(id, entries);
    }
}
