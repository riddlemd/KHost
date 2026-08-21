using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Interactions.Handlers;

public class ConfirmDuplicateSongHandler : IInteractionHandler<ConfirmDuplicateSongRequest, bool>
{
    private readonly IDialogService _dialogService;

    public ConfirmDuplicateSongHandler(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public Task<bool> HandleAsync(ConfirmDuplicateSongRequest request, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _ = _dialogService.ShowConfirmationAsync(
            BuildMessage(request),
            onConfirm: () => { tcs.TrySetResult(true); return Task.CompletedTask; },
            title: "Duplicate Song",
            confirmText: "Queue Anyway",
            onCancel: () => tcs.TrySetResult(false),
            onClose: () => tcs.TrySetResult(false));

        return tcs.Task;
    }

    private static string BuildMessage(ConfirmDuplicateSongRequest request)
    {
        var song = $"<span class=\"kh-emphasis\">{request.MediaTitle}</span>";

        var reasons = new List<string>();

        if (request.TimesAlreadyQueued == 1)
            reasons.Add("is already in the queue");
        else if (request.TimesAlreadyQueued > 1)
            reasons.Add($"is already in the queue {request.TimesAlreadyQueued} times");

        if (request.SungWithinHours is { } hours)
            reasons.Add(hours < 1 ? "was sung less than an hour ago" : $"was sung about {Pluralize(hours, "hour")} ago");

        return $"{song} {string.Join(" and ", reasons)}. Queue it anyway?";
    }

    private static string Pluralize(int count, string noun)
        => count == 1 ? $"{count} {noun}" : $"{count} {noun}s";
}
