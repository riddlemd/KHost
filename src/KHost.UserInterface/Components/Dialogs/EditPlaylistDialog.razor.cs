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

    /// <summary>Titles for the entries already in the list, so a row can name what it plays.</summary>
    private readonly Dictionary<Guid, string> _titles = [];

    /// <summary>Formats alongside them: only a video answers for its own length.</summary>
    private readonly Dictionary<Guid, string> _formats = [];

    /// <summary>Lengths too, so a row can show what it will run for before it is overridden.</summary>
    private readonly Dictionary<Guid, TimeSpan?> _durations = [];

    /// <summary>
    /// One row of the picker. Media and playlists are different things but the same choice, and a
    /// combo box binds one type — so they meet here rather than in two controls.
    /// </summary>
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


            _addOpen = false;
            _addChoice = null;
            _addText = "";

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
        _formats.Clear();
        _durations.Clear();

        foreach (var id in _entries.SelectMany(e => new[] { e.MediaId, e.AudioMediaId })
                     .OfType<Guid>().Distinct())
        {
            if (await Media.ReadAsync(id) is { } media)
            {
                _titles[id] = media.Title;
                _formats[id] = media.Format;
                _durations[id] = media.Duration;
            }
        }
    }

    /// <summary>
    /// Artist as well as title: the search covers both, so a row matched on its artist looks like
    /// a mistake when only the title is shown.
    /// </summary>
    private static string Describe(Media media)
        => string.IsNullOrWhiteSpace(media.Artist) ? media.Title : $"{media.Title} — {media.Artist}";

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

    /// <summary>Blank hands the entry back to the default, which is the point of showing it as one.</summary>
    private void SetEntryDuration(int index, string? value)
        => _entries[index].Duration = double.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>
    /// The seconds the entry will actually run for while its own length is blank, shown as the
    /// placeholder so a host sees the number they are inheriting rather than the rule behind it.
    /// Mirrors what AdService resolves, so the two cannot say different things.
    /// </summary>
    private string DescribeDefaultDuration(MediaPoolEntry entry)
    {
        // A video answers for itself.
        if (entry.MediaId is { } visualId
            && _formats.TryGetValue(visualId, out var format)
            && !MediaFormats.IsImage(format)
            && _durations.GetValueOrDefault(visualId) is { } visualLength)
        {
            return Seconds(visualLength);
        }

        // A still with a voiceover runs to the end of the voiceover, so the two finish together.
        if (entry.AudioMediaId is { } audioId
            && _durations.GetValueOrDefault(audioId) is { } audioLength)
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

    /// <summary>
    /// Media first, then playlists — the box draws a heading wherever the group changes and never
    /// reorders, so the order they are returned in is the order they are grouped in.
    /// </summary>
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

    /// <summary>
    /// Adding is its own press. Choosing a row only fills the field, so a host who picks the wrong
    /// one types again rather than deleting a line they did not mean to make.
    /// </summary>
    private void AddChosenEntry()
    {
        if (_addChoice is not { } choice)
            return;

        if (choice.Media is { } media)
        {
            _titles[media.Id] = media.Title;
            _formats[media.Id] = media.Format;
            _durations[media.Id] = media.Duration;

            _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = media.Id, Position = _entries.Count });
        }
        else if (choice.Pool is { } pool)
        {
            _entries.Add(new MediaPoolEntry { Id = Guid.NewGuid(), ChildPoolId = pool.Id, Position = _entries.Count });
        }

        // Collapsed again: the row it made is the confirmation, and the dialog goes back to being
        // mostly the list it is for.
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
