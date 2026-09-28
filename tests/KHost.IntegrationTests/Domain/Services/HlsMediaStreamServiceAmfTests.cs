using KHost.Domain.Services.VideoEncoding;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>AMD AMF (h264_amf).</summary>
public sealed class HlsMediaStreamServiceAmfTests()
    : HlsMediaStreamServiceHardwareEncodeTests(VideoEncoderProfile.Amf);
