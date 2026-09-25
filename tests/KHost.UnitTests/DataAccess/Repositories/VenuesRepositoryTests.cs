using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class VenuesRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly VenuesRepository _repository;

    public VenuesRepositoryTests()
        => _repository = new VenuesRepository(_database, NullLogger<BaseRepository<Venue>>.Instance);

    [Fact]
    public async Task CreateAsync_RejectsADuplicateName()
    {
        await _repository.CreateAsync(new Venue { Name = "The Alley" });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _repository.CreateAsync(new Venue { Name = "The Alley" }));
    }

    [Fact]
    public async Task CreateAsync_AllowsDistinctNames()
    {
        await _repository.CreateAsync(new Venue { Name = "The Alley" });
        await _repository.CreateAsync(new Venue { Name = "The Alley Annex" });

        Assert.True(await _repository.HasAnyAsync());
    }

    /// <summary>The corner and QR size enums were renamed from ScreenCorner and ScreenQrSize. A venue
    /// saved before that holds them by number, and must open with the same choices.</summary>
    [Fact]
    public async Task ReadAsync_SettingsSavedBeforeTheOverlayEnumsWereRenamed_LoadTheSameChoices()
    {
        var venue = await _repository.CreateAsync(new Venue { Name = "The Alley" });

        // What an older host wrote: TopRight, Large, TopLeft.
        using (var context = _database.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Venues SET Settings = json_set(Settings, '$.QrCodeCorner', 2, '$.QrCodeSize', 2, '$.BreakMusicCardCorner', 3) WHERE Id = {0}",
                venue.Id);
        }

        var settings = (await _repository.ReadAsync(venue.Id))!.Settings;

        Assert.Equal(OverlayCorner.TopRight, settings.QrCodeCorner);
        Assert.Equal(QrCodeSize.Large, settings.QrCodeSize);
        Assert.Equal(OverlayCorner.TopLeft, settings.BreakMusicCardCorner);
    }

    /// <summary>By number, not by name, which is why the rename needed no migration and why the
    /// values must never be reordered.</summary>
    [Fact]
    public async Task CreateAsync_StoresTheOverlayEnumsByNumber()
    {
        var venue = await _repository.CreateAsync(new Venue
        {
            Name = "The Alley",
            Settings = new Venue.VenueSettings { QrCodeCorner = OverlayCorner.BottomLeft, QrCodeSize = QrCodeSize.Small },
        });

        using var context = _database.CreateDbContext();
        var stored = await context.Database
            .SqlQueryRaw<string>(
                "SELECT json_type(Settings, '$.QrCodeCorner') || ':' || json_extract(Settings, '$.QrCodeCorner') || ',' || json_extract(Settings, '$.QrCodeSize') AS Value FROM Venues WHERE Id = {0}",
                venue.Id)
            .SingleAsync();

        Assert.Equal("integer:1,0", stored);
    }

    public void Dispose() => _database.Dispose();
}
