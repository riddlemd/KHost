namespace KHost.Abstractions.Interactions.Requests;

/// <summary>One field of a <see cref="TextPromptRequest"/>. Secret masks the input; it is not
/// stored anywhere the request or its response is not already going.</summary>
public sealed record TextPromptField(string Key, string Label, bool Secret = false);

/// <summary>
/// Asks the host to type in one or more values the caller has no setting for — a plugin's own
/// login, say. What the caller then does with them is its own: nothing here reaches
/// <c>plugins.json</c> or any other stored settings, so a caller that keeps what it collected puts
/// it in the secret store rather than leaving it where a settings file would carry it. A null
/// response means the host cancelled.
/// </summary>
public sealed record TextPromptRequest(
    string Title,
    string? Message,
    IReadOnlyList<TextPromptField> Fields) : IInteractionRequest<IReadOnlyDictionary<string, string>?>;
