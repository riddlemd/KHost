using KHost.Domain.Services;
using KHost.Domain.Services.VideoEncoding;
using KHost.UserInterface.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.UserInterface.Startup;

/// <summary>The hardware encoder probe runs at startup, so the first song is not the one that waits on it.</summary>
public class HostInitializationVideoEncoderTests
{
    private readonly IVideoEncoderSelector _selector = Substitute.For<IVideoEncoderSelector>();

    [Fact]
    public async Task WarmVideoEncoderProbe_AtStartup_ProbesUnderTheHostsSetting()
    {
        var warmed = new TaskCompletionSource<VideoEncoderPreference>();
        _selector.WarmAsync(Arg.Any<VideoEncoderPreference>())
            .Returns(call =>
            {
                warmed.TrySetResult(call.Arg<VideoEncoderPreference>());
                return Task.CompletedTask;
            });

        HostInitialization.WarmVideoEncoderProbe(Services(VideoEncoderPreference.Hardware));

        Assert.Equal(VideoEncoderPreference.Hardware, await warmed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Not awaited: a probe that hangs on a driver must not hold startup with it.</summary>
    [Fact]
    public async Task WarmVideoEncoderProbe_AProbeThatNeverEnds_ReturnsAtOnce()
    {
        _selector.WarmAsync(Arg.Any<VideoEncoderPreference>()).Returns(new TaskCompletionSource().Task);

        var startup = Task.Run(() => HostInitialization.WarmVideoEncoderProbe(Services(VideoEncoderPreference.Auto)));

        Assert.Same(startup, await Task.WhenAny(startup, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    private IServiceProvider Services(VideoEncoderPreference preference)
        => new ServiceCollection()
            .AddSingleton(_selector)
            .AddSingleton<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>(
                new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(
                    new HlsMediaStreamService.ServiceOptions { Encoder = preference }))
            .BuildServiceProvider();
}
