using KHost.Domain.Services.VideoEncoding;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>Intel Quick Sync (h264_qsv).</summary>
public sealed class HlsMediaStreamServiceQuickSyncTests()
    : HlsMediaStreamServiceHardwareEncodeTests(VideoEncoderProfile.QuickSync);
