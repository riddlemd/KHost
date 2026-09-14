namespace KHost.Domain.Services.Screens;

/// <summary>
/// A singleton that has to exist before the host serves anything, because it wires itself up in
/// its constructor and does nothing at all until it is built.
/// </summary>
/// <remarks>
/// <para>
/// The container builds a singleton the first time somebody asks for it. A service that subscribes
/// to the message broker or to a screen server event in its constructor has therefore subscribed
/// to nothing until that moment — and a screen connecting before then is answered by nobody, with
/// no error anywhere to say so. That has been found three times: the marquee, the break music
/// card, and the codes.
/// </para>
/// <para>
/// Carrying no members is the point. This says "build me", not "do something": the work each of
/// these does on the way up is its own, and several already have an InitializeAsync for it. What
/// was missing was never the work — it was being constructed at all.
/// </para>
/// <para>
/// Being reachable through somebody else's constructor does not count and is not relied on.
/// PlaybackService was alive only because ScreenMarqueeService takes it, which is the marquee
/// needing playback for its own reasons; removing that parameter would have stopped screens being
/// answered, silently.
/// </para>
/// </remarks>
public interface IStartsWithTheHost;
