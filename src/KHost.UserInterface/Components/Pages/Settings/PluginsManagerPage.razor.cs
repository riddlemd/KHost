using System.Text.Json;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class PluginsManagerPage : IDisposable
{
    [Inject] private IPluginsService? PluginsService { get; set; }

    private readonly string _pluginsDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
    private HashSet<string> _enabledIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SettingField>> _settingFields = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        if (PluginsService is null) return;

        PluginsService.StateChanged += OnStateChanged;

        _enabledIds = (await PluginsService.ReadEnabledIdsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in PluginsService.Plugins.Where(p => p.Manifest?.Settings.Count > 0))
        {
            var stored = await PluginsService.ReadSettingsAsync(plugin.Id);

            _settingFields[plugin.Id] = plugin.Manifest!.Settings.Select(definition =>
            {
                stored.TryGetValue(definition.Key, out var storedValue);
                var element = storedValue.ValueKind != JsonValueKind.Undefined ? storedValue : definition.Default;

                return new SettingField
                {
                    Definition = definition,
                    Text = element?.ValueKind is JsonValueKind.String ? element.Value.GetString() : element?.ToString(),
                    Flag = element?.ValueKind is JsonValueKind.True,
                };
            }).ToList();
        }
    }

    private async Task ToggleAsync(string pluginId, bool enabled)
    {
        if (PluginsService is null) return;

        await PluginsService.SetEnabledAsync(pluginId, enabled);

        if (enabled) _enabledIds.Add(pluginId);
        else _enabledIds.Remove(pluginId);
    }

    private async Task SaveSettingsAsync(string pluginId)
    {
        if (PluginsService is null || !_settingFields.TryGetValue(pluginId, out var fields)) return;

        var values = new Dictionary<string, JsonElement>();

        foreach (var field in fields)
        {
            switch (field.Definition.Type)
            {
                case PluginSettingType.Bool:
                    values[field.Definition.Key] = JsonSerializer.SerializeToElement(field.Flag);
                    break;
                case PluginSettingType.Int:
                    // Unparseable input is omitted so the plugin falls back to its manifest default.
                    if (int.TryParse(field.Text, out var number))
                        values[field.Definition.Key] = JsonSerializer.SerializeToElement(number);
                    break;
                default:
                    if (!string.IsNullOrEmpty(field.Text))
                        values[field.Definition.Key] = JsonSerializer.SerializeToElement(field.Text);
                    break;
            }
        }

        await PluginsService.SaveSettingsAsync(pluginId, values);
    }

    private static string GetInputType(PluginSettingDefinition definition) => definition switch
    {
        { Secret: true } => "password",
        { Type: PluginSettingType.Int } => "number",
        _ => "text",
    };

    private static string GetStatusLabel(PluginStatus status) => status switch
    {
        PluginStatus.Loaded or PluginStatus.Enabled => "Active",
        PluginStatus.Disabled => "Disabled",
        PluginStatus.Incompatible => "Incompatible",
        PluginStatus.Errored => "Error",
        _ => status.ToString(),
    };

    private static string GetStatusBadgeClass(PluginStatus status) => status switch
    {
        PluginStatus.Loaded or PluginStatus.Enabled => "kh-badge--success",
        PluginStatus.Disabled => "kh-badge--secondary",
        PluginStatus.Incompatible => "kh-badge--info",
        PluginStatus.Errored => "kh-badge--danger",
        _ => "kh-badge--secondary",
    };

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (PluginsService is not null)
            PluginsService.StateChanged -= OnStateChanged;
    }

    private class SettingField
    {
        public required PluginSettingDefinition Definition { get; init; }
        public string? Text { get; set; }
        public bool Flag { get; set; }
    }
}
