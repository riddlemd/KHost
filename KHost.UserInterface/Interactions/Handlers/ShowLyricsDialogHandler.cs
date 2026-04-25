using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Interactions.Handlers;

public class ShowLyricsDialogHandler : IInteractionHandler<ShowLyricsRequest>
{
    private readonly IDialogService _dialogService;

    public ShowLyricsDialogHandler(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public Task HandleAsync(ShowLyricsRequest request, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _ = _dialogService.ShowLyricsAsync(request.Query, onClose: () => tcs.TrySetResult());

        return tcs.Task;
    }
}
