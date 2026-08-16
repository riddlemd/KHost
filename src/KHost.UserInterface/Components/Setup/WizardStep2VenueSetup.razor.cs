using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Components.Setup;

public partial class WizardStep2VenueSetup
{
    [Inject] private IVenuesService? VenuesService { get; set; }

    [Parameter]
    public EventCallback OnComplete { get; set; }

    private SetupVenueModel _model = new();
    private EditContext _editContext = default!;
    private string _serverError = string.Empty;
    private bool _isLoading = false;

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    private async Task OnNextAsync()
    {
        _serverError = string.Empty;
        _isLoading = true;

        try
        {
            var venue = new Venue
            {
                Name = _model.Name,
                Enabled = true,
                Settings = new()
                {
                    DefaultVolume = _model.DefaultVolume,
                    PromptBeforeRemovingSinger = _model.PromptBeforeRemovingSinger,
                    PromptBeforeRemovingPerformance = _model.PromptBeforeRemovingPerformance,
                    ClearQueueOnClose = _model.ClearQueueOnClose,
                }
            };
            var createdVenue = await VenuesService!.CreateAsync(venue);
            await VenuesService.SelectVenueAsync(createdVenue.Id);
            await OnComplete.InvokeAsync();
        }
        catch (Exception ex)
        {
            _serverError = $"Failed to create venue: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }
}
