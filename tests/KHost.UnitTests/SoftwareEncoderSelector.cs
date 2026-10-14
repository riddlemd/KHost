using KHost.Abstractions.Models;
using KHost.Domain.Services.VideoEncoding;

namespace KHost.UnitTests;

/// <summary>Always libx264, with no probe: the encode under test, not the choice of encoder.</summary>
internal sealed class SoftwareEncoderSelector : IVideoEncoderSelector
{
    public Task WarmAsync(VideoEncoderPreference preference) => Task.CompletedTask;

    public Task<VideoEncoderProfile> SelectAsync(VideoEncoderPreference preference, CancellationToken cancellationToken = default)
        => Task.FromResult(VideoEncoderProfile.Software);

    public void ReportFailure(VideoEncoderProfile encoder) { }
}
