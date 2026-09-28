using KHost.Domain.Services.VideoEncoding;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>Apple VideoToolbox (h264_videotoolbox).</summary>
public sealed class HlsMediaStreamServiceVideoToolboxTests()
    : HlsMediaStreamServiceHardwareEncodeTests(VideoEncoderProfile.VideoToolbox);
