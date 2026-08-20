using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class AppSettingsPage
{
    [Inject] private IAppSettingsService? AppSettings { get; set; }

    private AppSettings _model = new();
    private bool _restartRequired;
    private bool _saving;
    private string? _error;

    protected override void OnInitialized()
    {
        if (AppSettings is null) return;

        _model = AppSettings.Current;
        _restartRequired = AppSettings.RestartRequired;
    }

    private async Task SaveAsync()
    {
        if (AppSettings is null) return;

        _saving = true;
        _error = null;

        var result = await AppSettings.SaveAsync(_model);

        if (!result.Saved)
        {
            _error = result.Error;
            // The refused toggle must not keep looking flipped.
            _model = AppSettings.Current;
        }

        _restartRequired = AppSettings.RestartRequired;
        _saving = false;
    }
}
