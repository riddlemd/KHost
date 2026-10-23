using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The picture question is answered by whichever probe owns the file.</summary>
public class MediaSourcePictureProbeTests
{
    private readonly IMediaProbeService _probes = Substitute.For<IMediaProbeService>();

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public async Task HasMovingPicture_IsWhatTheOwningProbeSaid_NoAnswerReadingAsNone(bool? said, bool expected)
    {
        _probes.ProbeAsync("/songs/a.kit", Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { HasMovingPicture = said });

        Assert.Equal(expected, await new MediaSourcePictureProbe(_probes).HasMovingPictureAsync("/songs/a.kit"));
    }

    [Fact]
    public async Task HasMovingPicture_AFileNobodyCouldRead_IsFalse()
    {
        _probes.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MediaProbeResult?)null);

        Assert.False(await new MediaSourcePictureProbe(_probes).HasMovingPictureAsync("/songs/a.mp4"));
    }
}
