using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

public class MediaTests
{
    [Fact]
    public void Media_InitializesWithNewGuidId()
    {
        var media = new Media { FilePath = "/path/to/media.mp4", Title = "Media" };

        Assert.NotEqual(Guid.Empty, media.Id);
    }

    [Fact]
    public void Media_HasRandomDurationByDefault()
    {
        var media = new Media { FilePath = "/path/to/media.mp4", Title = "Media", Duration = TimeSpan.FromSeconds(45) };

        Assert.NotNull(media.Duration);
        Assert.True(media.Duration!.Value.TotalSeconds >= 30);
    }

    [Fact]
    public void Media_DefaultStatusIsUnknown()
    {
        var media = new Media { FilePath = "/path/to/media.mp4", Title = "Media" };

        Assert.Equal(MediaStatus.Unknown, media.Status);
    }

    [Fact]
    public void Media_DateAddedIsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var media = new Media { FilePath = "/path/to/media.mp4", Title = "Media" };
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.InRange(media.DateAdded, before, after);
    }

    [Fact]
    public void Media_DefaultStringFieldsAreInitialized()
    {
        var media = new Media { FilePath = "/path/to/media.mp4", Title = "Media" };

        Assert.Equal(string.Empty, media.Artist);
        Assert.Equal(string.Empty, media.Format);
        Assert.Equal("", media.Notes);
    }

    [Fact]
    public void Media_CanBeInstantiated()
    {
        var media = new Media { FilePath = "/path/to/media.mp4", Title = "Media" };

        Assert.NotNull(media);
    }

    [Fact]
    public void Media_TwoInstancesHaveDistinctIds()
    {
        var a = new Media { FilePath = "/path/a.mp4", Title = "A" };
        var b = new Media { FilePath = "/path/b.mp4", Title = "B" };

        Assert.NotEqual(a.Id, b.Id);
    }
}
