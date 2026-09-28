using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>Against a database built by the migrations, not EnsureCreated, so a model change with no
/// migration behind it fails here.</summary>
public class VisualisationPlaylistRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-visualisations-{Guid.NewGuid():N}.db");
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly VisualisationPlaylistRepository _repository;

    public VisualisationPlaylistRepositoryTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new VisualisationPlaylistRepository(_factory, NullLogger<BaseRepository<VisualisationPlaylist>>.Instance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheMigrations_LeaveNothingOfTheModelUnmigrated()
    {
        using var context = _factory.CreateDbContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CreateAsync_FoldsTheName()
    {
        var created = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Björk Night", Shuffle = true });

        var stored = await _repository.ReadAsync(created.Id);

        Assert.Equal("bjork night", stored!.NameFolded);
        Assert.True(stored.Shuffle);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_KeepsTheOrderGivenAndEverySetting()
    {
        var playlist = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Night" });
        var keptId = Guid.NewGuid();

        await _repository.ReplaceEntriesAsync(playlist.Id,
        [
            new() { Id = keptId, PresetName = "Second", PresetSource = VisualiserPresetSource.Imported, Brightness = 80, Saturation = 150, Sensitivity = 250, DarkenBehindWords = false, Position = 9 },
            new() { Id = Guid.Empty, PresetName = "First" },
        ]);

        var loaded = await _repository.ReadWithEntriesAsync(playlist.Id);

        Assert.Equal(["Second", "First"], loaded!.Entries.Select(e => e.PresetName));
        Assert.Equal([0, 1], loaded.Entries.Select(e => e.Position));
        var first = loaded.Entries[0];
        Assert.Equal((keptId, VisualiserPresetSource.Imported, 80, 150, 250, false),
            (first.Id, first.PresetSource, first.Brightness, first.Saturation, first.Sensitivity, first.DarkenBehindWords));
        Assert.NotEqual(Guid.Empty, loaded.Entries[1].Id);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_KeepsABuiltInsOptions()
    {
        var playlist = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Night" });

        await _repository.ReplaceEntriesAsync(playlist.Id,
        [
            new() { PresetName = "mirrored-bars", PresetSource = VisualiserPresetSource.BuiltIn, BarCount = 64, ColourScheme = VisualiserColourScheme.Single, Colour = "#ff8800" },
        ]);

        var entry = Assert.Single((await _repository.ReadWithEntriesAsync(playlist.Id))!.Entries);
        Assert.Equal((VisualiserPresetSource.BuiltIn, "mirrored-bars", 64, VisualiserColourScheme.Single, "#ff8800"),
            (entry.PresetSource, entry.PresetName, entry.BarCount, entry.ColourScheme, entry.Colour));
    }

    /// <summary>An entry saved before the built-in options existed reads as a fresh entry would.</summary>
    [Fact]
    public async Task Migrate_AnEntryFromBeforeTheBuiltIns_TakesTheDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"khost-visualisations-old-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DefaultContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            await using (var context = new DefaultContext(options))
            {
                await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260928135813_AddVisualisationPlaylists");
                // As EF writes a Guid here: upper-case text, which a bound Guid (a blob) is not.
                var playlistId = Guid.NewGuid().ToString().ToUpperInvariant();
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO VisualisationPlaylists (Id, Name, NameFolded, Shuffle) VALUES ({0}, 'Old', 'old', 0)", playlistId);
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO VisualisationEntries (Id, VisualisationPlaylistId, Position, PresetSource, PresetName, Brightness, Saturation, Sensitivity, DarkenBehindWords) VALUES ({0}, {1}, 0, 0, '_Mig_049', 100, 100, 100, 1)",
                    Guid.NewGuid().ToString().ToUpperInvariant(), playlistId);

                await context.Database.MigrateAsync();

                var entry = await context.VisualisationEntries.SingleAsync();
                Assert.Equal((VisualisationEntry.DefaultBarCount, VisualiserColourScheme.Classic, VisualisationEntry.DefaultColour),
                    (entry.BarCount, entry.ColourScheme, entry.Colour));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ReplaceEntriesAsync_AReorder_ReplacesTheOldList()
    {
        var playlist = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Night" });
        await _repository.ReplaceEntriesAsync(playlist.Id, [new() { PresetName = "A" }, new() { PresetName = "B" }]);
        var first = (await _repository.ReadWithEntriesAsync(playlist.Id))!.Entries;

        await _repository.ReplaceEntriesAsync(playlist.Id, [first[1], first[0]]);

        Assert.Equal(["B", "A"], (await _repository.ReadWithEntriesAsync(playlist.Id))!.Entries.Select(e => e.PresetName));
    }

    [Fact]
    public async Task ReadAllWithEntriesAsync_EveryPlaylistByNameWithItsEntries()
    {
        var zed = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Zed" });
        var alpha = await _repository.CreateAsync(new VisualisationPlaylist { Name = "alpha" });
        await _repository.ReplaceEntriesAsync(zed.Id, [new() { PresetName = "Z1" }, new() { PresetName = "Z2" }]);

        var all = await _repository.ReadAllWithEntriesAsync();

        Assert.Equal(["alpha", "Zed"], all.Select(p => p.Name));
        Assert.Equal(["Z1", "Z2"], all[1].Entries.Select(e => e.PresetName));
        Assert.Empty(all[0].Entries);
    }

    [Fact]
    public async Task DeleteAsync_TakesTheEntriesWithIt()
    {
        var playlist = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Night" });
        await _repository.ReplaceEntriesAsync(playlist.Id, [new() { PresetName = "A" }]);

        Assert.True(await _repository.DeleteAsync(playlist.Id));

        using var context = _factory.CreateDbContext();
        Assert.Empty(context.VisualisationEntries);
    }

    [Fact]
    public async Task UpdateAsync_RenamesAndRefolds()
    {
        var playlist = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Night" });

        await _repository.UpdateAsync(new VisualisationPlaylist { Id = playlist.Id, Name = "Späte Nacht", Shuffle = true });

        var stored = await _repository.ReadAsync(playlist.Id);
        Assert.Equal(("Späte Nacht", "spate nacht", true), (stored!.Name, stored.NameFolded, stored.Shuffle));
    }
}
