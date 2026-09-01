using KHost.Cast;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Cast;

/// <summary>Discovery state, without touching the network.</summary>
public class CastDiscoveryTests : IDisposable
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private readonly CastService _cast;

    public CastDiscoveryTests()
    {
        _cast = new CastService(
            NullLogger<CastService>.Instance,
            Options.Create(new CastService.ServiceOptions()),
            _broker);
    }

    [Fact]
    public void IsDiscovering_IsFalse_UntilAsked()
    {
        // Browsing sweeps the whole network, so nothing starts it on the app's behalf.
        Assert.False(_cast.IsDiscovering);
        Assert.Empty(_cast.Devices);
    }

    [Fact]
    public async Task StopDiscovery_IsANoOp_WhenItWasNeverStarted()
    {
        await _cast.StopDiscoveryAsync();

        Assert.False(_cast.IsDiscovering);
    }

    [Fact]
    public async Task StopDiscovery_AnnouncesCastChanged_SoThePageRedraws()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<CastChanged>(_ => raised++);

        await _cast.StopDiscoveryAsync();

        Assert.Equal(1, raised);
    }

    public void Dispose()
    {
        _cast.Dispose();
        GC.SuppressFinalize(this);
    }
}
