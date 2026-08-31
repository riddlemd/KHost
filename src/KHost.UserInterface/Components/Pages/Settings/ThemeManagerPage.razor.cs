using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Messaging;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class ThemeManagerPage : IDisposable
{
    [Inject] private IThemeService? ThemeService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private IReadOnlyList<ThemeDefinition> _themes = [];

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<ThemesChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<ThemeChanged>(_ => OnStateChanged()));

        Load();
    }

    private void Load() => _themes = ThemeService?.AllThemes ?? [];

    private void OnStateChanged()
    {
        Load();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Mirrors the service's own refusals so the row explains itself rather than looking broken:
    /// the theme on screen has to stay reachable, and one has to be left to switch to.
    /// </summary>
    private bool CanToggle(ThemeDefinition theme, bool isCurrent)
    {
        if (!theme.IsEnabled)
            return true;

        return !isCurrent && _themes.Count(t => t.IsEnabled) > 1;
    }

    private string DisableHint(ThemeDefinition theme, bool isCurrent)
    {
        if (!theme.IsEnabled)
            return $"Enable {theme.Name}";

        if (isCurrent)
            return "Switch to another theme before disabling this one";

        return _themes.Count(t => t.IsEnabled) > 1
            ? $"Disable {theme.Name}"
            : "At least one theme has to stay enabled";
    }

    private async Task ToggleAsync(ThemeDefinition theme, bool enabled)
    {
        if (ThemeService is null) return;

        await ThemeService.SetEnabledAsync(theme.Id, enabled);
    }

    private async Task OpenAddDialogAsync()
    {
        if (DialogService is null) return;

        await DialogService.RequestEditAsync(null, async theme => await SaveAsync(theme));
    }

    private async Task OpenEditDialogAsync(ThemeDefinition theme)
    {
        if (DialogService is null) return;

        await DialogService.RequestEditAsync(theme, async updated => await SaveAsync(updated));
    }

    private async Task SaveAsync(ThemeDefinition? theme)
    {
        if (ThemeService is null || theme is null) return;

        if (string.IsNullOrEmpty(theme.Id))
            theme.Id = ThemeService.BuildId(theme.Name);

        await ThemeService.SaveAsync(theme);
    }

    private async Task CloneAsync(ThemeDefinition theme)
    {
        if (ThemeService is null) return;

        await ThemeService.CloneAsync(theme.Id);
    }

    private async Task StartDeleteAsync(ThemeDefinition theme)
    {
        if (ThemeService is null || DialogService is null) return;

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{theme.Name}</span>?",
            async () => await ThemeService.DeleteAsync(theme.Id),
            "Delete Theme",
            "Delete"
        );
    }

    public void Dispose() => _subscriptions.Dispose();
}
