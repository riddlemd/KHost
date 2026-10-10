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
    [Inject] private IPlaybackService PlaybackService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    // The readout follows the thumb, but the service only hears the value the host let go of: a
    // drag emits dozens, and each one is a database write, an announcement and a settle restarted.
    private SongControlValues _values = new();

    // The volumes are deliberately not part of this. Lead sits at zero and backing at the house
    // setting on every song, so counting them would leave the trigger marked all night.
    private bool IsChanged => _values.Pitch != 0 || _values.Tempo != 0;

    private SongControlStyle Style => AppSettings.Current.SongControlStyle;

    /// <summary>Says so on the closed trigger, or a transposed song is invisible until it plays.</summary>
    private string TriggerTitle => IsChanged
        ? $"Performance Settings: key {SongAdjustmentDisplay.FormatPitch(_values.Pitch)}, tempo {SongAdjustmentDisplay.FormatTempo(_values.Tempo)}"
        : "Performance Settings";

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
        _values = new SongControlValues
        {
            Pitch = PlaybackService.Pitch,
            Tempo = PlaybackService.Tempo,
            Lead = PlaybackService.LeadVolume,
            Backing = PlaybackService.BackingVolume,
            Voices = new Dictionary<string, int>(PlaybackService.VoiceVolumes),
        };
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

    /// <summary>Bound so the trigger's marking re-renders with the drag; the list holds the value.</summary>
    private void OnInput(SongControlChange change) { }

    private Task CommitAsync(SongControlChange change) => change.Setting switch
    {
        SongControlSetting.Pitch => PlaybackService.SetPitchAsync(change.Value),
        SongControlSetting.Tempo => PlaybackService.SetTempoAsync(change.Value),
        SongControlSetting.Lead => PlaybackService.SetLeadVolumeAsync(change.Value),
        SongControlSetting.Backing => PlaybackService.SetBackingVolumeAsync(change.Value),
        SongControlSetting.Voice when change.Voice is not null => PlaybackService.SetVoiceVolumeAsync(change.Voice, change.Value),
        _ => Task.CompletedTask,
    };

    public void Dispose() => _subscriptions.Dispose();
}
