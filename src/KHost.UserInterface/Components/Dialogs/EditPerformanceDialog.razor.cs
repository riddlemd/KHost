using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using KHost.Common.Visualisations;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>Edits one turn — the name it is announced under, the key, tempo and levels it is sung
/// at and, while it is still waiting, what is drawn under its words — whether it is still waiting
/// or already in the singer's history.</summary>
/// <remarks>Hands back a <see cref="PerformanceEdit"/> carrying only what moved; the caller saves it
/// with <see cref="PerformanceEdit.SaveAsync"/>.</remarks>
public partial class EditPerformanceDialog
{
    private const string _rootClassName = "kh-edit-performance-dialog";

    [Inject] private IAudioTrackService AudioTracks { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IPlaybackService PlaybackService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IVisualiserPresetService VisualiserPresets { get; set; } = default!;

    private SongControlValues _values = new();
    private SongControlValues _opened = new();
    private IReadOnlyList<AudioTrack> _tracks = [];
    private AliasModel _alias = new();
    private EditContext _editContext = new(new AliasModel());

    /// <summary>Off for a venue never asked, matching where the queue shows an alias at all.</summary>
    private bool _allowAliases;

    /// <summary>The turn is loaded for playback: playback resolved its name at load and holds its
    /// values, writing them back over the row on its next change, so nothing here may be saved.</summary>
    private bool _loaded;

    /// <summary>Still waiting to be sung: only a queued turn has a background left to draw.</summary>
    private bool _queued;

    private BackgroundChoice _backgroundChoice;

    /// <summary>The look "A look" draws, kept while another choice is shown so switching back
    /// finds it as it was left.</summary>
    private PerformanceBackground _look = new() { Type = PerformanceBackgroundType.Look };

    private IReadOnlyList<VisualiserPreset> _presets = [];

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Performance? Performance { get; set; }

    /// <summary>The song, for its title and to find which voices it ships apart.</summary>
    [Parameter] public Media? Media { get; set; }

    /// <summary>The singer's own name, which is what a blank alias falls back to.</summary>
    [Parameter] public string? SingerName { get; set; }

    [Parameter] public EventCallback<PerformanceEdit> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private string _fallbackName =>
        string.IsNullOrWhiteSpace(SingerName) ? "the singer’s own name" : SingerName!;

    // DialogHost keys every dialog by request id, so this runs once per open with the turn bound.
    protected override async Task OnInitializedAsync()
    {
        _allowAliases = (await VenuesService.ReadSelectedVenueAsync())?.Settings.AllowAliases ?? false;

        if (Performance is not { } performance) return;

        _loaded = IsLoaded(performance);
        _queued = performance.QueuePosition is not null;
        if (_queued) OpenBackground(performance.Background);
        _alias = new AliasModel { SungAs = performance.SungAs };
        _editContext = new EditContext(_alias);

        // What LoadAsync would start the song at, so the editor opens on what the room would hear.
        _values = new SongControlValues
        {
            Pitch = performance.Pitch,
            Tempo = performance.Tempo,
            Lead = AudioLevels.ClampVolume(performance.LeadVolume),
            Backing = AudioLevels.ClampVolume(performance.BackingVolume ?? AppSettings.Current.BackingVocalVolume),
        };

        if (!string.IsNullOrWhiteSpace(Media?.FilePath))
        {
            // A file still downloading probes as nothing, which leaves key and tempo, as it should.
            _tracks = await AudioTracks.ReadTracksAsync(Media.FilePath) ?? [];

            foreach (var voice in _tracks.Where(t => t.Role == AudioTrackRole.Lead).Select(t => t.Voice).OfType<string>())
                _values.Voices[voice] = AudioLevels.ClampVolume(
                    performance.VoiceVolumes?.GetValueOrDefault(voice, AudioMix.DefaultLeadVolume) ?? AudioMix.DefaultLeadVolume);
        }

        _opened = Copy(_values);
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (Performance is not { } performance)
        {
            await CloseAsync();
            return;
        }

        // Loaded while the editor was open: the same reasons as at open, found later.
        if (IsLoaded(performance))
        {
            _loaded = true;
            return;
        }

        var edit = new PerformanceEdit { Settings = ChangedSettings(performance) };

        if (_queued && ChosenBackground() is var background && !SameBackground(background, performance.Background))
            edit = edit with { BackgroundChanged = true, Background = background };

        // Blank is stored as null, not "": every reader takes an empty name as "use the singer's
        // own", and null is the answer a row that was never given one carries.
        var typed = Normalise(_alias.SungAs);
        if (_allowAliases && typed != Normalise(performance.SungAs))
            edit = edit with { SungAsChanged = true, SungAs = typed };

        // DialogHost closes after awaiting this itself; closing here too would report a cancel.
        await OnSave.InvokeAsync(edit);
    }

    /// <summary>Null when no control moved, so a rename alone leaves the levels row untouched.</summary>
    private PerformanceSettings? ChangedSettings(Performance performance)
    {
        var moved = _values.Pitch != _opened.Pitch
            || _values.Tempo != _opened.Tempo
            || _values.Lead != _opened.Lead
            || _values.Backing != _opened.Backing
            || _values.Voices.Any(v => _opened.Voices.GetValueOrDefault(v.Key) != v.Value);

        if (!moved) return null;

        // Left null when nobody moved it, so the turn keeps following the machine setting.
        int? backing = performance.BackingVolume is null && _values.Backing == _opened.Backing
            ? null
            : _values.Backing;

        return new PerformanceSettings(_values.Pitch, _values.Tempo, _values.Lead, backing)
        {
            VoiceVolumes = _values.Voices.Count == 0 ? null : new Dictionary<string, int>(_values.Voices),
        };
    }

    private void OpenBackground(PerformanceBackground? background)
    {
        _presets = VisualiserPresets.ReadAll();
        _backgroundChoice = background?.Type switch
        {
            PerformanceBackgroundType.Black => BackgroundChoice.Black,
            PerformanceBackgroundType.Look => BackgroundChoice.Look,
            _ => BackgroundChoice.Venue,
        };

        if (background is { Type: PerformanceBackgroundType.Look })
            background.CopyWithinRangesTo(_look);
        // Starts on the first preset, as a new playlist entry does.
        else if (_presets.FirstOrDefault() is { } preset)
            (_look.PresetSource, _look.PresetName) = (preset.Source, preset.Name);
    }

    private void ChooseBackground(ChangeEventArgs e)
    {
        if (Enum.TryParse<BackgroundChoice>(e.Value?.ToString(), out var choice) && Enum.IsDefined(choice))
            _backgroundChoice = choice;
    }

    private PerformanceBackground? ChosenBackground() => _backgroundChoice switch
    {
        BackgroundChoice.Black => new PerformanceBackground { Type = PerformanceBackgroundType.Black },
        BackgroundChoice.Look => VisualisationLooks.BackgroundWithinRanges(_look),
        _ => null,
    };

    /// <summary>Compared as stored, so reopening and saving an unchanged look writes nothing.</summary>
    private static bool SameBackground(PerformanceBackground? a, PerformanceBackground? b)
        => JsonSerializer.Serialize(VisualisationLooks.BackgroundWithinRanges(a)) == JsonSerializer.Serialize(VisualisationLooks.BackgroundWithinRanges(b));

    private bool IsLoaded(Performance performance) => PlaybackService.CurrentPerformance?.Id == performance.Id;

    private static string? Normalise(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private static SongControlValues Copy(SongControlValues values) => new()
    {
        Pitch = values.Pitch,
        Tempo = values.Tempo,
        Lead = values.Lead,
        Backing = values.Backing,
        Voices = new Dictionary<string, int>(values.Voices),
    };

    private async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    private enum BackgroundChoice { Venue, Black, Look }

    private sealed class AliasModel
    {
        /// <summary>Matched to the column, which the migration cut at 255.</summary>
        [MaxLength(255, ErrorMessage = "That name is too long.")]
        public string? SungAs { get; set; }
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(
            Performance performance, Media? media, string? singerName, Func<PerformanceEdit, Task> onSave,
            Action? onCancel, Action? onClose)
            : base(onClose)
        {
            Performance = performance;
            Media = media;
            SingerName = singerName;
            OnSave = onSave;
            OnCancel = onCancel;
        }

        public Performance Performance { get; }
        public Media? Media { get; }
        public string? SingerName { get; }

        /// <summary>A Task, not an Action: an async void failure never reaches the error boundary.</summary>
        public Func<PerformanceEdit, Task> OnSave { get; }
        public Action? OnCancel { get; }
    }
}
