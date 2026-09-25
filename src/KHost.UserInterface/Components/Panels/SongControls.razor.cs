using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components.Panels;

public partial class SongControls : IDisposable
{
    /// <summary>Ten steps to either end, where one percent a step would be fifty.</summary>
    private const int TempoStep = 5;

    /// <summary>Coarser than tempo: a level is judged by ear, not read off a number.</summary>
    private const int VolumeStep = 5;

    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private IAppSettingsService? AppSettings { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    // The readout follows the thumb, but the service only hears the value the host let go of: a
    // drag emits dozens, and each one is a database write, an announcement and a settle restarted.
    private int _pitch;
    private int _tempo;
    private int _lead;
    private int _backing;
    private Dictionary<string, int> _voices = [];

    // The volumes are deliberately not part of this. Lead sits at zero and backing at the house
    // setting on every song, so counting them would leave the trigger marked all night.
    private bool IsChanged => _pitch != 0 || _tempo != 0;

    private SongControlStyle Style => AppSettings?.Current.SongControlStyle ?? SongControlStyle.Sliders;

    /// <summary>The panel as data: one list rendered twice, not copies that could drift apart.</summary>
    private IEnumerable<SongControl> Controls()
    {
        yield return new SongControl(this, "Key", "Key, in semitones from the recording",
            _pitch, IPlaybackService.MinPitch, IPlaybackService.MaxPitch, 1,
            FormatPitch, v => _pitch = v, CommitPitchAsync);

        yield return new SongControl(this, "Tempo", "Tempo, as a percentage of the recording",
            _tempo, IPlaybackService.MinTempo, IPlaybackService.MaxTempo, TempoStep,
            FormatTempo, v => _tempo = v, CommitTempoAsync);

        // Only a file that ships its voices apart has anything here to balance, and the music
        // never gets a fader: it is the reference the voices are set against. Laid out as KaraFun
        // does — backing, then a fader per singer's lead — so a host moving from it finds them.
        if (HasTrack(AudioTrackRole.Backing))
            yield return new SongControl(this, "Backing Vocals", "Backing vocal volume, as a percentage",
                _backing, AudioMix.MinVolume, AudioMix.MaxVolume, VolumeStep,
                FormatVolume, v => _backing = v, CommitBackingAsync);

        var leads = PlaybackService?.AudioTracks.Where(t => t.Role == AudioTrackRole.Lead).ToList() ?? [];

        if (leads.Any(t => t.Voice is null))
            yield return new SongControl(this, "Lead Vocal", "Lead vocal volume, as a percentage",
                _lead, AudioMix.MinVolume, AudioMix.MaxVolume, VolumeStep,
                FormatVolume, v => _lead = v, CommitLeadAsync);

        foreach (var voice in leads.Select(t => t.Voice).OfType<string>().Distinct(StringComparer.Ordinal))
            yield return new SongControl(this, voice, $"Lead vocal for {voice}, as a percentage",
                _voices.GetValueOrDefault(voice, _lead), AudioMix.MinVolume, AudioMix.MaxVolume, VolumeStep,
                FormatVolume, v => _voices[voice] = v, v => CommitVoiceAsync(voice, v));
    }

    /// <param name="Receiver">The panel, which re-renders after each callback. Named rather than
    /// taken from the delegate: a lambda closing over a voice targets its closure, not the panel.</param>
    private sealed record SongControl(
        object Receiver,
        string Label,
        string AriaLabel,
        int Value,
        int Min,
        int Max,
        int Step,
        Func<int, string> Format,
        Action<int> Track,
        Func<int, Task> Commit)
    {
        /// <summary>Moves the readout with the drag; the service hears nothing yet.</summary>
        public EventCallback<int> OnInput => EventCallback.Factory.Create<int>(Receiver, Track);

        public EventCallback<int> OnCommit => EventCallback.Factory.Create<int>(Receiver, Commit);
    }

    private bool HasTrack(AudioTrackRole role) =>
        PlaybackService?.AudioTracks.Any(t => t.Role == role) ?? false;

    /// <summary>Says so on the closed trigger, or a transposed song is invisible until it plays.</summary>
    private string TriggerTitle => IsChanged
        ? $"Key {FormatPitch(_pitch)}, tempo {FormatTempo(_tempo)}"
        : "Key and tempo";

    protected override void OnInitialized()
    {
        SyncFromService();

        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(_ => InvokeAsync(() =>
        {
            SyncFromService();
            StateHasChanged();
        })));
    }

    private void SyncFromService()
    {
        _pitch = PlaybackService?.Pitch ?? 0;
        _tempo = PlaybackService?.Tempo ?? 0;
        _lead = PlaybackService?.LeadVolume ?? AudioMix.DefaultLeadVolume;
        _backing = PlaybackService?.BackingVolume ?? AudioMix.DefaultBackingVolume;
        _voices = PlaybackService?.VoiceVolumes is { } voices ? new Dictionary<string, int>(voices) : [];
    }

    private bool _open;

    // Opening re-reads rather than trusting what the panel was left holding: a drag abandoned
    // without releasing commits nothing and announces nothing, so only this puts the thumb back.
    private void Toggle()
    {
        _open = !_open;

        if (_open) SyncFromService();
    }

    private void Close() => _open = false;

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") Close();
    }

    private Task CommitPitchAsync(int value)
    {
        _pitch = value;

        return PlaybackService?.SetPitchAsync(value) ?? Task.CompletedTask;
    }

    private Task CommitTempoAsync(int value)
    {
        _tempo = value;

        return PlaybackService?.SetTempoAsync(value) ?? Task.CompletedTask;
    }

    private Task CommitLeadAsync(int value)
    {
        _lead = value;

        return PlaybackService?.SetLeadVolumeAsync(value) ?? Task.CompletedTask;
    }

    private Task CommitVoiceAsync(string voice, int value)
    {
        _voices[voice] = value;

        return PlaybackService?.SetVoiceVolumeAsync(voice, value) ?? Task.CompletedTask;
    }

    private Task CommitBackingAsync(int value)
    {
        _backing = value;

        return PlaybackService?.SetBackingVolumeAsync(value) ?? Task.CompletedTask;
    }

    private static string FormatVolume(int volume) =>
        volume.ToString(CultureInfo.InvariantCulture) + "%";

    private static string FormatPitch(int semitones) =>
        semitones.ToString("+#;−#;0", CultureInfo.InvariantCulture);

    private static string FormatTempo(int tempo) =>
        tempo.ToString("+#;−#;0", CultureInfo.InvariantCulture) + "%";

    public void Dispose() => _subscriptions.Dispose();
}
