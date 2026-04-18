using KHost.Abstractions.Models;
using KHost.Domain.Models;

namespace KHost.UnitTests.Domain.Models;

public class QueuedSongTests
{
    [Fact]
    public void QueuedSong_DefaultStatusIsPending()
    {
        var qs = new QueuedSong
        {
            Singer = new Singer { Name = "Bob" },
            Song = new Song { FilePath = "/path/song.mp4", Title = "Song" }
        };

        Assert.Equal(QueuedSongStatus.Pending, qs.Status);
    }

    [Fact]
    public void QueuedSong_FilePath_DelegatesToSong()
    {
        var qs = new QueuedSong
        {
            Singer = new Singer { Name = "Bob" },
            Song = new Song { FilePath = "/path/song.mp4", Title = "Song" }
        };

        Assert.Equal("/path/song.mp4", qs.FilePath);
    }

    [Fact]
    public void QueuedSong_InitializesWithNewGuidId()
    {
        var qs = new QueuedSong
        {
            Singer = new Singer { Name = "Bob" },
            Song = new Song { FilePath = "/path/song.mp4", Title = "Song" }
        };

        Assert.NotEqual(Guid.Empty, qs.Id);
    }
}
