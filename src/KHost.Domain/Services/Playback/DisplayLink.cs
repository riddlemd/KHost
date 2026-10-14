using System.Runtime.CompilerServices;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.Displays.LocalScreen;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Playback;

/// <summary>The way out to whichever display is carrying the song: which one that is, what it
/// takes, calls made on it, and which of its sessions already has the song.</summary>
/// <remarks>Owned by <see cref="PlaybackService"/>, which decides what a joined or lost session
/// means for the song; this only answers and relays.</remarks>
internal sealed class DisplayLink
{
    // Guards _sessionId only.
    private readonly Lock _sessionLock = new();

    private readonly ILogger _logger;

    // The receiver app session the current song was loaded into. Compared rather than the device
    // id: a receiver that restarts is the same device having forgotten everything.
    private Guid? _sessionId;

    public DisplayLink(IEnumerable<IDisplayProvider> displays, ILogger logger)
    {
        // Every display the host can reach, screens included — the screens are a provider too.
        // One is connected at a time, which the providers enforce; this is simply all of them.
        All = [.. displays];
        _logger = logger;
    }

    public IReadOnlyList<IDisplayProvider> All { get; }

    /// <summary>The screens, the only displays that report a held start or a reached end.</summary>
    public IEnumerable<LocalScreenDisplayProvider> Screens => All.OfType<LocalScreenDisplayProvider>();

    /// <summary>The first display with a session, whether or not it already has the song.</summary>
    public IDisplayProvider? Joined => All.FirstOrDefault(display => display.SessionId is not null);

    /// <summary>Whether a screen going away was asked for: a Turn Off, a switch or a shutdown.</summary>
    /// <remarks>Only the screen — core, not a plugin — tracks which; a plugin display's own
    /// disconnect has no such signal.</remarks>
    public bool DisconnectWasRequested => Screens.Any(screen => screen.DisconnectWasRequested);

    /// <summary>Whether anything is connected; a provider that throws reading it counts as no.</summary>
    public bool IsConnected()
    {
        try
        {
            return ConnectedDisplay.Find(All) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the connected displays");
        }

        return false;
    }

    /// <summary>Whether <paramref name="sender"/> is the display carrying the song, whose reports
    /// alone define its clock.</summary>
    public bool IsCarrying(object? sender)
        => ConnectedDisplay.Find(All)?.Provider is { } carrying && ReferenceEquals(sender, carrying);

    /// <summary>Whether the display carrying the song can ride it down rather than cut it.</summary>
    /// <remarks>A device that has not listed itself yet is taken to fade: over-waiting is a pause
    /// nobody hears, while under-waiting cuts a song off mid-word.</remarks>
    public bool CanFade()
        => ConnectedDisplay.Find(All) is { } connected
            && (connected.Device is null || connected.Device.SupportsFade);

    public void ForgetSession()
    {
        lock (_sessionLock) _sessionId = null;
    }

    /// <summary>Takes <paramref name="session"/> as the one that has the song.</summary>
    /// <returns>False when it already had it.</returns>
    public bool TryClaimSession(Guid session)
    {
        lock (_sessionLock)
        {
            if (session == _sessionId) return false;
            _sessionId = session;
            return true;
        }
    }

    /// <summary>Records that the connected display's session is being handed the song now.</summary>
    /// <remarks>A DisplaysChanged for it arriving late (announced off the hub thread) must not
    /// replay it on top, a second load and a second play.</remarks>
    public void NoteLoading()
    {
        if (ConnectedDisplay.Find(All)?.Provider.SessionId is { } session)
            lock (_sessionLock) _sessionId = session;
    }

    /// <summary>Makes one call on whatever the song is coming out of.</summary>
    /// <remarks>A provider that throws is logged, never rethrown: the caller is moving the show on
    /// and a display that has gone must not stop it.</remarks>
    public async Task SendAsync(
        Func<IDisplayProvider, Task> call,
        [CallerArgumentExpression(nameof(call))] string what = "")
    {
        if (ConnectedDisplay.Find(All) is not { } connected) return;

        try { await call(connected.Provider); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {Call} to {Provider}", what, connected.Provider.Name);
        }
    }

    /// <summary>What the display that is up can take, in its own words, which decides what is
    /// worth producing.</summary>
    /// <remarks>Nothing connected asks for nothing special — whatever connects later triggers a
    /// reload. A plugin's answer is guarded: a provider that cannot describe itself still plays.</remarks>
    public RenderTarget DescribeTarget()
        => ConnectedDisplay.Find(All) is { Provider: var provider }
            ? DescribeTarget(provider)
            : RenderTarget.None;

    public RenderTarget DescribeTarget(IDisplayProvider provider)
    {
        try
        {
            return provider.DescribeTarget() ?? RenderTarget.None;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Display} could not describe what it takes; rendering for nothing special", provider.Name);
            return RenderTarget.None;
        }
    }

    /// <summary>Asks the connected display to move one stem itself.</summary>
    /// <returns>False when nothing is connected or the display would not take it.</returns>
    public async Task<bool> TryMoveStemAsync(AudioTrackRole role, int volume, string? voice)
    {
        if (ConnectedDisplay.Find(All) is not { Provider: var display }) return false;

        // A throw counts as a refusal: a change that silently never lands is worse than a rebuild.
        try
        {
            return await display.SetStemVolumeAsync(new StemLevel { Role = role, Voice = voice, Volume = volume });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Display} could not move the {Role} stem; rebuilding the stream", display.Name, role);
            return false;
        }
    }
}
