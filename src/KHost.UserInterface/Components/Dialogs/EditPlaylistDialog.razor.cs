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

    /// <summary>Set by the page: a playlist belongs to the manager it was created from.</summary>
    [Parameter] public PoolPurpose Purpose { get; set; }
    [Parameter] public EventCallback<MediaPool> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private bool _isNew;
    private bool _prevIsOpen;

    private Guid _id = Guid.NewGuid();
    private string _name = "";
    private PoolSelectionMode _selectionMode = PoolSelectionMode.Shuffle;
    private int _noRepeatCount = 3;
    private AdTriggerMode _adTrigger = AdTriggerMode.HostOnly;
    private int _adTriggerInterval = 4;

    private List<MediaPoolEntry> _entries = [];
    private IReadOnlyList<MediaPool> _poolChoices = [];

    /// <summary>Titles for the entries already in the list, so a row can name what it plays.</summary>
    private readonly Dictionary<Guid, string> _titles = [];

    private Media? _addMedia;
    private string _addMediaText = "";
    private string _addPoolId = "";

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _isNew = Pool is null;

            _id = Pool?.Id ?? Guid.NewGuid();
            _name = Pool?.Name ?? "";
            _selectionMode = Pool?.SelectionMode ?? PoolSelectionMode.Shuffle;
            _noRepeatCount = Pool?.NoRepeatCount ?? 3;
            _adTrigger = Pool?.AdTrigger ?? AdTriggerMode.HostOnly;
            _adTriggerInterval = Pool?.AdTriggerInterval ?? 4;

            // Copied rather than bound: Cancel has to leave the stored playlist untouched.
            _entries = [.. (Pool?.Entries ?? []).OrderBy(e => e.Position).Select(Copy)];

            _addMedia = null;
            _addMediaText = "";
            _addPoolId = "";

            await LoadChoicesAsync();
        }

        _prevIsOpen = IsOpen;
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
        await LoadEntryTitlesAsync();

        var pools = await MediaPools.ReadAllWithEntriesAsync(Purpose, venueId: null);

        // A playlist is never offered itself. Deeper loops are refused on save, which is the only
        // place the whole shape is known.
        _poolChoices = [.. pools.Where(p => p.Id != _id).OrderBy(p => p.Name)];
    }

    /// <summary>
    /// Only the rows already in the playlist, read by id. The pickers search the library instead
    /// of holding it, so there is no in-memory list to look a title up in.
    /// </summary>
    private async Task LoadEntryTitlesAsync()
    {
        _titles.Clear();

        foreach (var id in _entries.SelectMany(e => new[] { e.MediaId, e.AudioMediaId })
                     .OfType<Guid>().Distinct())
        {
            if (await Media.ReadAsync(id) is { } media)
                _titles[id] = media.Title;
        }
    }

    private string TitleFor(Guid id) => _titles.GetValueOrDefault(id, "(missing)");

    private string DescribeEntry(MediaPoolEntry entry)
    {
        if (entry.ChildPoolId is { } poolId)
            return $"Playlist: {_poolChoices.FirstOrDefault(p => p.Id == poolId)?.Name ?? "(missing)"}";

        if (entry.MediaId is { } mediaId)
            return TitleFor(mediaId);

        return entry.AudioMediaId is { } audioId
            ? $"Sound only: {TitleFor(audioId)}"
            : "(empty)";
    }

    /// <summary>
    /// What this playlist can use: an ad is a video, a sound, or a still; break music is a record.
    /// Never the karaoke library — those are backing tracks with no singer on them.
    /// </summary>
    private Task<IReadOnlyList<Media>> SearchMediaAsync(string term) => SearchAsync(term,
        Purpose == PoolPurpose.Ads
            ? [MediaType.Video, MediaType.Audio, MediaType.Image]
            : [MediaType.Audio]);

    private async Task<IReadOnlyList<Media>> SearchAsync(string term, MediaType[] types)
    {
        // Capped: the box is for finding one row, and a thousand of them help nobody.
        var page = await Media.SearchAsync(term, 1, 50, sort: null, new MediaSearchOptions { Types = types });

        return page.Items;
    }

    private void SetEntryWeight(int index, string? value)
        => _entries[index].Weight = int.TryParse(value, out var weight) && weight >= 0 ? weight : 1;

    private void AddMediaEntry()
    {
        if (_addMedia is not { } media)
            return;

        _titles[media.Id] = media.Title;
        _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = media.Id, Position = _entries.Count });

        _addMedia = null;
        _addMediaText = "";
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
            Purpose = Purpose,
            SelectionMode = _selectionMode,
            NoRepeatCount = Math.Clamp(_noRepeatCount, 0, 50),
            AdTrigger = _adTrigger,
            AdTriggerInterval = Math.Max(_adTriggerInterval, 1),
            Entries = _entries,
        });
    }
}
