namespace KHost.Abstractions.Messaging.Messages;

/// <summary>The next-singer card is no longer up; a display still showing it shows what it shows
/// between songs instead.</summary>
/// <remarks>Follows a <see cref="NextSingerAnnounced"/>. Published and awaited when a host takes the
/// card down, so the host's button settles once every display has heard it. Also announced when the
/// program moved or the selected venue changed while nothing was on, which a display draws over the
/// card anyway: a display hearing it during a song or an ad leaves that alone.</remarks>
public sealed record NextSingerWithdrawn;
