using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
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
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    // The readout follows the thumb, but the service only hears the value the host let go of: a
    // drag emits dozens, and each one is a database write, an announcement and a settle restarted.
    private int _pitch;
    private int _tempo;
    private int _lead;
    private int _backing;

    // The volumes are deliberately not part of this. Lead sits at zero and backing at the house
    // setting on every song, so counting them would leave the trigger marked all night.
    private bool IsChanged => _pitch != 0 || _tempo != 0;

    private bool HasTrack(AudioTrackRole role) =>
        PlaybackService?.AudioTracks.Any(t => t.Role == role) ?? false;

    /// <summary>Says so on the closed trigger, or a song left transposed is invisible until it plays.</summary>
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

    private void OnPitchInput(ChangeEventArgs e) => _pitch = Parse(e, _pitch);

    private void OnTempoInput(ChangeEventArgs e) => _tempo = Parse(e, _tempo);

    // Both commits read the event rather than the field the readout follows. A change can arrive
    // without an input before it, and then the field still holds the value the drag started from.
    private Task CommitPitchAsync(ChangeEventArgs e)
    {
        _pitch = Parse(e, _pitch);

        return PlaybackService?.SetPitchAsync(_pitch) ?? Task.CompletedTask;
    }

    private Task CommitTempoAsync(ChangeEventArgs e)
    {
        _tempo = Parse(e, _tempo);

        return PlaybackService?.SetTempoAsync(_tempo) ?? Task.CompletedTask;
    }

    private static int Parse(ChangeEventArgs e, int fallback) =>
        int.TryParse(e.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private void OnLeadInput(ChangeEventArgs e) => _lead = Parse(e, _lead);

    private void OnBackingInput(ChangeEventArgs e) => _backing = Parse(e, _backing);

    private Task CommitLeadAsync(ChangeEventArgs e)
    {
        _lead = Parse(e, _lead);

        return PlaybackService?.SetLeadVolumeAsync(_lead) ?? Task.CompletedTask;
    }

    private Task CommitBackingAsync(ChangeEventArgs e)
    {
        _backing = Parse(e, _backing);

        return PlaybackService?.SetBackingVolumeAsync(_backing) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Paints the track from the middle of a control's travel out to the thumb — left in red,
    /// right in green, nothing at all at rest. The thumb inset is not optional: its centre only
    /// travels between half a thumb from each end, so a raw percentage runs ahead of it.
    /// </summary>
    private static string Fill(int value, int min, int max)
    {
        var span = max - min == 0 ? 1 : max - min;

        double Fraction(double at) => (at - min) / span;

        // Zero is where all four rest, and the same expression paints both kinds: it is the centre
        // of the key and tempo travel, and the left edge of a volume that cannot go negative.
        var (from, to, colour) = value.CompareTo(0) switch
        {
            0 => (Fraction(0), Fraction(0), "transparent"),
            > 0 => (Fraction(0), Fraction(value), "var(--kh-success)"),
            _ => (Fraction(value), Fraction(0), "var(--kh-danger)"),
        };

        return FormattableString.Invariant($"--from-frac:{from:F4};--to-frac:{to:F4};--fill:{colour}");
    }

    private static string SignClass(int value) => value switch
    {
        0 => "",
        > 0 => "kh-song-controls__value--up",
        _ => "kh-song-controls__value--down",
    };

    private static string FormatVolume(int volume) =>
        volume.ToString(CultureInfo.InvariantCulture) + "%";

    private static string FormatPitch(int semitones) =>
        semitones.ToString("+#;−#;0", CultureInfo.InvariantCulture);

    private static string FormatTempo(int tempo) =>
        tempo.ToString("+#;−#;0", CultureInfo.InvariantCulture) + "%";

    public void Dispose() => _subscriptions.Dispose();
}
