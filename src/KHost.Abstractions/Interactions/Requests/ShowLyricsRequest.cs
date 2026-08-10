namespace KHost.Abstractions.Interactions.Requests;

public sealed record ShowLyricsRequest(string Query) : IInteractionRequest;
