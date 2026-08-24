using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditPlaylistDialog
{
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IMediaService Media { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public MediaPool? Pool { get; set; }
    [Parameter] public EventCallback<MediaPool> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private bool _isNew;
    private bool _prevIsOpen;

    private Guid _id = Guid.NewGuid();
    private string _name = "";
    private PoolPurpose _purpose = PoolPurpose.BreakMusic;
    private PoolSelectionMode _selectionMode = PoolSelectionMode.Shuffle;
    private int _noRepeatCount = 3;
    private AdTriggerMode _adTrigger = AdTriggerMode.HostOnly;
    private int _adTriggerInterval = 4;

    private List<MediaPoolEntry> _entries = [];
    private IReadOnlyList<Media> _mediaChoices = [];
    private IReadOnlyList<Media> _audioChoices = [];
    private IReadOnlyList<MediaPool> _poolChoices = [];

    private string _addMediaId = "";
    private string _addPoolId = "";

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _isNew = Pool is null;

            _id = Pool?.Id ?? Guid.NewGuid();
            _name = Pool?.Name ?? "";
            _purpose = Pool?.Purpose ?? PoolPurpose.BreakMusic;
            _selectionMode = Pool?.SelectionMode ?? PoolSelectionMode.Shuffle;
            _noRepeatCount = Pool?.NoRepeatCount ?? 3;
            _adTrigger = Pool?.AdTrigger ?? AdTriggerMode.HostOnly;
            _adTriggerInterval = Pool?.AdTriggerInterval ?? 4;

            // Copied rather than bound: Cancel has to leave the stored playlist untouched.
            _entries = [.. (Pool?.Entries ?? []).OrderBy(e => e.Position).Select(Copy)];

            _addMediaId = "";
            _addPoolId = "";

            await LoadChoicesAsync();
        }

        _prevIsOpen = IsOpen;
    }

    /// <summary>
    /// Both lists are scoped to the kind, so switching from break music to ads has to fetch again:
    /// otherwise the picker keeps offering bed tracks and none of the venue's ads.
    /// </summary>
    private async Task OnPurposeChangedAsync(ChangeEventArgs e)
    {
        if (!Enum.TryParse<PoolPurpose>(e.Value?.ToString(), out var purpose) || purpose == _purpose)
            return;

        _purpose = purpose;

        // Entries came out of the other purpose's libraries, so they are not this playlist's to keep.
        _entries.Clear();
        _addMediaId = "";
        _addPoolId = "";

        await LoadChoicesAsync();
    }

    private static MediaPoolEntry Copy(MediaPoolEntry entry) => new()
    {
        Id = entry.Id,
        MediaPoolId = entry.MediaPoolId,
        Position = entry.Position,
        Weight = entry.Weight,
        MediaId = entry.MediaId,
        AudioMediaId = entry.AudioMediaId,
        AudioStart = entry.AudioStart,
        Duration = entry.Duration,
        ChildPoolId = entry.ChildPoolId,
    };

    private async Task LoadChoicesAsync()
    {
        // What each purpose can actually use: an ad is a video or a picture, break music is a
        // record. Unpaged, because a paged read filtered in memory drops everything past page one.
        _mediaChoices = _purpose == PoolPurpose.Ads
            ? await Media.ReadAllByKindsAsync(MediaKind.Video, MediaKind.Image)
            : await Media.ReadAllByKindsAsync(MediaKind.Audio);

        // Never the karaoke library: those are backing tracks with no singer on them, so they are
        // neither something to play between singers nor an ad's voiceover.
        _audioChoices = await Media.ReadAllByKindsAsync(MediaKind.Audio);

        var pools = await MediaPools.ReadAllWithEntriesAsync(_purpose, venueId: null);

        // A playlist is never offered itself. Deeper loops are refused on save, which is the only
        // place the whole shape is known.
        _poolChoices = [.. pools.Where(p => p.Id != _id).OrderBy(p => p.Name)];
    }

    private string DescribeEntry(MediaPoolEntry entry)
    {
        if (entry.ChildPoolId is { } poolId)
            return $"Playlist: {_poolChoices.FirstOrDefault(p => p.Id == poolId)?.Name ?? "(missing)"}";

        if (entry.MediaId is { } mediaId)
            return _mediaChoices.FirstOrDefault(m => m.Id == mediaId)?.Title
                ?? _audioChoices.FirstOrDefault(m => m.Id == mediaId)?.Title
                ?? "(missing)";

        return entry.AudioMediaId is { } audioId
            ? $"Sound only: {_audioChoices.FirstOrDefault(m => m.Id == audioId)?.Title ?? "(missing)"}"
            : "(empty)";
    }

    /// <summary>Blank rather than 00:00:00, so "not set" reads as not set.</summary>
    private static string FormatTime(TimeSpan? value)
        => value is { } time ? time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture) : "";

    /// <summary>Accepts m:ss and h:mm:ss, and plain seconds — a host typing "20" means 20 seconds.</summary>
    private static TimeSpan? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;

        var parts = text.Split(':');
        var total = TimeSpan.Zero;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return null;

            total = total * 60 + TimeSpan.FromSeconds(value);
        }

        return total > TimeSpan.Zero ? total : null;
    }

    private void SetEntryAudio(int index, string? value)
        => _entries[index].AudioMediaId = Guid.TryParse(value, out var id) ? id : null;

    private void SetEntryAudioStart(int index, string? value)
        => _entries[index].AudioStart = ParseTime(value);

    private void SetEntryDuration(int index, string? value)
        => _entries[index].Duration = ParseTime(value);

    private void SetEntryWeight(int index, string? value)
        => _entries[index].Weight = int.TryParse(value, out var weight) && weight >= 0 ? weight : 1;

    private void AddMediaEntry()
    {
        if (!Guid.TryParse(_addMediaId, out var mediaId))
            return;

        _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = mediaId, Position = _entries.Count });
        _addMediaId = "";
    }

    private void AddPoolEntry()
    {
        if (!Guid.TryParse(_addPoolId, out var poolId))
            return;

        _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), ChildPoolId = poolId, Position = _entries.Count });
        _addPoolId = "";
    }

    private void RemoveEntry(int index) => _entries.RemoveAt(index);

    private void MoveEntry(int index, int by)
    {
        var target = index + by;

        if (target < 0 || target >= _entries.Count)
            return;

        (_entries[index], _entries[target]) = (_entries[target], _entries[index]);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_name))
            return;

        for (var i = 0; i < _entries.Count; i++)
            _entries[i].Position = i;

        await OnSave.InvokeAsync(new MediaPool
        {
            Id = _id,
            Name = _name.Trim(),
            Purpose = _purpose,
            SelectionMode = _selectionMode,
            NoRepeatCount = Math.Clamp(_noRepeatCount, 0, 50),
            AdTrigger = _adTrigger,
            AdTriggerInterval = Math.Max(_adTriggerInterval, 1),
            Entries = _entries,
        });
    }
}
