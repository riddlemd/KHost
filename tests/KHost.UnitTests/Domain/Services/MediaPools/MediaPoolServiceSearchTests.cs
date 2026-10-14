using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services.MediaPools;
using KHost.Domain.Services.Messaging;
using KHost.UnitTests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.MediaPools;

/// <summary>The typed search overload, through the real repository.</summary>
public class MediaPoolServiceSearchTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly MediaPoolService _service;

    public MediaPoolServiceSearchTests()
    {
        _service = new MediaPoolService(
            NullLogger<MediaPoolService>.Instance,
            new MediaPoolRepository(_database, NullLogger<BaseRepository<MediaPool>>.Instance),
            Substitute.For<IVenuesService>(),
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task SearchAsync_WithAPurpose_ReturnsThatPurposeOnly()
    {
        await SeedBedAndSpotsAsync();

        var result = await _service.SearchAsync("House", 1, 50, sort: null,
            new MediaPoolSearchOptions { Purpose = PoolPurpose.Ads });

        Assert.Equal("House Spots", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task SearchAsync_WithNullOptions_ReturnsEveryPurpose()
    {
        await SeedBedAndSpotsAsync();

        var result = await _service.SearchAsync("House", 1, 50, sort: null, options: null);

        Assert.Equal(2, result.TotalCount);
    }

    private Task SeedBedAndSpotsAsync() => _database.SeedAsync(
        new MediaPool { Id = Guid.NewGuid(), Name = "House Bed", Purpose = PoolPurpose.BreakMusic },
        new MediaPool { Id = Guid.NewGuid(), Name = "House Spots", Purpose = PoolPurpose.Ads });

    public void Dispose() => _database.Dispose();
}
