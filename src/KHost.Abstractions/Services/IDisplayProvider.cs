using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>A transport to somewhere the song comes out.</summary>
/// <remarks>It finds such places, connects to one, hands it what to play and drives transport on
/// it. It does not decide what the show is — it is told. The screens reach the host through one of
/// these too; that one is core rather than a plugin. See <c>docs/display-provider.md</c> for the
/// shape's reasoning.
///
/// <para>An extension point: a plugin IMPLEMENTS it and the host discovers it, listing it as
/// "Display provider" on the Plugins page. The plugin's object is one singleton shared across every
/// extension interface it implements, and is called from any thread.</para>
///
/// <para><b>One display at a time, across the whole system.</b> Before connecting a device the host
/// disconnects every other provider, and the provider whose <see cref="ConnectedDeviceId"/> is set
/// is the one the song goes to. A provider refuses a second device rather than driving two.</para>
///
/// <para><b>The host drives transport; the provider owns presentation.</b> This interface is
/// transport and control only. Everything drawn on the device — the words, the marquee, cards,
/// pictures — and the device's volume are the provider's own business: it reads what it needs from
/// the host's services and messages and sends it to its device however that device takes it.</para>
///
/// <para><b>What a provider can read to draw with.</b> What is on the main channel is
/// <see cref="IPlaybackService.CurrentProgram"/>; who sings next is <see cref="IUpNextService"/>; the
/// venue's QR code is <see cref="IQrCodeOfferService"/>; the card naming who is up arrives as
/// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/>; the break music is
/// <see cref="IBreakMusicService"/>; a song's words are <see cref="ITimedLyricsService"/>; the venue's
/// marquee and overlay settings are on <see cref="IVenuesService"/>. Each names the message that says
/// it moved. <see cref="IPlaybackService"/> depends on every display provider, so a provider must not
/// take it in its constructor: resolve it on first use.</para>
///
/// <para>Announce <see cref="KHost.Abstractions.Messaging.Messages.DisplaysChanged"/> whenever the
/// device list, the connection or the <see cref="SessionId"/> moves. The host re-reads the
/// providers on it: a new session gets the running song handed to it again, and every provider
/// reporting no session while a song plays parks that song, paused, at its start.</para></remarks>
public interface IDisplayProvider
{
    /// <summary>Names the transport for the console, so no wording here has to be built in.</summary>
    string Name { get; }

    /// <summary>The device's own report of where the song is; the host's playhead follows it.</summary>
    /// <remarks>Raise it regularly while playing. The host listens only to the connected provider,
    /// and only while both it and the report say playing. <see cref="DisplayPlaybackStatus.Position"/>
    /// is song time, stream offset included. A provider that never raises it leaves the host
    /// running a free clock, which drifts from what the room hears and can end the song — and
    /// rotate the singer away — while it is still audible.</remarks>
    event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged;

    // --- finding devices ---

    /// <summary>True while discovery is running; the console's search toggle reads it.</summary>
    /// <remarks>Two different acts under one name: a real sweep for a network transport, and
    /// "open one" where the host launches the device itself. <see cref="SearchesForDevices"/> is
    /// which one this is. A sweep crosses the whole network, so it stays off until someone asks.
    /// Announce <c>DisplaysChanged</c> when it flips.</remarks>
    bool IsDiscovering { get; }

    /// <summary>Whether <see cref="StartDiscoveryAsync"/> looks for devices or opens the one there is.</summary>
    /// <remarks>False for a transport the host opens itself, such as the screens: a console
    /// offering "search for devices" must not launch a screen, and a console with nothing but
    /// such a transport must not offer the search at all. Has a default body returning true, a
    /// plugin's transport being nearly always a sweep; override it only for a transport that opens
    /// its device rather than finding one.</remarks>
    bool SearchesForDevices => true;

    /// <summary>Begins looking for devices, or opens the one there is; see
    /// <see cref="SearchesForDevices"/>.</summary>
    /// <remarks>Discovery may outlive this call; report progress through <see cref="IsDiscovering"/>
    /// and <see cref="Devices"/>, and announce <c>DisplaysChanged</c> as either moves. A sweep that
    /// finds nothing should say so in a log, so "blocked" and "nothing there" can be told
    /// apart.</remarks>
    Task StartDiscoveryAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops browsing and forgets what it found; an active connection is left alone.</summary>
    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);

    /// <summary>Every device this transport currently knows, connected or not.</summary>
    /// <remarks>Read often and from any thread, so answer from memory.</remarks>
    IReadOnlyList<DisplayDevice> Devices { get; }

    // --- connection ---

    /// <summary>The one device this transport drives, or null. Every argument-free member below
    /// addresses it.</summary>
    /// <remarks>Non-null is what makes this provider the one the song goes to.</remarks>
    string? ConnectedDeviceId { get; }

    /// <summary>Identifies the device session; changes on reconnect. Null if disconnected.</summary>
    /// <remarks>Must change whenever the device may have forgotten what it was given — a restarted
    /// receiver or a screen that dropped and came back — since a new value is how the host knows to
    /// hand the song over again.</remarks>
    Guid? SessionId { get; }

    /// <summary>Connects to the device with <paramref name="deviceId"/>. One device per provider.
    /// </summary>
    /// <returns>True once connected, including when that device was already the connected one.
    /// False when the device refused, or when the provider already drives a different device and
    /// will not give it up.</returns>
    /// <remarks>The host disconnects every other display first. Announce <c>DisplaysChanged</c>
    /// once connected, with a new <see cref="SessionId"/>.</remarks>
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Lets go of the connected device.</summary>
    /// <remarks>Once it is gone, <see cref="ConnectedDeviceId"/> and <see cref="SessionId"/> read null;
    /// announce <c>DisplaysChanged</c> then.</remarks>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    // --- transport ---

    /// <summary>What the connected device wants rendered for it, which decides what the host asks a
    /// renderer to produce.</summary>
    /// <remarks>Asked on every load and every reconnect, only of the provider that is connected, so
    /// answer from memory. An answer that throws or is null is taken as asking for nothing special.
    ///
    /// <para>Has a default body answering <see cref="RenderTarget.None"/>: one stream, mixed and
    /// without burned words. Override it to ask for <see cref="RenderTarget.MixesStems"/> — which
    /// commits the provider to playing <see cref="DisplayLoad.Stems"/> and answering
    /// <see cref="SetStemVolumeAsync"/> — or <see cref="RenderTarget.BurnLyrics"/> for a device that
    /// shows a picture but cannot draw words.</para></remarks>
    RenderTarget DescribeTarget() => RenderTarget.None;

    /// <summary>Loads one song or clip, ready to play but not playing.</summary>
    /// <remarks>The call the host makes for every song, every ad clip on the main channel, and every
    /// reload after a key, tempo or mix change, which reopens the stream at the playhead. Play
    /// follows separately. A provider that draws the words reads them for itself during this call,
    /// before play, so they are in place before the first syllable.</remarks>
    Task LoadAsync(DisplayLoad load, CancellationToken cancellationToken = default);

    /// <summary>Starts or resumes what was loaded.</summary>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Holds the song where it is.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>A fade of null or zero stops at once; the room hears the difference.</summary>
    /// <remarks>A fade is asked for only while playing and only when the device reports
    /// <see cref="DisplayDevice.SupportsFade"/>. The host then waits that long before moving the
    /// queue on, so a device that cannot fade must not claim to.</remarks>
    Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default);

    /// <summary>Moves the playhead to <paramref name="position"/>, in song time.</summary>
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>Sets the song's level on the device, 0 to 1.</summary>
    /// <remarks>The host does not call this: the venue's volume is the provider's to apply, on
    /// connect and when the selected venue changes. Has a default body that does nothing.</remarks>
    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Moves one voice against the music, on a display that mixes the stems itself.</summary>
    /// <returns>True when the device took the new level. False when it cannot ride it — it was
    /// handed a stream the host already mixed — and the host then rebuilds the stream at the
    /// playhead with the new mix instead.</returns>
    /// <remarks>Called only while the loaded song carries <see cref="DisplayLoad.Stems"/>. Has a
    /// default body answering false.</remarks>
    Task<bool> SetStemVolumeAsync(StemLevel level, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    // --- the second audio channel: break music and an ad's bed ---

    /// <summary>Loads a stream onto the second audio channel, which plays beside or instead of the
    /// song and carries no timeline.</summary>
    /// <remarks>Called by the host's library break music and for an ad's own voiceover. A device that
    /// keeps the default, which does nothing, simply plays no host-carried break music, while
    /// the host still treats the track as playing.</remarks>
    Task LoadBackgroundAsync(BackgroundLoad background, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Starts or resumes the second channel. Has a default body that does nothing.</summary>
    Task PlayBackgroundAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Holds the second channel. Has a default body that does nothing.</summary>
    Task PauseBackgroundAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Ends the second channel, fading over <paramref name="fade"/> when given. Has a
    /// default body that does nothing.</summary>
    Task StopBackgroundAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Sets the second channel's level, 0 to 1.</summary>
    /// <remarks>The host does not call this: like <see cref="SetVolumeAsync"/>, the venue's level is
    /// the provider's to apply. Has a default body that does nothing.</remarks>
    Task SetBackgroundVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}


/// <summary>One report from a device of where its song is, raised through
/// <see cref="IDisplayProvider.PlaybackStatusChanged"/>.</summary>
public sealed class DisplayPlaybackStatus
{
    /// <summary>Absolute: the stream offset is already added.</summary>
    public required TimeSpan Position { get; init; }

    /// <summary>Whether the device is playing; a report saying not playing does not move the
    /// host's playhead.</summary>
    public required bool IsPlaying { get; init; }

    /// <summary>When <see cref="Position"/> held, in UTC; the host advances the position by the time
    /// since. A device that reports no time of its own may stamp when the report arrived.</summary>
    public required DateTime SampledAtUtc { get; init; }
}

/// <summary>One place a song can come out.</summary>
/// <remarks>What the device wants rendered is not here: the provider answers that through
/// <see cref="IDisplayProvider.DescribeTarget"/>, and what it draws is its own business.</remarks>
public sealed class DisplayDevice
{
    /// <summary>Identifies the device within its provider; what
    /// <see cref="IDisplayProvider.ConnectAsync"/> is handed.</summary>
    public required string Id { get; init; }
    /// <summary>The name the console shows for the device.</summary>
    public required string Name { get; init; }
    /// <summary>What kind of device it is, shown beneath the name; null to show the provider's
    /// name instead.</summary>
    public string? Model { get; init; }
    /// <summary>Where the device is, shown beside the model; null when there is nothing worth
    /// showing.</summary>
    public string? Address { get; init; }

    /// <summary>Whether this is the device the provider is connected to now.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Plays sound.</summary>
    public bool SupportsAudio { get; init; }
    /// <summary>Shows a picture.</summary>
    public bool SupportsVideo { get; init; }

    /// <summary>Rides the song down on a stop rather than cutting it dead.</summary>
    /// <remarks>Not decoration: the host <em>waits out</em> the fade it asks for, so a device that
    /// cannot fade must say so or every stop buys that many seconds of silence before the queue
    /// moves on. A receiver driven over its own transport has no mixer to ride down.</remarks>
    public bool SupportsFade { get; init; }
}
