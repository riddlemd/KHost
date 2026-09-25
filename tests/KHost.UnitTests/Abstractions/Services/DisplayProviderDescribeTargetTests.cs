using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Displays;

namespace KHost.UnitTests.Abstractions.Services;

/// <summary>The default body is what every provider that predates it answers, so it must be the
/// answer the host used to work out for itself.</summary>
public class DisplayProviderDescribeTargetTests
{
    public static TheoryData<string?, DisplayDevice[]> Displays() => new()
    {
        { "tv", [Device("tv", mixes: true)] },
        { "tv", [Device("tv", mixes: false)] },
        { "tv", [Device("other", mixes: true), Device("tv", mixes: false)] },

        // Listed under another id: the connected row answers, as it always did.
        { "session-7", [Device("other", mixes: false), Device("tv", mixes: true, connected: true)] },
        { "session-7", [Device("tv", mixes: false, connected: true)] },

        // Connected before it lists anything.
        { "tv", [] },
    };

    [Theory]
    [MemberData(nameof(Displays))]
    public void DefaultBody_AnswersWhatTheHostUsedToWorkOut(string? connectedId, DisplayDevice[] devices)
    {
        IDisplayProvider provider = new BareDisplay(connectedId, devices);

        var target = provider.DescribeTarget();

        var used = ConnectedDisplay.Find([provider]) is { Device.SupportsStemMix: true };
        Assert.Equal(used, target.MixesStems);
        Assert.False(target.BurnLyrics);
    }

    [Fact]
    public void DefaultBody_ADeviceThatMixes_AsksForTheStems()
    {
        IDisplayProvider provider = new BareDisplay("tv", [Device("tv", mixes: true)]);

        Assert.True(provider.DescribeTarget().MixesStems);
    }

    [Fact]
    public void DefaultBody_ADeviceThatCannotMix_AsksForNothingSpecial()
    {
        IDisplayProvider provider = new BareDisplay("tv", [Device("tv", mixes: false)]);

        var target = provider.DescribeTarget();

        Assert.False(target.MixesStems || target.BurnLyrics);
    }

    private static DisplayDevice Device(string id, bool mixes, bool connected = false)
        => new() { Id = id, Name = id, IsConnected = connected, SupportsStemMix = mixes };

    /// <summary>Implements only what an interface without default bodies would demand.</summary>
    private sealed class BareDisplay(string? connectedId, IReadOnlyList<DisplayDevice> devices) : IDisplayProvider
    {
        public event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged { add { } remove { } }

        public string Name => "Bare";
        public bool IsDiscovering => false;
        public IReadOnlyList<DisplayDevice> Devices => devices;
        public string? ConnectedDeviceId => connectedId;
        public Guid? SessionId => null;

        public Task StartDiscoveryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopDiscoveryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(string streamUrl, TimeSpan startOffset, int tempo = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
