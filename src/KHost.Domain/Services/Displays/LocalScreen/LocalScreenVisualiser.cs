using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Display;
using KHost.Domain.Services.Visualisations;
using KHost.IPC.SignalR.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>Decides whether the local screen draws a visualiser under the playing song, and which,
/// and sends it. Also picks the next-singer card's.</summary>
/// <remarks>Presentation the <see cref="LocalScreenDisplayProvider"/> owns, kept apart because it
/// carries its own per-song memory: the pick, the probe's answer, the levels and the video.</remarks>
internal sealed class LocalScreenVisualiser
{
    private readonly ILogger _logger;
    private readonly IServiceProvider? _services;
    private readonly Func<IScreenCommand, Task<bool>> _send;
    private readonly Func<PlaybackProgram.Playing, TimedLyrics?> _lyricsFor;
    private readonly Func<Task<Venue.VenueSettings?>> _readVenue;

    // Serialises visualiser sends: a load and a venue edit can both decide it at once.
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>What was decided for the song now loaded; replaced whole when the song changes.</summary>
    private SongState? _song;

    /// <param name="send">Sends a command to the screen; never throws.</param>
    /// <param name="lyricsFor">The words read for the song, or null when none were read for it.</param>
    /// <param name="readVenue">Reads the selected venue's settings afresh.</param>
    public LocalScreenVisualiser(
        ILogger logger,
        IServiceProvider? services,
        Func<IScreenCommand, Task<bool>> send,
        Func<PlaybackProgram.Playing, TimedLyrics?> lyricsFor,
        Func<Task<Venue.VenueSettings?>> readVenue)
    {
        _logger = logger;
        _services = services;
        _send = send;
        _lyricsFor = lyricsFor;
        _readVenue = readVenue;
    }

    public static SetVisualiserCommand Off => new() { Enabled = false };

    /// <summary>Tells the screen whether to draw a visualiser under the playing song, and which.</summary>
    /// <remarks>On a load, decided for that load; with none (a venue edit), re-decided for the song
    /// already loaded, and skipped for a song whose own load has not decided it yet. Never throws:
    /// the visualiser is decoration, and a song plays over black without it.</remarks>
    public async Task SendAsync(DisplayLoad? load, Func<Task<Venue.VenueSettings?>>? venue = null)
    {
        if (_services?.GetService<IPlaybackService>()?.CurrentProgram is not PlaybackProgram.Playing song)
            return;

        await _lock.WaitAsync();
        try
        {
            if (_song is not { } state || !ReferenceEquals(song, state.Song))
            {
                if (load is null) return;

                state = _song = new SongState(song, load);
            }
            else if (load is not null)
            {
                state.Load = load;
            }

            await _send(await DecideAsync(state, venue ?? _readVenue));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not decide the visualiser for '{Title}'", song.Media.Title);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Takes the visualiser down between songs, and drops the last song's levels with it.</summary>
    public async Task TakeDownAsync()
    {
        _services?.GetService<ISongLevelsService>()?.Clear();
        await _send(Off);
    }

    /// <summary>The venue playlist's next entry, taken the way a song takes one, with no levels:
    /// between singers there is no song for the host to read them from.</summary>
    /// <remarks>Never throws: the card matters more than what is behind it.</remarks>
    public async Task<SetVisualiserCommand?> ForNextSingerCardAsync(NextSingerCard card, Func<Task<Venue.VenueSettings?>> readVenue)
    {
        try
        {
            var venue = await readVenue();
            if (venue?.VisualisationPlaylistId is not { } playlistId) return null;
            if (_services?.GetService<IVisualisationPlaylistService>() is not { } playlists) return null;

            // An empty or missing playlist answers null.
            if (await playlists.SelectNextAsync(playlistId) is not { } entry) return null;

            // The turn is taken all the same: looking on for a drawing would spend two.
            if (entry.PresetSource == VisualiserPresetSource.Video) return null;

            return VisualiserFor(entry, "the next-singer card", () => null, venue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not pick a visualisation for the card naming {Singer}", card.Singer);
            return null;
        }
    }

    /// <summary>On only for a performance with timed words and nothing of its own to show under them
    /// (<see cref="SongBackdrops.ForPlaying"/> answering black): the performance's own background
    /// when it names one, else an entry from the venue's visualisation playlist.</summary>
    /// <remarks>Any preset the timing names is ignored. A background that is black draws nothing; a
    /// look that does not resolve draws the venue's playlist. An ad reads no words, so it never
    /// gets one.</remarks>
    private async Task<SetVisualiserCommand> DecideAsync(SongState state, Func<Task<Venue.VenueSettings?>> readVenue)
    {
        var song = state.Song;
        var load = state.Load;
        var venue = await readVenue();
        var playlistId = venue?.VisualisationPlaylistId;
        var background = await ReadBackgroundAsync(song);

        // Ahead of the probe below: a venue with no playlist and a song with no look of its own
        // draws nothing, and must not cost a read of the file.
        if (playlistId is null && background is not { Type: PerformanceBackgroundType.Look }) return Off;

        var lyrics = _lyricsFor(song);
        if (lyrics is not { Pages.Count: > 0 }) return Off;

        var path = song.Media.FilePath;
        var backdrop = SongBackdrops.ForPlaying(hasTimedLyrics: true, path, await PlaysOwnPictureAsync(state, path, load));
        if (backdrop != SongBackdrop.Black) return Off;

        if (background is { Type: PerformanceBackgroundType.Black }) return Off;

        if (background is { Type: PerformanceBackgroundType.Look }
            && LookForSong(state, background, () => LevelsUrlFor(state, path, load), venue) is { } own)
            return own;

        if (playlistId is not { } venuePlaylistId) return Off;
        if (_services?.GetService<IVisualisationPlaylistService>() is not { } playlists) return Off;

        // Picked only once the song is known to draw one, so a video does not use up a turn.
        if (await EntryForSongAsync(state, playlists, venuePlaylistId) is not { } entry) return Off;

        // Never through the levels: a video is drawn muted and does not follow the song.
        if (entry.PresetSource == VisualiserPresetSource.Video) return VideoVisualiserFor(state, entry, entry.Id) ?? Off;

        return VisualiserFor(entry, song.Media.Title, () => LevelsUrlFor(state, path, load), venue) ?? Off;
    }

    /// <summary>The performance's own background, read afresh: the program carries the row as it was
    /// at load. Null when it names none, or it cannot be read.</summary>
    private async Task<PerformanceBackground?> ReadBackgroundAsync(PlaybackProgram.Playing song)
    {
        if (song.Performance is not { } performance || _services?.GetService<IPerformanceService>() is not { } performances)
            return null;

        return (await performances.ReadAsync(performance.Id))?.Background;
    }

    /// <summary>The command that draws the performance's own look; null when it does not resolve,
    /// so the venue's playlist draws instead. A video whose URL is still being found is drawn as
    /// off meanwhile, not handed to the playlist.</summary>
    private SetVisualiserCommand? LookForSong(
        SongState state, PerformanceBackground look, Func<string?> levelsUrl, Venue.VenueSettings? venue)
    {
        if (look.PresetSource != VisualiserPresetSource.Video) return VisualiserFor(look, state.Song.Media.Title, levelsUrl, venue);

        // Known not to play, or naming none: the playlist answers, and keeps its own pick.
        if (look.VideoMediaId is not { } mediaId || state.Video is { Url: null } known && known.MediaId == mediaId)
            return null;

        var pick = LookPick(state.Song);
        state.Pick = pick;
        return VideoVisualiserFor(state, look, pick.EntryId) ?? Off;
    }

    /// <summary>The pick a performance's own look is kept under: no playlist has an empty id, so it
    /// never matches a playlist's entry, and a late video URL for it is still this song's.</summary>
    private static (Guid PlaylistId, Guid EntryId) LookPick(PlaybackProgram.Playing song)
        => (Guid.Empty, song.Performance!.Id);

    /// <summary>The command that draws <paramref name="entry"/>; null when its preset cannot be
    /// found, which leaves whatever was under it.</summary>
    /// <remarks><paramref name="levelsUrl"/> is asked only once the preset resolves: it may start a
    /// read of the song's levels.</remarks>
    private SetVisualiserCommand? VisualiserFor(
        IVisualisationLook entry, string drawnFor, Func<string?> levelsUrl, Venue.VenueSettings? venue)
    {
        string? name = null, url = null, builtIn = null;
        if (entry.PresetSource == VisualiserPresetSource.BuiltIn)
        {
            if (!VisualiserPresetService.BuiltIns.Any(b => b.Name == entry.PresetName))
            {
                _logger.LogWarning("The visualisation names a built-in drawing '{Preset}' the host does not have; '{Title}' draws without it",
                    entry.PresetName, drawnFor);
                return null;
            }

            builtIn = entry.PresetName;
        }
        else if (entry.PresetSource == VisualiserPresetSource.Imported)
        {
            if (ImportedPresetUrl(entry.PresetName) is not { } imported)
            {
                _logger.LogWarning("The visualisation's imported preset '{Preset}' is not there any more; '{Title}' draws without it",
                    entry.PresetName, drawnFor);
                return null;
            }

            url = imported;
        }
        else
        {
            name = entry.PresetName;
        }

        return new SetVisualiserCommand
        {
            Enabled = true,
            PresetName = name,
            PresetUrl = url,
            BuiltIn = builtIn,
            BarCount = entry.BarCount,
            ColourScheme = entry.ColourScheme,
            Colour = entry.Colour,
            Brightness = entry.Brightness,
            Saturation = entry.Saturation,
            Sensitivity = entry.Sensitivity,
            LevelsUrl = levelsUrl(),

            // A preset's colours are its own; only the host's drawings take the venue's.
            VenuePalette = builtIn is not null && entry.RespectsVenueTheme ? venue?.ResolveScreenColours().VisualisationPalette : null,
        };
    }

    /// <summary>The command that plays <paramref name="entry"/>'s video, once its URL is known; null
    /// until then, or when it cannot be had.</summary>
    /// <remarks>The first ask for a song is answered off the load path: an encode can take seconds,
    /// and the song must not wait on its decoration. <see cref="ResolveVideoAsync"/> decides again
    /// once it is known.</remarks>
    private SetVisualiserCommand? VideoVisualiserFor(SongState state, IVisualisationLook entry, Guid pickId)
    {
        if (entry.VideoMediaId is not { } mediaId)
        {
            _logger.LogWarning("The visualisation names no video; '{Title}' draws without it", state.Song.Media.Title);
            return null;
        }

        if (state.Video is { } known && known.MediaId == mediaId)
        {
            if (known.Url is null) return null;

            return new SetVisualiserCommand
            {
                Enabled = true,
                VideoUrl = known.Url,
                Brightness = entry.Brightness,
                Saturation = entry.Saturation,
            };
        }

        if (state.VideoPending != mediaId)
        {
            state.VideoPending = mediaId;
            var song = state.Song;
            _ = Task.Run(() => ResolveVideoAsync(song, pickId, mediaId));
        }

        return null;
    }

    /// <summary>Finds where the screen plays a video entry's video, then draws it if the same song
    /// and the same entry are still current.</summary>
    /// <remarks>Never throws. A song that ended, or an entry replaced, while it was encoding gets
    /// nothing.</remarks>
    private async Task ResolveVideoAsync(PlaybackProgram.Playing song, Guid entryId, Guid mediaId)
    {
        string? url = null;
        try
        {
            var media = _services?.GetService<IMediaService>() is { } library ? await library.ReadAsync(mediaId) : null;

            if (media is null)
                _logger.LogWarning("The visualisation's video {MediaId} is not in the library; '{Title}' draws without it", mediaId, song.Media.Title);
            else if (media.Type != MediaType.Video)
                _logger.LogWarning("The visualisation names '{Video}', which is not a video; '{Title}' draws without it", media.Title, song.Media.Title);
            else if (_services?.GetService<IVideoBackdropService>() is { } backdrops)
                url = await backdrops.UrlForAsync(media);

            if (media is { Type: MediaType.Video } && url is null)
                _logger.LogWarning("The screen cannot play '{Video}'; '{Title}' draws without it", media.Title, song.Media.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not find the visualisation's video {MediaId} for '{Title}'", mediaId, song.Media.Title);
        }

        await _lock.WaitAsync();
        try
        {
            if (_song is not { } state || !ReferenceEquals(song, state.Song)
                || !ReferenceEquals(song, _services?.GetService<IPlaybackService>()?.CurrentProgram))
                return;

            // Kept even for an entry since replaced: it is this song's answer for this video.
            if (state.VideoPending == mediaId) state.VideoPending = null;
            state.Video = (mediaId, url);

            if (state.Pick?.EntryId != entryId) return;

            // A playlist's video that cannot play stays black; a performance's own falls back to the playlist.
            if (url is null && state.Pick?.PlaylistId != Guid.Empty) return;

            // Decided whole again rather than sent as found: the venue or the entry may have moved.
            await _send(await DecideAsync(state, _readVenue));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not draw the visualisation's video for '{Title}'", song.Media.Title);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The song's entry: the one already picked while it is still in the venue's playlist,
    /// read afresh so an edit to its settings shows mid-song; else the playlist's next.</summary>
    private static async Task<VisualisationEntry?> EntryForSongAsync(SongState state, IVisualisationPlaylistService playlists, Guid playlistId)
    {
        // An empty or missing playlist falls through to SelectNextAsync, which answers null.
        var playlist = await playlists.ReadWithEntriesAsync(playlistId);

        if (state.Pick is { } pick && pick.PlaylistId == playlistId
            && playlist?.Entries.FirstOrDefault(entry => entry.Id == pick.EntryId) is { } kept)
            return kept;

        var next = await playlists.SelectNextAsync(playlistId);
        state.Pick = next is null ? null : (playlistId, next.Id);

        return next;
    }

    /// <summary>Where the screen fetches an imported preset, versioned by when it was written so a
    /// re-import reaches a screen already drawing it; null when no such preset is stored.</summary>
    private string? ImportedPresetUrl(string name)
    {
        if (_services?.GetService<IVisualiserPresetService>() is not { } presets) return null;
        if (_services.GetService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>() is not { } options) return null;

        var preset = presets.ReadAll().FirstOrDefault(p => p.Source == VisualiserPresetSource.Imported && p.Name == name);
        if (preset is null) return null;

        return $"{options.CurrentValue.BaseAddress.TrimEnd('/')}{VisualiserPresetService.RoutePrefix}"
            + $"{Uri.EscapeDataString(name)}?v={preset.ImportedUtc?.Ticks ?? 0}";
    }

    /// <summary>Starts reading the song's levels the first time its visualiser is on, and answers
    /// where they are served; null when there is nothing the host can read them from.</summary>
    /// <remarks>Read for every song that draws one, whether or not the screen can listen for itself:
    /// the screen decides which it uses, and a stem song that is later re-keyed becomes an encoded
    /// one the screen cannot hear. A load with nothing readable is asked again on the next load.</remarks>
    private string? LevelsUrlFor(SongState state, string path, DisplayLoad load)
    {
        if (state.LevelsUrl is not null) return state.LevelsUrl;
        if (_services?.GetService<ISongLevelsService>() is not { } levels) return null;

        var stems = _services.GetService<IStemStreamService>();

        // A stem song loaded already re-keyed arrives as the host's own mix of its stems.
        var mixed = load.Stems.Count == 0 && load.StreamUrl is { } stream ? stems?.StemsMixedInto(stream) : null;

        var inputs = SongLevels.InputsFor(path, load, url =>
        {
            try { return stems?.ResolveStemInput(url); }
            catch (InvalidOperationException) { return null; }
        }, mixed);

        if (inputs is null) return null;

        return state.LevelsUrl = levels.Begin(inputs);
    }

    /// <summary>Whether the screen shows a picture from the song's own file for this load.</summary>
    /// <remarks>Stems are sound only. With no probe to ask, the answer is yes: a visualiser drawn
    /// over a singer's own video is worse than black under words.</remarks>
    private async Task<bool> PlaysOwnPictureAsync(SongState state, string path, DisplayLoad load)
    {
        if (load.Stems.Count > 0 || load.StreamUrl is null) return false;

        // Free, and nothing from an audio file is ever shown, so the probe is skipped.
        if (!SongBackdrops.MayShowPictureFrom(path)) return false;

        if (state.SourceHasPicture is { } known) return known;

        if (_services?.GetService<ISourcePictureProbe>() is not { } probe) return true;

        var hasPicture = await probe.HasMovingPictureAsync(path);
        state.SourceHasPicture = hasPicture;

        return hasPicture;
    }

    /// <summary>One song's decision so far, held by the song's identity: a rebuild or a rejoin reloads
    /// the same program and must keep the same picture. A new song starts a new one.</summary>
    private sealed record SongState(PlaybackProgram.Playing Song, DisplayLoad Load)
    {
        /// <summary>The latest load of <see cref="Song"/>; a key change's reload replaces it.</summary>
        public DisplayLoad Load { get; set; } = Load;

        /// <summary>The playlist entry picked for the song, or <see cref="LookPick"/> for its own look.</summary>
        public (Guid PlaylistId, Guid EntryId)? Pick { get; set; }

        /// <summary>The probe's answer, asked once per song.</summary>
        public bool? SourceHasPicture { get; set; }

        /// <summary>Where the song's levels are served, once a read was started. Kept across a key
        /// change's reload: the levels are indexed by song time.</summary>
        public string? LevelsUrl { get; set; }

        /// <summary>The video entry's URL, null when it cannot be had; asked once per song and video,
        /// since the first ask may encode for seconds.</summary>
        public (Guid MediaId, string? Url)? Video { get; set; }

        /// <summary>The video being resolved off the load path.</summary>
        public Guid? VideoPending { get; set; }
    }
}
