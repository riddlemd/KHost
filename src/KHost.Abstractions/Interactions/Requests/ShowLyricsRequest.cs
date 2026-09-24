namespace KHost.Abstractions.Interactions.Requests;

/// <summary>Asks the host to show lyrics for a song.</summary>
/// <param name="Query">Title and artist (or however the caller wants the lookup phrased).</param>
public sealed record ShowLyricsRequest(string Query) : IInteractionRequest;
