using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class MediaCacheLoaderTests
{
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();

    [Fact]
    public async Task ReadByIdAsync_ReadsEveryDistinctId_Once()
    {
        var first = new Media { Id = Guid.NewGuid(), FilePath = "/a.mp4", Title = "A" };
        var second = new Media { Id = Guid.NewGuid(), FilePath = "/b.mp4", Title = "B" };
        _mediaService.ReadAsync(first.Id).Returns(first);
        _mediaService.ReadAsync(second.Id).Returns(second);

        var result = await MediaCacheLoader.ReadByIdAsync(_mediaService, [first.Id, second.Id, first.Id]);

        Assert.Equal(2, result.Count);
        Assert.Same(first, result[first.Id]);
        Assert.Same(second, result[second.Id]);
        await _mediaService.Received(1).ReadAsync(first.Id);
    }

    // A deleted row still surfaces its id: a caller reads by TryGetValue, and the missing-media
    // fallback has to fire whether the id is absent or maps to null.
    [Fact]
    public async Task ReadByIdAsync_KeepsAnEntry_ForAnIdTheServiceCannotFind()
    {
        var missingId = Guid.NewGuid();
        _mediaService.ReadAsync(missingId).Returns((Media?)null);

        var result = await MediaCacheLoader.ReadByIdAsync(_mediaService, [missingId]);

        Assert.True(result.TryGetValue(missingId, out var media));
        Assert.Null(media);
    }
}
