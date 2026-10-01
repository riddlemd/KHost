using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>Sets how a waiting turn will be sung — key, tempo and levels — with the same controls the
/// playing song uses.</summary>
/// <remarks>Edits a copy and hands back <see cref="PerformanceSettings"/>; the caller saves them
/// through <see cref="IPerformanceService.UpdateSettingsAsync"/>, which holds each to its range.</remarks>
public partial class EditPerformanceSettingsDialog
{
    private const string _rootClassName = "kh-edit-performance-settings-dialog";

    [Inject] private IAudioTrackService AudioTracks { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IPlaybackService PlaybackService { get; set; } = default!;

    private SongControlValues _values = new();
    private IReadOnlyList<AudioTrack> _tracks = [];
    private int _shownBacking;

    /// <summary>Set when the turn was loaded for playback while this was open.</summary>
    private bool _loaded;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Performance? Performance { get; set; }

    /// <summary>The song, for its title and to find which voices it ships apart.</summary>
    [Parameter] public Media? Media { get; set; }

    [Parameter] public EventCallback<PerformanceSettings> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    // DialogHost keys every dialog by request id, so this runs once per open with the turn bound.
    protected override async Task OnInitializedAsync()
    {
        if (Performance is not { } performance) return;

        // What LoadAsync would start the song at, so the editor opens on what the room would hear.
        _shownBacking = AudioLevels.ClampVolume(performance.BackingVolume ?? AppSettings.Current.BackingVocalVolume);
        _values = new SongControlValues
        {
            Pitch = performance.Pitch,
            Tempo = performance.Tempo,
            Lead = AudioLevels.ClampVolume(performance.LeadVolume),
            Backing = _shownBacking,
        };

        if (string.IsNullOrWhiteSpace(Media?.FilePath)) return;

        // A file still downloading probes as nothing, which leaves key and tempo, as it should.
        _tracks = await AudioTracks.ReadTracksAsync(Media.FilePath) ?? [];

        foreach (var voice in _tracks.Where(t => t.Role == AudioTrackRole.Lead).Select(t => t.Voice).OfType<string>())
            _values.Voices[voice] = AudioLevels.ClampVolume(
                performance.VoiceVolumes?.GetValueOrDefault(voice, AudioMix.DefaultLeadVolume) ?? AudioMix.DefaultLeadVolume);
    }

    private async Task SaveAsync()
    {
        if (Performance is not { } performance)
        {
            await CloseAsync();
            return;
        }

        // Playback holds the loaded turn's values and writes them over the row on its next change,
        // so a save here would be heard by nobody and then undone.
        if (PlaybackService.CurrentPerformance?.Id == performance.Id)
        {
            _loaded = true;
            return;
        }

        // Left null when nobody moved it, so the turn keeps following the machine setting.
        int? backing = performance.BackingVolume is null && _values.Backing == _shownBacking
            ? null
            : _values.Backing;

        var settings = new PerformanceSettings(_values.Pitch, _values.Tempo, _values.Lead, backing)
        {
            VoiceVolumes = _values.Voices.Count == 0 ? null : new Dictionary<string, int>(_values.Voices),
        };

        // DialogHost closes after awaiting this itself; closing here too would report a cancel.
        await OnSave.InvokeAsync(settings);
    }

    private async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(
            Performance performance, Media? media, Func<PerformanceSettings, Task> onSave,
            Action? onCancel, Action? onClose)
            : base(onClose)
        {
            Performance = performance;
            Media = media;
            OnSave = onSave;
            OnCancel = onCancel;
        }

        public Performance Performance { get; }
        public Media? Media { get; }

        /// <summary>A Task, not an Action: an async void failure never reaches the error boundary.</summary>
        public Func<PerformanceSettings, Task> OnSave { get; }
        public Action? OnCancel { get; }
    }
}
