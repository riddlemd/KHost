namespace KHost.Abstractions.Interactions.Requests;

/// <summary>One field of a <see cref="TextPromptRequest"/>.</summary>
/// <param name="Key">Identifies this field's answer in the returned dictionary.</param>
/// <param name="Label">What the host shows beside the input.</param>
/// <param name="Secret">True masks the input on screen. It says nothing about how the answer is
/// kept afterwards — a caller that wants that must store it itself rather than as a setting.</param>
public sealed record TextPromptField(string Key, string Label, bool Secret = false);

/// <summary>Asks the host to collect one or more typed values, with no place of its own to keep them.</summary>
/// <param name="Title">The dialog's heading.</param>
/// <param name="Message">Explanatory text shown above the fields, or null for none.</param>
/// <param name="Fields">The fields to collect, in display order.</param>
/// <remarks>Resolves to the answers keyed by <see cref="TextPromptField.Key"/>, or null if the host
/// cancelled. A caller that wants to keep an answer stores it itself; nothing here is persisted.</remarks>
public sealed record TextPromptRequest(
    string Title,
    string? Message,
    IReadOnlyList<TextPromptField> Fields) : IInteractionRequest<IReadOnlyDictionary<string, string>?>;
