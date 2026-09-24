namespace KHost.Abstractions.Interactions.Requests;

/// <summary>Asks the host to confirm queueing a song already queued or recently sung.</summary>
/// <param name="MediaTitle">The song's title, for the confirmation dialog.</param>
/// <param name="TimesAlreadyQueued">How many times it is already waiting in the queue.</param>
/// <param name="SungWithinHours">
/// How many hours ago it was last sung within the venue's repeat window, or null when it was not
/// performed inside that window.
/// </param>
public sealed record ConfirmDuplicateSongRequest(
    string MediaTitle,
    int TimesAlreadyQueued,
    int? SungWithinHours) : IInteractionRequest<bool>;
