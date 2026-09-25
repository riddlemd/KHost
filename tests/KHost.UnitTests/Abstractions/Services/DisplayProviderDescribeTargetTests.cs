using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UnitTests.Abstractions.Services;

/// <summary>The default bodies are what a provider that implements only transport answers, so they
/// must never commit it to something it did not ask for.</summary>
public class DisplayProviderDescribeTargetTests
{
    /// <summary>Stems a provider never asked for would reach it with nothing encoded to play.</summary>
    [Fact]
    public void DefaultDescribeTarget_AsksForNothingSpecial()
    {
        IDisplayProvider provider = new BareDisplay();

        var target = provider.DescribeTarget();

        Assert.False(target.MixesStems);
        Assert.False(target.BurnLyrics);
    }

    /// <summary>False is what makes the host rebuild the stream, so a provider that cannot ride a
    /// level still hears the new mix.</summary>
    [Fact]
    public async Task DefaultSetStemVolume_Refuses()
    {
        IDisplayProvider provider = new BareDisplay();

        Assert.False(await provider.SetStemVolumeAsync(new StemLevel { Role = AudioTrackRole.Lead, Volume = 40 }));
    }

    /// <summary>Implements only what an interface without default bodies would demand.</summary>
    private sealed class BareDisplay : IDisplayProvider
    {
        public event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged { add { } remove { } }

        public string Name => "Bare";
        public bool IsDiscovering => false;
        public IReadOnlyList<DisplayDevice> Devices => [new DisplayDevice { Id = "tv", Name = "TV", IsConnected = true }];
        public string? ConnectedDeviceId => "tv";
        public Guid? SessionId => null;

        public Task StartDiscoveryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopDiscoveryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(DisplayLoad load, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
