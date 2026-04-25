using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Services;

internal class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<DefaultContext> _contextFactory;
    private readonly IMediaRepository _mediaRepository;
    private readonly IVenuesRepository _venuesRepository;

    public DatabaseInitializer(
        IDbContextFactory<DefaultContext> contextFactory,
        IMediaRepository mediaRepository,
        IVenuesRepository venuesRepository)
    {
        _contextFactory = contextFactory;
        _mediaRepository = mediaRepository;
        _venuesRepository = venuesRepository;
    }

    public async Task InitializeAsync()
    {
        var databaseFilePath = Path.Combine(AppContext.BaseDirectory, "cache", "khost.db");
        var databaseDirectory = Path.GetDirectoryName(databaseFilePath);

        if (!Directory.Exists(databaseDirectory))
            Directory.CreateDirectory(databaseDirectory!);

        using var context = await _contextFactory.CreateDbContextAsync();

        await context.Database.MigrateAsync();

        await SeedVenuesAsync();
        await SeedMediaAsync();
    }

    private async Task SeedVenuesAsync()
    {
        var existingVenues = await _venuesRepository.ReadAllAsync(pageNumber: 1, pageSize: 1);

        if (existingVenues.Items.Count > 0)
            return;

        var defaultVenues = new Venue[]
        {
            new () { Name = "Default Venue", Notes = "The default karaoke venue", Enabled = true },
        };

        foreach (var venue in defaultVenues)
        {
            await _venuesRepository.CreateAsync(venue);
        }
    }

    private async Task SeedMediaAsync()
    {
        var existingMedia = await _mediaRepository.ReadAllAsync(pageNumber: 1, pageSize: 1);

        if (existingMedia.Items.Count > 0)
            return;

        var defaultMedia = new Media[]
        {
            new () { Title = "Bohemian Rhapsody", Artist = "Queen", Format = "mp3", FilePath = "./NOFILE1", Duration = TimeSpan.FromSeconds(30), Status = MediaStatus.Ready },
            new () { Title = "Imagine", Artist = "John Lennon", Format = "mp3", FilePath = "./NOFILE2", Duration = TimeSpan.FromSeconds(120), Status = MediaStatus.Downloading },
            new (){ Title = "Hotel California", Artist = "Eagles", Format = "mp3", FilePath = "./NOFILE3", Duration = TimeSpan.FromSeconds(45), Status = MediaStatus.Broken },
            new (){ Title = "Stairway to Heaven", Artist = "Led Zeppelin", Format = "mp3", FilePath = "./NOFILE4", Duration = TimeSpan.FromSeconds(80), Status = MediaStatus.Processing },
            new () { Title = "Like a Virgin", Artist = "Madonna", Format = "mp3", FilePath = "./NOFILE5", Duration = TimeSpan.FromSeconds(15) }
        };

        foreach (var media in defaultMedia)
        {
            await _mediaRepository.CreateAsync(media);
        }
    }
}
