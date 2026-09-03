using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Interactions.Handlers;

public class TextPromptDialogHandler : IInteractionHandler<TextPromptRequest, IReadOnlyDictionary<string, string>?>
{
    private readonly IDialogService _dialogService;

    public TextPromptDialogHandler(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public Task<IReadOnlyDictionary<string, string>?> HandleAsync(
        TextPromptRequest request, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<IReadOnlyDictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _ = _dialogService.RequestTextPromptAsync(
            request.Title,
            request.Message,
            request.Fields,
            onSubmit: values => { tcs.TrySetResult(values); return Task.CompletedTask; },
            onCancel: () => tcs.TrySetResult(null));

        return tcs.Task;
    }
}
