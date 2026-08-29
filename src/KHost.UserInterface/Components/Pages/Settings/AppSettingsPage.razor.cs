using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;


namespace KHost.UserInterface.Components.Pages.Settings;

public partial class AppSettingsPage : IDisposable
{
    [Inject] private IAppSettingsService? AppSettings { get; set; }
    [Inject] private IFlashService? Flash { get; set; }
    [Inject] private IDialogService? Dialog { get; set; }
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private IDisposable? _navigationGuard;

    // Set while leaving on the host's answer, so the guard does not stop the navigation it asked for.
    private bool _leaving;

    private AppSettings _model = new();
    private bool _restartRequired;
    private bool _saving;
    private string? _error;
    private string? _defaultMediaDirectory;

    protected override void OnInitialized()
    {
        if (AppSettings is null) return;

        _model = AppSettings.Current;
        _restartRequired = AppSettings.RestartRequired;
        _defaultMediaDirectory = AppSettings.DefaultMediaDirectory;

        // Registered here rather than on first render: a navigation can be asked for before the
        // page has painted, and an unguarded one loses the edits without a word.
        _navigationGuard = Navigation.RegisterLocationChangingHandler(OnLocationChangingAsync);
    }

    /// <summary>
    /// Compares against what is stored rather than tracking each field: AppSettings is a record,
    /// so a page that edits a field and puts it back is correctly not dirty.
    /// </summary>
    private bool HasUnsavedChanges => AppSettings is not null && _model != AppSettings.Current;

    private async ValueTask OnLocationChangingAsync(LocationChangingContext context)
    {
        if (_leaving || !HasUnsavedChanges || Dialog is null) return;

        // Held rather than cancelled: the host has not chosen yet, and the target has to survive
        // long enough to be navigated to once they do.
        context.PreventNavigation();

        var target = context.TargetLocation;

        await Dialog.ShowUnsavedChangesAsync(
            onSave: async () =>
            {
                await SaveAsync();

                // A refused save keeps the host here with the reason on screen, rather than
                // carrying them away from settings that did not take.
                if (_error is null) LeaveTo(target);
            },
            onDiscard: () =>
            {
                LeaveTo(target);
                return Task.CompletedTask;
            });
    }

    private void LeaveTo(string target)
    {
        _leaving = true;
        Navigation.NavigateTo(target);
    }

    public void Dispose() => _navigationGuard?.Dispose();

    private async Task SaveAsync()
    {
        if (AppSettings is null) return;

        _saving = true;
        _error = null;

        var result = await AppSettings.SaveAsync(_model);

        if (result.Saved)
        {
            Flash?.Show("App settings saved.");
        }
        else
        {
            _error = result.Error;
            // The refused toggle must not keep looking flipped.
            _model = AppSettings.Current;
        }

        _restartRequired = AppSettings.RestartRequired;
        _saving = false;
    }
}
