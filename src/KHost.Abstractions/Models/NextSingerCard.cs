namespace KHost.Abstractions.Models;

/// <summary>Who sings next and what they will sing, as the room should be told.</summary>
/// <remarks>Handed to a display through
/// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/>. Never names the singer
/// at the microphone.</remarks>
public sealed record NextSingerCard
{
    /// <summary>The singer, as the venue allows them to be called.</summary>
    public required string Singer { get; init; }

    /// <summary>Their next song, or null when they have nothing queued; never blank.</summary>
    public string? Song { get; init; }

    /// <summary>The queued song's artist, or null where none is recorded; never blank.</summary>
    public string? Artist { get; init; }
}
