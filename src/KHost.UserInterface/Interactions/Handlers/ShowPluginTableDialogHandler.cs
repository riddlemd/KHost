using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Interactions.Handlers;

/// <summary>Opens the dialog and waits for it to close, so the plugin's button stays busy
/// while the host is looking at the table.</summary>
public class ShowPluginTableDialogHandler : IInteractionHandler<ShowPluginTableRequest>
{
    private readonly IDialogService _dialogService;

    public ShowPluginTableDialogHandler(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public Task HandleAsync(ShowPluginTableRequest request, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        InteractionCompletion.LinkCancellation(tcs, cancellationToken);

        _ = _dialogService.ShowPluginTableAsync(request, onClose: () => tcs.TrySetResult());

        return tcs.Task;
    }
}
