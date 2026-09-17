namespace KHost.Abstractions.Interactions.Requests;

/// <summary>One field of a <see cref="TextPromptRequest"/>; Secret masks the input.</summary>
public sealed record TextPromptField(string Key, string Label, bool Secret = false);

/// <summary>Asks the host to type values with no setting; a keeper puts them in the secret store.</summary>
public sealed record TextPromptRequest(
    string Title,
    string? Message,
    IReadOnlyList<TextPromptField> Fields) : IInteractionRequest<IReadOnlyDictionary<string, string>?>;
