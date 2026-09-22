using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>A transport to somewhere the song comes out, and everything the host can put on it.</summary>
/// <remarks>It finds such places, connects to one, hands it a stream, drives transport on it, and
/// draws on it. It does not decide what the show is — it is told. The screens reach the host
/// through one of these too; that one is core rather than a plugin. See
/// <c>docs/display-provider.md</c> for the shape's reasoning.</remarks>
public interface IDisplayProvider
{
    /// <summary>Names the transport for the console, so no wording here has to be built in.</summary>
    string Name { get; }

    /// <summary>How many devices this transport drives at once.</summary>
    /// <remarks>Enforced, not advertised: a provider at its limit refuses the next connection
    /// rather than taking it and behaving oddly. It is also the tripwire —
    /// <see cref="ConnectedDeviceId"/>, <see cref="SessionId"/> and every argument-free member
    /// below are honest only while this is 1. The day it is 2 they become collections and each
    /// drawable member needs a device to address.</remarks>
    int MaxConnectedDevices => 1;

    /// <summary>The only clock with no syncable screen; a free timer would end the song early.</summary>
    event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged;

    // --- finding devices ---

    /// <summary>Browsing sweeps the whole network, so it is off until someone asks for it.</summary>
    /// <remarks>Two different acts under one name: a real sweep for a network transport, and
    /// "open one" where the host launches the device itself. <see cref="SearchesForDevices"/> is
    /// which one this is.</remarks>
    bool IsDiscovering { get; }

    /// <summary>Whether <see cref="StartDiscoveryAsync"/> looks for devices or opens the one there is.</summary>
    /// <remarks>False for a transport the host opens itself, such as the screens: a console
    /// offering "search for devices" must not launch a screen, and a console with nothing but
    /// such a transport must not offer the search at all. True by default, a plugin's transport
    /// being nearly always a sweep.</remarks>
    bool SearchesForDevices => true;

    Task StartDiscoveryAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops browsing and forgets what it found; an active connection is left alone.</summary>
    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<DisplayDevice> Devices { get; }

    // --- connection ---

    string? ConnectedDeviceId { get; }

    /// <summary>Identifies the device session; changes on reconnect. Null if disconnected.</summary>
    Guid? SessionId { get; }

    /// <summary>Replaces whatever was connected before: one song, one device.</summary>
    /// <returns>False when the device refused, or when the provider is already at
    /// <see cref="MaxConnectedDevices"/> and will not give up what it has.</returns>
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    // --- transport ---

    /// <summary><paramref name="tempo"/> converts the device's seconds back to song seconds.</summary>
    Task LoadAsync(string streamUrl, TimeSpan startOffset, int tempo = 0, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    /// <summary>A fade of null or zero stops at once; the room hears the difference.</summary>
    Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // --- drawable ---
    //
    // Every one has a default body, which is what makes the full surface honest for a device that
    // cannot draw: a receiver's provider implements the transport above and ignores all of this.
    // The same deliberate exception IMediaPlaybackGate.Claims and IPluginButtonHandler.DescribeButton
    // are. What a device can actually show is on DisplayDevice, so the host need not send what
    // will not land.

    Task SetTimedLyricsAsync(SetTimedLyricsCommand lyrics, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task SetMarqueeAsync(SetMarqueeCommand marquee, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task SetQrCodesAsync(SetScreenQrCodesCommand codes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task ShowNextSingerAsync(ShowNextSingerCommand card, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task SetBreakMusicCardAsync(SetBreakMusicCardCommand card, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task ShowImageAsync(ShowImageCommand image, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task HideImageAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task SetVideoAsync(bool enabled, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // --- the second audio channel: break music and an ad's bed ---

    Task LoadBackgroundAsync(LoadBackgroundCommand background, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task PlayBackgroundAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task PauseBackgroundAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task StopBackgroundAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task SetBackgroundVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class DisplayPlaybackStatus
{
    /// <summary>Absolute: the stream offset is already added.</summary>
    public required TimeSpan Position { get; init; }

    public required bool IsPlaying { get; init; }

    /// <summary>Arrival time, not sample time: no clock handshake; off by a LAN hop, negligible.</summary>
    public required DateTime SampledAtUtc { get; init; }
}

/// <summary>One place a song can come out, and what it can show once it is there.</summary>
/// <remarks>The capabilities sit here rather than on the provider: a provider may reach devices of
/// differing ability, and the host is asking about the thing in the room. One flag per drawn thing
/// rather than one for all of them, because the host answers each differently — lyrics are fixed
/// for the whole song and can be composited into the stream for a device that cannot draw them,
/// while a marquee that rescrolls on every venue edit would mean restarting the encode, so it is
/// simply left off.</remarks>
public sealed class DisplayDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }

    public bool IsConnected { get; init; }

    public bool SupportsAudio { get; init; }
    public bool SupportsVideo { get; init; }

    /// <summary>Rides the song down on a stop rather than cutting it dead.</summary>
    /// <remarks>Not decoration: the host <em>waits out</em> the fade it asks for, so a device that
    /// cannot fade must say so or every stop buys that many seconds of silence before the queue
    /// moves on. A receiver driven over its own transport has no mixer to ride down.</remarks>
    public bool SupportsFade { get; init; }

    /// <summary>Draws a song's words itself, from the timing the host hands over.</summary>
    public bool SupportsLyrics { get; init; }

    public bool SupportsMarquee { get; init; }
    public bool SupportsQrCodes { get; init; }

    /// <summary>Anything put up as a picture rather than a layer: the next-singer card, the
    /// break-music card, and a plain shown image, which already share that shape.</summary>
    public bool SupportsImage { get; init; }
}
