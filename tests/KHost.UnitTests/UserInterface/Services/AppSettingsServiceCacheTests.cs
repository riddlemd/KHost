using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;

namespace KHost.UnitTests.UserInterface.Services;

/// <summary>Current is read on renders, so it is parsed once per configuration reload, not per access.</summary>
public class AppSettingsServiceCacheTests
{
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IConfigurationRoot _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection([new("Playback:DynamicLeadIns", "false")])
        .Build();

    public AppSettingsServiceCacheTests()
    {
        // An unstubbed substitute provider answers "" for its name, not null: no provider is active.
        _breakMusic.ActiveProvider.Returns((IBreakMusicProvider?)null);
    }

    private AppSettingsService Service() => new(_configuration, _ffmpeg, _breakMusic, Path.GetTempPath());

    /// <summary>An in-memory write raises no reload, so it shows only whether Current re-reads.</summary>
    [Fact]
    public void Current_KeepsItsRead_UntilTheConfigurationReloads()
    {
        var service = Service();
        Assert.False(service.Current.DynamicLeadIns);

        _configuration["Playback:DynamicLeadIns"] = "true";

        Assert.False(service.Current.DynamicLeadIns);
    }

    [Fact]
    public void Current_ReadsAgain_OnceTheConfigurationReloads()
    {
        var service = Service();
        Assert.False(service.Current.DynamicLeadIns);

        _configuration["Playback:DynamicLeadIns"] = "true";
        _configuration.Reload();

        Assert.True(service.Current.DynamicLeadIns);
    }

    /// <summary>The page edits the copy it is handed; the next reader must not see an unsaved edit.</summary>
    [Fact]
    public void Current_HandsOutACopy()
    {
        var service = Service();

        service.Current.SegmentSeconds = 9;

        Assert.Equal(2, service.Current.SegmentSeconds);
    }

    /// <summary>The active provider moves without any configuration reload, so the fallback is not cached.</summary>
    [Fact]
    public void Current_FollowsTheActiveBreakMusicProvider_BetweenReloads()
    {
        var service = Service();
        Assert.Null(service.Current.BreakMusicProvider);

        var active = Substitute.For<IBreakMusicProvider>();
        active.SourceName.Returns("JukeboxProvider");
        _breakMusic.ActiveProvider.Returns(active);

        Assert.Equal("JukeboxProvider", service.Current.BreakMusicProvider);
    }
}
