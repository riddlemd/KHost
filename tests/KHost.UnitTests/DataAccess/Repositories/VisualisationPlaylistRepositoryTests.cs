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
// Unpooled so each file can be deleted once its context closes. Never SqliteConnection.ClearAllPools():
// it disposes every pooled handle in the process, including ones other tests are using right now.
public class VisualisationPlaylistRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-visualisations-{Guid.NewGuid():N}.db");
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly VisualisationPlaylistRepository _repository;

    public VisualisationPlaylistRepositoryTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath};Pooling=False")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new VisualisationPlaylistRepository(_factory, NullLogger<BaseRepository<VisualisationPlaylist>>.Instance);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheMigrations_LeaveNothingOfTheModelUnmigrated()
    {
        using var context = _factory.CreateDbContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    /// <summary>A fresh install: migrating from nothing seeds the built-in playlist, which is what
    /// every install (including this test's own database) goes through.</summary>
    [Fact]
    public async Task Migrate_AFreshDatabase_SeedsTheDefaultPlaylistWithTheAmbientScenesInOrder()
    {
        var playlist = await _repository.ReadWithEntriesAsync(VisualisationPlaylist.DefaultId);

        Assert.NotNull(playlist);
        Assert.Equal(("Default Visualizations", false), (playlist!.Name, playlist.Shuffle));
        Assert.Equal(
            ["ambient-gradient", "ambient-bokeh", "ambient-embers", "ambient-rings", "ambient-beams"],
            playlist.Entries.Select(e => e.PresetName));
        Assert.Equal([0, 1, 2, 3, 4], playlist.Entries.Select(e => e.Position));
        Assert.All(playlist.Entries, e => Assert.Equal(VisualiserPresetSource.BuiltIn, e.PresetSource));
    }

    /// <summary>A database that predates this migration gets the row on its next upgrade, same as a
    /// fresh install — the case an existing, already-in-use host hits.</summary>
    [Fact]
    public async Task Migrate_ADatabaseFromBeforeTheDefaultPlaylist_GetsItOnUpgrade()
    {
        var path = Path.Combine(Path.GetTempPath(), $"khost-visualisations-preexisting-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DefaultContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            await using var context = new DefaultContext(options);
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260928172930_AddBuiltInVisualisations");

            Assert.Empty(context.VisualisationPlaylists);

            await context.Database.MigrateAsync();

            var playlist = await context.VisualisationPlaylists
                .Include(p => p.Entries.OrderBy(e => e.Position))
                .SingleAsync(p => p.Id == VisualisationPlaylist.DefaultId);
            Assert.Equal(
                ["ambient-gradient", "ambient-bokeh", "ambient-embers", "ambient-rings", "ambient-beams"],
                playlist.Entries.Select(e => e.PresetName));
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
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
            new() { Id = keptId, PresetName = "Second", PresetSource = VisualiserPresetSource.Imported, Brightness = 80, Saturation = 150, Sensitivity = 250, Position = 9 },
            new() { Id = Guid.Empty, PresetName = "First" },
        ]);

        var loaded = await _repository.ReadWithEntriesAsync(playlist.Id);

        Assert.Equal(["Second", "First"], loaded!.Entries.Select(e => e.PresetName));
        Assert.Equal([0, 1], loaded.Entries.Select(e => e.Position));
        var first = loaded.Entries[0];
        Assert.Equal((keptId, VisualiserPresetSource.Imported, 80, 150, 250),
            (first.Id, first.PresetSource, first.Brightness, first.Saturation, first.Sensitivity));
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
        var options = new DbContextOptionsBuilder<DefaultContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
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

                // Not .SingleAsync(): the same run also seeds the default playlist's five entries.
                var entry = await context.VisualisationEntries.SingleAsync(e => e.VisualisationPlaylistId == Guid.Parse(playlistId));
                Assert.Equal((VisualisationEntry.DefaultBarCount, VisualiserColourScheme.Classic, VisualisationEntry.DefaultColour),
                    (entry.BarCount, entry.ColourScheme, entry.Colour));
            }
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>The dropped column takes nothing with it: a row written while it still existed
    /// survives the migration, PresetName and all.</summary>
    [Fact]
    public async Task Migrate_DropsDarkenBehindWords_AndKeepsTheRowThatHadIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"khost-visualisations-darken-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DefaultContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            await using (var context = new DefaultContext(options))
            {
                // The last migration to still carry the column.
                await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260928183750_AddDefaultVisualisationPlaylist");
                var playlistId = Guid.NewGuid().ToString().ToUpperInvariant();
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO VisualisationPlaylists (Id, Name, NameFolded, Shuffle) VALUES ({0}, 'Old', 'old', 0)", playlistId);
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO VisualisationEntries (Id, VisualisationPlaylistId, Position, PresetSource, PresetName, Brightness, Saturation, Sensitivity, DarkenBehindWords) VALUES ({0}, {1}, 0, 0, 'KeptAcrossTheDrop', 100, 100, 100, 1)",
                    Guid.NewGuid().ToString().ToUpperInvariant(), playlistId);

                await context.Database.MigrateAsync();

                var entry = await context.VisualisationEntries.SingleAsync(e => e.VisualisationPlaylistId == Guid.Parse(playlistId));
                Assert.Equal("KeptAcrossTheDrop", entry.PresetName);
            }
        }
        finally
        {
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

        // "Default Visualizations" is the migration-seeded playlist, folding between the two.
        Assert.Equal(["alpha", "Default Visualizations", "Zed"], all.Select(p => p.Name));
        Assert.Equal(["Z1", "Z2"], all.Single(p => p.Name == "Zed").Entries.Select(e => e.PresetName));
        Assert.Empty(all.Single(p => p.Name == "alpha").Entries);
    }

    [Fact]
    public async Task DeleteAsync_TakesTheEntriesWithIt()
    {
        var playlist = await _repository.CreateAsync(new VisualisationPlaylist { Name = "Night" });
        await _repository.ReplaceEntriesAsync(playlist.Id, [new() { PresetName = "A" }]);

        Assert.True(await _repository.DeleteAsync(playlist.Id));

        using var context = _factory.CreateDbContext();
        // Not Assert.Empty(): the seeded default playlist's own entries are still there.
        Assert.DoesNotContain(context.VisualisationEntries, e => e.VisualisationPlaylistId == playlist.Id);
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
