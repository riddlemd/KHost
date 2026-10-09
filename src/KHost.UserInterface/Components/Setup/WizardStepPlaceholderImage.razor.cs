using KHost.Abstractions.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Setup;

/// <summary>Picks the image new venues show while nothing plays, and how it fills the screen, before
/// the first venue is made, so that venue starts with them too.</summary>
public partial class WizardStepPlaceholderImage
{
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;

    [Parameter] public EventCallback OnComplete { get; set; }

    private Guid? _mediaId;
    private ImageScaling? _scaling;
    private bool _isSaving;
    private string _serverError = string.Empty;

    protected override void OnInitialized()
    {
        var current = AppSettings.Current;
        _mediaId = current.NewVenuePlaceholderImageId;
        _scaling = current.NewVenuePlaceholderImageScaling;
    }

    private async Task OnNextAsync()
    {
        _serverError = string.Empty;
        _isSaving = true;

        try
        {
            // A scaling with no image to scale would only surprise the next venue that gets one.
            await AppSettings.SaveNewVenuePlaceholderImageAsync(_mediaId, _mediaId is null ? null : _scaling);
            await OnComplete.InvokeAsync();
        }
        catch (IOException ex)
        {
            _serverError = $"Could not save the choice: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
}
