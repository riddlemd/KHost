using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using KHost.DataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Services;

public class RetiredVenueSettingsReaderTests : IDisposable
{
    // Thrown, not logged: startup runs this read, and an unordered First there warns on every launch.
    private readonly SqliteTestDatabase _database = new(options => options.ConfigureWarnings(warnings => warnings.Throw(
        CoreEventId.FirstWithoutOrderByAndFilterWarning,
        CoreEventId.RowLimitingOperationWithoutOrderByWarning)));
    private readonly RetiredVenueSettingsReader _reader;

    public RetiredVenueSettingsReaderTests()
        => _reader = new RetiredVenueSettingsReader(_database);

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>What a host from before the mode moved to App Settings left in the row.</summary>
    internal static async Task<Guid> SeedVenueAsync(SqliteTestDatabase database, string? oldProvider)
    {
        var venue = await new VenuesRepository(database, NullLogger<BaseRepository<Venue>>.Instance)
            .CreateAsync(new Venue { Name = $"The Alley {Guid.NewGuid():n}" });

        if (oldProvider is not null)
        {
            using var context = database.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Venues SET Settings = json_set(Settings, '$.BreakMusicProvider', {0}) WHERE Id = {1}",
                oldProvider, venue.Id);
        }

        return venue.Id;
    }

    [Fact]
    public async Task ReadBreakMusicProviderAsync_AVenueThatChoseOne_ReadsIt()
    {
        var id = await SeedVenueAsync(_database, " JukeboxProvider ");

        Assert.Equal("JukeboxProvider", await _reader.ReadBreakMusicProviderAsync(id));
    }

    [Fact]
    public async Task ReadBreakMusicProviderAsync_AVenueThatNeverChose_ReadsNull()
    {
        var id = await SeedVenueAsync(_database, null);

        Assert.Null(await _reader.ReadBreakMusicProviderAsync(id));
    }

    /// <summary>A cleared field is no choice, not a provider named nothing.</summary>
    [Fact]
    public async Task ReadBreakMusicProviderAsync_ABlankChoice_ReadsNull()
    {
        var id = await SeedVenueAsync(_database, "   ");

        Assert.Null(await _reader.ReadBreakMusicProviderAsync(id));
    }

    [Fact]
    public async Task ReadBreakMusicProviderAsync_AnotherVenuesChoice_IsNotRead()
    {
        await SeedVenueAsync(_database, "JukeboxProvider");
        var id = await SeedVenueAsync(_database, null);

        Assert.Null(await _reader.ReadBreakMusicProviderAsync(id));
    }
}
