using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Plugins;

/// <inheritdoc />
public sealed class PluginButtonService : IPluginButtonService
{
    private readonly IReadOnlyDictionary<string, IPluginButtonHandler> _handlers;
    private readonly IPluginRegistry _registry;

    public PluginButtonService(IEnumerable<PluginButtonBinding> bindings, IPluginRegistry registry)
    {
        // A plugin implements the handler once, so one binding per id; group defensively rather
        // than throw if a plugin somehow registered two.
        _handlers = bindings
            .GroupBy(binding => binding.PluginId)
            .ToDictionary(group => group.Key, group => group.First().Handler);
        _registry = registry;
    }

    public IReadOnlyList<(PluginButtonDefinition Definition, PluginButtonState State)> ButtonsFor(string pluginId)
    {
        if (!_handlers.TryGetValue(pluginId, out var handler))
            return [];

        var manifest = _registry.Plugins.FirstOrDefault(plugin => plugin.Id == pluginId)?.Manifest;
        if (manifest is null)
            return [];

        return
        [
            .. manifest.Buttons
                .Select(button => (button, State: Describe(handler, button.Key)))
                .Where(pair => pair.State.Visible)
        ];
    }

    public Task InvokeAsync(string pluginId, string key, CancellationToken cancellationToken = default)
        => _handlers.TryGetValue(pluginId, out var handler)
            ? handler.InvokeButtonAsync(key, cancellationToken)
            : Task.CompletedTask;

    // A plugin's own state check; a throwing one must not take the whole row's buttons down.
    private static PluginButtonState Describe(IPluginButtonHandler handler, string key)
    {
        try { return handler.DescribeButton(key); }
        catch { return PluginButtonState.Default; }
    }
}
