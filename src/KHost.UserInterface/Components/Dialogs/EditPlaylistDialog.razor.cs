using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using KHost.Common.Media;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditPlaylistDialog
{
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;

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

    /// <summary>The media behind the entries already in the list, so a row can name what it plays,
    /// answer for its own format, and show what it will run for before it is overridden.</summary>
    private readonly Dictionary<Guid, Media> _media = [];

    /// <summary>One row of the picker: different things, same choice, a combo box binds one type.</summary>
    /// <param name="Group">Its heading in the menu. The caller groups by sorting; the box never reorders.</param>
    internal sealed record AddChoice(string Label, string Group, Media? Media, MediaPool? Pool);

    private bool _addOpen;
    private bool _addJustOpened;
    private AddChoice? _addChoice;
    private string _addText = "";
    private ComboBox<AddChoice>? _addPicker;

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

            ResetAdd();

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

    /// <summary>Rows already in the playlist: pickers search the library, no list held here.</summary>
    private async Task LoadEntryTitlesAsync()
    {
        _media.Clear();

        foreach (var id in _entries.SelectMany(e => new[] { e.MediaId, e.AudioMediaId })
                     .OfType<Guid>().Distinct())
        {
            if (await Media.ReadAsync(id) is { } media)
                _media[id] = media;
        }
    }

    /// <summary>Artist as well as title: search covers both, so an artist-only match reads fine.</summary>
    private static string Describe(Media media)
        => string.IsNullOrWhiteSpace(media.Artist) ? media.Title : $"{media.Title} - {media.Artist}";

    private string TitleFor(Guid id) => _media.TryGetValue(id, out var media) ? media.Title : "(missing)";

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

    /// <summary>Never the karaoke library: those are backing tracks with no singer on them.</summary>
    private async Task<IReadOnlyList<Media>> SearchMediaAsync(string term)
    {
        MediaType[] types = Purpose == PoolPurpose.Ads
            ? [MediaType.Video, MediaType.Audio, MediaType.Image]
            : [MediaType.Audio];

        // Capped: the box is for finding one row, and a thousand of them help nobody.
        var page = await Media.SearchAsync(term, 1, 50, sort: null, new MediaSearchOptions { Types = types });

        return page.Items;
    }

    private void SetEntryWeight(int index, string? value)
        => _entries[index].Weight = int.TryParse(value, out var weight) && weight >= 0 ? weight : 1;

    /// <summary>Blank hands the entry back to the default, which is the point of showing it as one.</summary>
    private void SetEntryDuration(int index, string? value)
        => _entries[index].Duration = double.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>Shown as the placeholder, not the rule: mirrors what AdService resolves.</summary>
    private string DescribeDefaultDuration(MediaPoolEntry entry)
    {
        // A video answers for itself.
        if (entry.MediaId is { } visualId
            && _media.TryGetValue(visualId, out var visual)
            && !MediaFormats.IsImage(visual.Format)
            && visual.Duration is { } visualLength)
        {
            return Seconds(visualLength);
        }

        // A still with a voiceover runs to the end of the voiceover, so the two finish together.
        if (entry.AudioMediaId is { } audioId
            && _media.TryGetValue(audioId, out var audio)
            && audio.Duration is { } audioLength)
        {
            return Seconds(audioLength - (entry.AudioStart ?? TimeSpan.Zero));
        }

        return $"{AppSettings.Current.AdDefaultDurationSeconds:0.#}";
    }

    private static string Seconds(TimeSpan length) => $"{length.TotalSeconds:0.#}";

    /// <summary>Focused a render late: the box does not exist until the one that revealed it.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_addJustOpened || _addPicker is null)
            return;

        _addJustOpened = false;

        await _addPicker.FocusAsync();
    }

    private void OpenAdd()
    {
        _addOpen = true;
        _addJustOpened = true;
    }

    /// <summary>Media first, then playlists: the box heads on group change, never reorders.</summary>
    private async Task<IReadOnlyList<AddChoice>> SearchAddChoicesAsync(string term)
    {
        var media = await SearchMediaAsync(term);

        var choices = media
            .Select(row => new AddChoice(Describe(row), "Media", row, null))
            .ToList();

        choices.AddRange(_poolChoices
            .Where(pool => pool.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(pool => new AddChoice(pool.Name, "Playlists", null, pool)));

        return choices;
    }

    /// <summary>Choosing a row only fills the field; a wrong pick can be retyped, not deleted.</summary>
    private void AddChosenEntry()
    {
        if (_addChoice is not { } choice)
            return;

        if (choice.Media is { } media)
        {
            _media[media.Id] = media;

            _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = media.Id });
        }
        else if (choice.Pool is { } pool)
        {
            _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), ChildPoolId = pool.Id });
        }

        // Collapsed again: the row it made is the confirmation, and the dialog goes back to being
        // mostly the list it is for.
        ResetAdd();
    }

    /// <summary>Position is overwritten by index just before save, so a new entry needs none here.</summary>
    private void ResetAdd()
    {
        _addOpen = false;
        _addChoice = null;
        _addText = "";
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
