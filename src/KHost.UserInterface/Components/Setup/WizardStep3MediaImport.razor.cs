using Microsoft.AspNetCore.Components;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Components.Setup;

public partial class WizardStep3MediaImport
{
    [Inject] private IMediaService MediaService { get; set; } = default!;

    [Parameter]
    public EventCallback OnComplete { get; set; }

    private bool _isLoading = false;

    private async Task OnImportCompletedAsync()
    {
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnCompleteAsync()
    {
        _isLoading = true;
        try
        {
            await OnComplete.InvokeAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }
}
