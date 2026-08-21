using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Interactions.Handlers;

public class EditMediaDialogHandler : IInteractionHandler<EditMediaRequest, Media?>
{
    private readonly IDialogService _dialogService;

    public EditMediaDialogHandler(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public Task<Media?> HandleAsync(EditMediaRequest request, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<Media?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _ = _dialogService.RequestEditAsync(
            request.Media,
            onSave: media => { tcs.TrySetResult(media); return Task.CompletedTask; },
            onCancel: () => tcs.TrySetResult(null));

        return tcs.Task;
    }
}
