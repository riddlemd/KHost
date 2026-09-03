namespace KHost.Abstractions.Interactions.Requests;

/// <summary>One field of a <see cref="TextPromptRequest"/>. Secret masks the input; it is not
/// stored anywhere the request or its response is not already going.</summary>
public sealed record TextPromptField(string Key, string Label, bool Secret = false);

/// <summary>
/// Asks the host to type in one or more values the requester will not persist — a plugin's own
/// login for a session it will hold only in memory, say. Unlike a plugin setting, nothing here
/// ever reaches <c>plugins.json</c> or any other stored settings; the dialog is the only place
/// these values exist outside the caller's own variables, for exactly as long as it takes to
/// answer the request. A null response means the host cancelled.
/// </summary>
public sealed record TextPromptRequest(
    string Title,
    string? Message,
    IReadOnlyList<TextPromptField> Fields) : IInteractionRequest<IReadOnlyDictionary<string, string>?>;
