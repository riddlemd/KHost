using KHost.Abstractions.Exceptions;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components;

/// <summary>
/// Catches what call sites do not. A KHostException already carries a host-readable explanation, so
/// it becomes a dialog and the page carries on; anything else is a bug rather than a situation, and
/// falls through to the default error UI instead of being dressed up as an expected failure.
/// </summary>
public sealed class KHostErrorBoundary : ErrorBoundary
{
    [Inject] private IDialogService? DialogService { get; set; }

    protected override async Task OnErrorAsync(Exception exception)
    {
        if (exception is not KHostException error)
        {
            await base.OnErrorAsync(exception);
            return;
        }

        await DialogService!.ShowErrorAsync(error);

        // Without this the boundary stays tripped and swaps the whole page for its error content,
        // which is the wrong outcome when the failure has already been explained in a dialog.
        Recover();
    }
}
