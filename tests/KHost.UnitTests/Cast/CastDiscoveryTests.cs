using KHost.Cast;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Cast;

/// <summary>Discovery state, without touching the network.</summary>
public class CastDiscoveryTests : IDisposable
{
    private readonly CastService _cast = new(
        NullLogger<CastService>.Instance,
        Options.Create(new CastService.ServiceOptions()));

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
    public async Task StopDiscovery_RaisesStateChanged_SoThePageRedraws()
    {
        var raised = 0;
        _cast.StateChanged += (_, _) => raised++;

        await _cast.StopDiscoveryAsync();

        Assert.Equal(1, raised);
    }

    public void Dispose()
    {
        _cast.Dispose();
        GC.SuppressFinalize(this);
    }
}
