using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UnitTests.Domain.Services.Plugins;

/// <summary>Implements multiple extension interfaces to prove they resolve to one instance.</summary>
public sealed class SharedInstanceExtensionDouble : IMediaProvider, IPluginButtonHandler, IMediaPlaybackGate
{
    // ActivatorUtilities constructs the extension with a PluginContext argument, so a parameterless
    // constructor would leave it unmatched.
    public SharedInstanceExtensionDouble(IPluginContext context) => _ = context;

    public string DisplayName => "Double";
    public string SourceName => "Double";
    public IEnumerable<MediaProviderAction> Actions => [];

    public Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
        => Task.FromResult(new List<MediaSearchEntity>());

    public Task InvokeButtonAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string ProviderId => "double";
    public Task<PlaybackGateResult> CanAsync(MediaAction action, Media media, CancellationToken cancellationToken = default)
        => Task.FromResult(PlaybackGateResult.Ok);
}
