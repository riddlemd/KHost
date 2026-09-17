using KHost.Abstractions.Exceptions;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Components;

/// <summary>Shows a dialog instead of swapping the page, which would lose the queue mid-show.</summary>
public sealed class KHostErrorBoundary : ErrorBoundary
{
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private ILogger<KHostErrorBoundary>? Logger { get; set; }

    protected override async Task OnErrorAsync(Exception exception)
    {
        // A KHostException was thrown by someone who knew what it meant; anything else is a bug,
        // and the host still has to be told something they can act on and quote.
        var presentable = exception as KHostException ?? Unexpected(exception);

        // The base class logs; this one does not call it, so the detail would otherwise be lost.
        Logger?.LogError(exception, "Error boundary caught {ExceptionType}", exception.GetType().Name);

        await DialogService!.ShowErrorAsync(presentable);

        // Without this the boundary stays tripped and swaps the whole page for its error content,
        // which is the wrong outcome when the failure has already been explained in a dialog.
        Recover();
    }

    private static KHostException Unexpected(Exception exception)
        => new("KHost hit a problem it did not expect and stopped what it was doing.",
               "Whatever was running keeps running. If this repeats, the log has the detail.",
               $"KH-UNEXPECTED-{exception.GetType().Name}",
               exception);
}
