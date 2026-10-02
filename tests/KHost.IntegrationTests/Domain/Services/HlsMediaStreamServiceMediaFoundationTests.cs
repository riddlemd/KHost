using KHost.Domain.Services.VideoEncoding;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>Windows Media Foundation (h264_mf).</summary>
public sealed class HlsMediaStreamServiceMediaFoundationTests()
    : HlsMediaStreamServiceHardwareEncodeTests(VideoEncoderProfile.MediaFoundation);
