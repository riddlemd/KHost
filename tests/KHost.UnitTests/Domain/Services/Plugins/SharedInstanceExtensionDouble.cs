using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UnitTests.Domain.Services.Plugins;

/// <summary>
/// A public plugin extension type that is BOTH a media provider and a button handler, so a loader
/// test can prove the two interfaces resolve to one shared instance. Loaded by copying this test
/// assembly in as a plugin entry — nothing else in it implements a plugin extension interface.
/// </summary>
public sealed class SharedInstanceExtensionDouble : IMediaProvider, IPluginButtonHandler, IMediaPlaybackGate
{
    // The loader builds an extension with its PluginContext as an argument, so a constructor has
    // to accept it — a parameterless one leaves ActivatorUtilities with an unused argument and no
    // match.
    public SharedInstanceExtensionDouble(IPluginContext context) => _ = context;

    public string DisplayName => "Double";
    public string SourceName => "Double";
    public IEnumerable<MediaProviderAction> Actions => [];

    public Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
        => Task.FromResult(new List<MediaSearchEntity>());

    public Task InvokeButtonAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string GateKey => "double";
    public Task<PlaybackGateResult> CanPlayAsync(Media media, CancellationToken cancellationToken = default)
        => Task.FromResult(PlaybackGateResult.Ok);
}
