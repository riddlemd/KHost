using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

/// <summary>The song controls themselves: key, tempo and a fader per voice the file ships apart.</summary>
/// <remarks>Shared by the playing song's panel and the queued-turn editor, so the two cannot offer
/// different ranges, steps or faders for the same song.</remarks>
public partial class SongControlList
{
    /// <summary>Ten steps to either end, where one percent a step would be fifty.</summary>
    private const int TempoStep = 5;

    /// <summary>Coarser than tempo: a level is judged by ear, not read off a number.</summary>
    private const int VolumeStep = 5;

    [Parameter] public SongControlStyle Style { get; set; }

    /// <summary>The file's probed voices; empty for an ordinary single-track song.</summary>
    [Parameter] public IReadOnlyList<AudioTrack> Tracks { get; set; } = [];

    /// <summary>Written as the controls move, before either callback hears of it.</summary>
    [Parameter] public SongControlValues Values { get; set; } = new();

    /// <summary>Every value a drag passes over, for a readout.</summary>
    [Parameter] public EventCallback<SongControlChange> Input { get; set; }

    /// <summary>Only the value a control was let go of on.</summary>
    [Parameter] public EventCallback<SongControlChange> Committed { get; set; }

    /// <summary>Every control shown at its value and none movable.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>The panel as data: one list rendered twice, not copies that could drift apart.</summary>
    private IEnumerable<SongControl> Controls()
    {
        yield return Control("Key", "Key, in semitones from the recording",
            Values.Pitch, IPlaybackService.MinPitch, IPlaybackService.MaxPitch, 1,
            SongAdjustmentDisplay.FormatPitch, SongControlSetting.Pitch);

        yield return Control("Tempo", "Tempo, as a percentage of the recording",
            Values.Tempo, IPlaybackService.MinTempo, IPlaybackService.MaxTempo, TempoStep,
            SongAdjustmentDisplay.FormatTempo, SongControlSetting.Tempo);

        // Only a file that ships its voices apart has anything here to balance, and the music
        // never gets a fader: it is the reference the voices are set against. Laid out the way
        // another karaoke product orders its faders — backing, then a fader per singer's lead —
        // so a host moving from one finds them.
        if (Tracks.Any(t => t.Role == AudioTrackRole.Backing))
            yield return Control("Backing Vocals", "Backing vocal volume, as a percentage",
                Values.Backing, AudioMix.MinVolume, AudioMix.MaxVolume, VolumeStep,
                FormatVolume, SongControlSetting.Backing);

        var leads = Tracks.Where(t => t.Role == AudioTrackRole.Lead).ToList();

        if (leads.Any(t => t.Voice is null))
            yield return Control("Lead Vocal", "Lead vocal volume, as a percentage",
                Values.Lead, AudioMix.MinVolume, AudioMix.MaxVolume, VolumeStep,
                FormatVolume, SongControlSetting.Lead);

        foreach (var voice in leads.Select(t => t.Voice).OfType<string>().Distinct(StringComparer.Ordinal))
            yield return Control(voice, $"Lead vocal for {voice}, as a percentage",
                Values.Voices.GetValueOrDefault(voice, Values.Lead), AudioMix.MinVolume, AudioMix.MaxVolume, VolumeStep,
                FormatVolume, SongControlSetting.Voice, voice);
    }

    private SongControl Control(
        string label, string ariaLabel, int value, int min, int max, int step,
        Func<int, string> format, SongControlSetting setting, string? voice = null)
        => new(this, label, ariaLabel, value, min, max, step, format,
            v => MoveAsync(Input, new SongControlChange(setting, v, voice)),
            v => MoveAsync(Committed, new SongControlChange(setting, v, voice)));

    private Task MoveAsync(EventCallback<SongControlChange> callback, SongControlChange change)
    {
        Values.Apply(change);

        return callback.InvokeAsync(change);
    }

    /// <param name="Receiver">The list, which re-renders after each callback. Named rather than
    /// taken from the delegate: a lambda closing over a voice targets its closure, not the list.</param>
    private sealed record SongControl(
        object Receiver,
        string Label,
        string AriaLabel,
        int Value,
        int Min,
        int Max,
        int Step,
        Func<int, string> Format,
        Func<int, Task> Track,
        Func<int, Task> Commit)
    {
        public EventCallback<int> OnInput => EventCallback.Factory.Create<int>(Receiver, Track);

        public EventCallback<int> OnCommit => EventCallback.Factory.Create<int>(Receiver, Commit);
    }

    private static string FormatVolume(int volume) =>
        volume.ToString(CultureInfo.InvariantCulture) + "%";
}
