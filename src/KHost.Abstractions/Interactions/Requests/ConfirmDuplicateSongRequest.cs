namespace KHost.Abstractions.Interactions.Requests;

/// <summary>SungWithinHours is null when the song was not performed inside the venue's window.</summary>
public sealed record ConfirmDuplicateSongRequest(
    string MediaTitle,
    int TimesAlreadyQueued,
    int? SungWithinHours) : IInteractionRequest<bool>;
