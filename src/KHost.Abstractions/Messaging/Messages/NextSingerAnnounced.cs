using KHost.Abstractions.Models;

namespace KHost.Abstractions.Messaging.Messages;

/// <summary>A host asked for the room to be told who sings next; draw <paramref name="Card"/>.</summary>
/// <param name="Card">Who is up, composed whole; draw it as given.</param>
/// <remarks>Carries the card, unlike a change message: it is a one-shot request with nothing to read
/// back afterwards, so a display that was not connected when it was published never draws it. The
/// card stands until the display draws something else over it. Published and awaited: the host's
/// button settles once every display has been handed the card.</remarks>
public sealed record NextSingerAnnounced(NextSingerCard Card);
