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


}
