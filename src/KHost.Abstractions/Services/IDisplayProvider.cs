using KHost.Abstractions.Models;
using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>A transport to somewhere the song comes out, and everything the host can put on it.</summary>
/// <remarks>It finds such places, connects to one, hands it a stream, drives transport on it, and
/// draws on it. It does not decide what the show is — it is told. The screens reach the host
/// through one of these too; that one is core rather than a plugin. See
/// <c>docs/display-provider.md</c> for the shape's reasoning.
///
/// <para>An extension point: a plugin IMPLEMENTS it and the host discovers it, listing it as
/// "Display provider" on the Plugins page. The plugin's object is one singleton shared across every
/// extension interface it implements, and is called from any thread.</para>
///
/// <para><b>One display at a time, across the whole system.</b> Before connecting a device the host
/// disconnects every other provider, and the provider whose <see cref="ConnectedDeviceId"/> is set
/// is the one the song goes to. A provider refuses a second device rather than driving two.</para>
///
/// <para><b>The host drives transport; the provider owns presentation.</b> The host calls only the
/// loading, transport, stem and second-channel members. Everything drawn on the device — the words,
/// the marquee, cards, pictures — and the device's volume are the provider's own business: it reads
/// what it needs from the host's services and messages and sends it to its own device. The drawable
/// members have default bodies that do nothing, so a provider implements what its device can do and
/// ignores the rest.</para>
///
/// <para><b>What a provider can read to draw with.</b> What is on the main channel is
/// <see cref="IPlaybackService.CurrentProgram"/>; who sings next is <see cref="IUpNextService"/>; the
/// venue's QR code is <see cref="IQrCodeOfferService"/>; the card naming who is up arrives as
/// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/>; the break music is
/// <see cref="IBreakMusicService"/>; a song's words are <see cref="ITimedLyricsService"/>. Each
/// names the message that says it moved. <see cref="IPlaybackService"/> depends on every display
/// provider, so a provider must not take it in its constructor: resolve it on first use.</para>
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
    /// <remarks>Read often and from any thread, so answer from memory. The connected device's entry
    /// is where the host reads what it can do; see <see cref="DisplayDevice"/>.</remarks>
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

    /// <summary>What the connected device can take, which decides what the host asks a renderer to
    /// produce for it.</summary>
    /// <remarks>Asked on every load, only of the provider that is connected, so answer from memory.
    /// An answer that throws or is null is taken as asking for nothing special.
    ///
    /// <para>Has a default body: <see cref="RenderTarget.MixesStems"/> when the connected device's
    /// entry in <see cref="Devices"/> claims <see cref="DisplayDevice.SupportsStemMix"/>, and every
    /// other flag false. Override it to ask for more — <see cref="RenderTarget.BurnLyrics"/> for a
    /// device that shows a picture but cannot draw words — keeping it in step with what
    /// <see cref="Devices"/> claims.</para></remarks>
    RenderTarget DescribeTarget()
    {
        var device = Devices.FirstOrDefault(d => d.Id == ConnectedDeviceId)
            ?? Devices.FirstOrDefault(d => d.IsConnected);

        return device is { SupportsStemMix: true }
            ? new RenderTarget { MixesStems = true }
            : RenderTarget.None;
    }

    /// <summary>Loads a stream the host encoded, ready to play but not playing.</summary>
    /// <param name="streamUrl">What to play end to end.</param>
    /// <param name="startOffset">The song position the stream's zero maps to; add it to every
    /// position reported back.</param>
    /// <param name="tempo">Percent the stream was retimed by; converts the device's seconds back to
    /// song seconds.</param>
    /// <param name="cancellationToken">Abandons the load.</param>
    /// <remarks>The minimum a provider must implement to put a song out. The host never calls it
    /// directly: it calls the <see cref="LoadMediaCommand"/> overload, whose default body forwards
    /// here.</remarks>
    Task LoadAsync(string streamUrl, TimeSpan startOffset, int tempo = 0, CancellationToken cancellationToken = default);

    /// <summary>The whole load, including stems for a device that mixes them itself.</summary>
    /// <remarks>The call the host makes for every song, every ad clip on the main channel, and every
    /// reload after a key, tempo or mix change, which reopens the stream at the playhead. Play
    /// follows separately. A provider that draws the words reads them for itself during this call,
    /// before play, so they are in place before the first syllable.
    ///
    /// <para>Has a default body forwarding the stream to the other overload, so a provider that has
    /// not heard of stems keeps working and simply plays what the host already mixed. Override it
    /// only alongside <see cref="DisplayDevice.SupportsStemMix"/>; the two are one claim made in two
    /// places.</para></remarks>
    Task LoadAsync(LoadMediaCommand media, CancellationToken cancellationToken = default)
        => LoadAsync(media.StreamUrl, media.StreamStartOffset, media.Tempo, cancellationToken);

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
    /// <remarks>Only ever sent to a device whose <see cref="DisplayDevice.SupportsStemMix"/> is set;
    /// anything else was handed a stream the host already mixed, where the levels are baked in.
    /// Has a default body that does nothing.</remarks>
    Task SetStemVolumeAsync(SetStemVolumeCommand stem, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // --- drawable ---
    //
    // Every one has a default body, which is what makes the full surface honest for a device that
    // cannot draw: a receiver's provider implements the transport above and ignores all of this.
    // The same deliberate exception IMediaPlaybackGate.Claims and IPluginButtonHandler.DescribeButton
    // are. What a device can actually show is on DisplayDevice, so the host need not send what
    // will not land.

    /// <summary>Hands the device a song's words and their timing, to draw over the song.</summary>
    /// <remarks>The host does not call this; presentation is the provider's. A provider whose device
    /// draws words reads them from <see cref="ITimedLyricsService"/> while loading and sends them
    /// itself. A device that cannot draw them can still show them: ask for
    /// <see cref="RenderTarget.BurnLyrics"/> through <see cref="DescribeTarget"/>, and a renderer
    /// able to may burn them into the picture. Has a default body that does nothing.</remarks>
    Task SetTimedLyricsAsync(SetTimedLyricsCommand lyrics, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Hands the device the marquee, whole.</summary>
    /// <remarks>The host does not call this; presentation is the provider's. The content comes from
    /// <see cref="IScreenMarqueeService.BuildAsync"/>, which names the singers
    /// <see cref="IUpNextService"/> does; ask again on
    /// <see cref="KHost.Abstractions.Messaging.Messages.SelectedVenueChanged"/> and
    /// <see cref="KHost.Abstractions.Messaging.Messages.UpNextChanged"/>. Has a default body that
    /// does nothing.</remarks>
    Task SetMarqueeAsync(SetMarqueeCommand marquee, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Hands the device the QR codes to show, whole; an empty set clears them.</summary>
    /// <remarks>The host does not call this; presentation is the provider's. What to show comes from
    /// <see cref="IQrCodeOfferService"/>, which names when to read it again. Has a default body
    /// that does nothing.</remarks>
    Task SetQrCodesAsync(SetScreenQrCodesCommand codes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Puts up the card naming who sings next.</summary>
    /// <remarks>The host does not call this; presentation is the provider's. The card arrives as
    /// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/> when a host asks for
    /// it. Has a default body that does nothing.</remarks>
    Task ShowNextSingerAsync(ShowNextSingerCommand card, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Puts up, or takes down, the card naming the break music, sent whole on change.</summary>
    /// <remarks>The host does not call this; presentation is the provider's. Has a default body
    /// that does nothing.</remarks>
    Task SetBreakMusicCardAsync(SetBreakMusicCardCommand card, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Puts a still up, such as the venue's card or an ad's; it stays until taken down.</summary>
    /// <remarks>The host does not call this; presentation is the provider's. An ad's still is
    /// <see cref="PlaybackProgram.AdStill"/> on <see cref="IPlaybackService.CurrentProgram"/>. Has a
    /// default body that does nothing.</remarks>
    Task ShowImageAsync(ShowImageCommand image, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Takes down whatever <see cref="ShowImageAsync"/> put up.</summary>
    /// <remarks>The host does not call this. Has a default body that does nothing.</remarks>
    Task HideImageAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Blanks or restores the song's picture without stopping playback.</summary>
    /// <remarks>The host does not call this. Has a default body that does nothing.</remarks>
    Task SetVideoAsync(bool enabled, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // --- the second audio channel: break music and an ad's bed ---

    /// <summary>Loads a stream onto the second audio channel, which plays beside or instead of the
    /// song and carries no timeline.</summary>
    /// <remarks>Called by the host's library break music and for an ad's own voiceover. A device that
    /// keeps the default, which does nothing, simply plays no host-carried break music, while
    /// the host still treats the track as playing.</remarks>
    Task LoadBackgroundAsync(LoadBackgroundCommand background, CancellationToken cancellationToken = default)
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

/// <summary>One place a song can come out, and what it can show once it is there.</summary>
/// <remarks>The capabilities sit here rather than on the provider: a provider may reach devices of
/// differing ability, and the host is asking about the thing in the room. One flag per drawn thing
/// rather than one for all of them, because the host answers each differently — lyrics are fixed
/// for the whole song and can be burned into the stream by a renderer for a device that cannot
/// draw them, while a marquee that rescrolls on every venue edit would mean restarting the encode,
/// so it is simply left off.</remarks>
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

    /// <summary>Takes the stems unmixed and rides the levels itself.</summary>
    /// <remarks>Changes what the host does, not only what it sends: on a device without this, a mix
    /// change reopens the stream at the playhead, which the room hears. A device with it is sent a
    /// gain instead and nothing is re-encoded. The host then need not encode the song at all for
    /// such a device, so the stems must be something it can decode on its own.</remarks>
    public bool SupportsStemMix { get; init; }

    /// <summary>Draws a song's words itself, from the timing the host hands over.</summary>
    public bool SupportsLyrics { get; init; }

    /// <summary>Draws the venue's scrolling marquee; a device without it shows none.</summary>
    public bool SupportsMarquee { get; init; }
    /// <summary>Draws the QR code the venue has chosen to show; a device without it shows none.</summary>
    public bool SupportsQrCodes { get; init; }

    /// <summary>Anything put up as a picture rather than a layer: the next-singer card, the
    /// break-music card, and a plain shown image, which already share that shape.</summary>
    public bool SupportsImage { get; init; }
}
