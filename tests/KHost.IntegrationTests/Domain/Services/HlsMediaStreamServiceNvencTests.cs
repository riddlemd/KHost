using KHost.Domain.Services.VideoEncoding;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>NVIDIA NVENC (h264_nvenc).</summary>
public sealed class HlsMediaStreamServiceNvencTests()
    : HlsMediaStreamServiceHardwareEncodeTests(VideoEncoderProfile.Nvenc);
