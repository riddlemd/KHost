using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Visualisations;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>Visualisation playlists, their entries and how each is drawn, and the presets a host
/// imported. Every change is saved as it is made: there is no Save to forget.</summary>
public partial class VisualisationsManagerPage : IDisposable
{
    [Inject] private IVisualisationPlaylistService Playlists { get; set; } = default!;
    [Inject] private IVisualiserPresetService Presets { get; set; } = default!;
    [Inject] private IVenuesService Venues { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IMediaService Media { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<VisualisationPlaylist> _playlists = [];
    private IReadOnlyList<VisualiserPreset> _presets = [];
    private Guid? _selectedId;
    private int _selectedEntry = -1;
    private Guid? _activePlaylistId;

    /// <summary>The library rows video entries name, by id; a row since gone is absent.</summary>
    private Dictionary<Guid, Media> _videos = [];

    private VisualisationPlaylist? Selected => _playlists.FirstOrDefault(p => p.Id == _selectedId);

    private VisualisationEntry? SelectedEntry
        => Selected is { } playlist && _selectedEntry >= 0 && _selectedEntry < playlist.Entries.Count
            ? playlist.Entries[_selectedEntry]
            : null;

    private IReadOnlyList<VisualiserPreset> Imported
        => [.. _presets.Where(p => p.Source == VisualiserPresetSource.Imported)];

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<VisualisationPlaylistsChanged>(_ => OnChanged()));
        _subscriptions.Add(Broker.Subscribe<VisualiserPresetsChanged>(_ => OnChanged()));

        // The "In use" badge follows the venue, wherever it is edited.
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => OnChanged()));
        _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(_ => OnChanged()));

        await RefreshAsync();

        _selectedId = _playlists.FirstOrDefault()?.Id;
        _selectedEntry = Selected is { Entries.Count: > 0 } ? 0 : -1;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        GC.SuppressFinalize(this);
    }

    private void OnChanged()
        => _ = InvokeAsync(async () =>
        {
            await RefreshAsync();
            StateHasChanged();
        });

    private async Task RefreshAsync()
    {
        _playlists = [.. await Playlists.ReadAllWithEntriesAsync()];
        _presets = Presets.ReadAll();
        _activePlaylistId = (await Venues.ReadSelectedVenueAsync())?.Settings.VisualisationPlaylistId;
        await ReadVideosAsync();

        if (_selectedId is not null && Selected is null) _selectedId = _playlists.FirstOrDefault()?.Id;
        if (SelectedEntry is null) _selectedEntry = Selected is { Entries.Count: > 0 } s ? Math.Min(Math.Max(_selectedEntry, 0), s.Entries.Count - 1) : -1;
    }

    // --- playlists ---

    private async Task AddPlaylistAsync()
    {
        var name = "New playlist";
        for (var n = 2; _playlists.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"New playlist {n}";

        var created = await Playlists.CreateAsync(new VisualisationPlaylist { Name = name });

        await RefreshAsync();
        _selectedId = created.Id;
        _selectedEntry = -1;
    }

    private void SelectPlaylist(Guid id)
    {
        if (_selectedId == id) return;

        _selectedId = id;
        _selectedEntry = Selected is { Entries.Count: > 0 } ? 0 : -1;
    }

    private async Task RenamePlaylistAsync(ChangeEventArgs e)
    {
        if (Selected is not { } playlist) return;

        var name = e.Value?.ToString()?.Trim();

        // An empty name would leave a row nobody can pick out; the field shows the old one again.
        if (string.IsNullOrEmpty(name) || name == playlist.Name) return;

        playlist.Name = name;
        await Playlists.UpdateAsync(Header(playlist));
    }

    private async Task SetShuffleAsync(ChangeEventArgs e)
    {
        if (Selected is not { } playlist) return;

        playlist.Shuffle = e.Value is true || string.Equals(e.Value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        await Playlists.UpdateAsync(Header(playlist));
    }

    private async Task StartDeletePlaylistAsync(VisualisationPlaylist playlist)
    {
        await Dialogs.ShowConfirmationAsync(
            $"Delete <span class=\"kh-emphasis\">{System.Net.WebUtility.HtmlEncode(playlist.Name)}</span>? Any venue using it shows black under the words.",
            async () =>
            {
                await Playlists.DeleteAsync(playlist.Id);
                await RefreshAsync();
            },
            "Delete Playlist",
            "Delete");
    }

    /// <summary>The playlist's own row, without its entries: an update saves only the playlist,
    /// and entries go through <see cref="SaveEntriesAsync"/>.</summary>
    private static VisualisationPlaylist Header(VisualisationPlaylist playlist) => new()
    {
        Id = playlist.Id,
        Name = playlist.Name,
        NameFolded = playlist.NameFolded,
        Shuffle = playlist.Shuffle,
    };

    // --- entries ---

    private void SelectEntry(int index) => _selectedEntry = index;

    private async Task AddEntryAsync()
    {
        // Starts on the first preset and opens in the editor below, where its preset is chosen:
        // a second picker beside the button read as the editor's own.
        if (Selected is not { } playlist || _presets.FirstOrDefault() is not { } preset) return;

        playlist.Entries.Add(new VisualisationEntry { PresetSource = preset.Source, PresetName = preset.Name });
        _selectedEntry = playlist.Entries.Count - 1;

        await SaveEntriesAsync(playlist);
    }

    private async Task MoveEntryAsync(int index, int by)
    {
        if (Selected is not { } playlist) return;

        var to = index + by;
        if (to < 0 || to >= playlist.Entries.Count) return;

        (playlist.Entries[index], playlist.Entries[to]) = (playlist.Entries[to], playlist.Entries[index]);
        if (_selectedEntry == index) _selectedEntry = to;
        else if (_selectedEntry == to) _selectedEntry = index;

        await SaveEntriesAsync(playlist);
    }

    private async Task RemoveEntryAsync(int index)
    {
        if (Selected is not { } playlist || index < 0 || index >= playlist.Entries.Count) return;

        playlist.Entries.RemoveAt(index);
        if (_selectedEntry >= playlist.Entries.Count) _selectedEntry = playlist.Entries.Count - 1;

        await SaveEntriesAsync(playlist);
    }

    /// <summary>The editor changed the selected entry in place; a newly picked video is read for its
    /// title first, so the list names it.</summary>
    private async Task SaveLookAsync(IVisualisationLook look)
    {
        if (Selected is not { } playlist) return;

        if (look.VideoMediaId is { } videoId && !_videos.ContainsKey(videoId)) await ReadVideosAsync();
        await SaveEntriesAsync(playlist);
    }

    private async Task SaveEntriesAsync(VisualisationPlaylist playlist)
    {
        if (!await Playlists.ReplaceEntriesAsync(playlist.Id, playlist.Entries))
            Flash.Show("That playlist is gone, so the change was not saved.", FlashType.Warning);
    }

    // --- presets ---

    private async Task ImportAsync(InputFileChangeEventArgs e)
    {
        foreach (var file in e.GetMultipleFiles(maximumFileCount: 50))
        {
            VisualiserPresetImport result;
            try
            {
                // One byte past the limit, so the service is the one that says a file is too large.
                await using var stream = file.OpenReadStream(VisualiserPresetService.MaxBytes + 1);
                result = await Presets.ImportAsync(file.Name, stream);
            }
            catch (IOException)
            {
                result = new VisualiserPresetImport { Error = $"That file is over {VisualiserPresetService.MaxBytes / 1024} KB, far larger than any preset." };
            }

            if (result.Preset is { } preset)
                Flash.Show(result.Replaced ? $"Replaced the preset {preset.Name}." : $"Imported the preset {preset.Name}.");
            else
                Flash.Show($"{file.Name}: {result.Error}", FlashType.Warning);
        }

        await RefreshAsync();
    }

    private async Task StartDeletePresetAsync(VisualiserPreset preset)
    {
        var used = _playlists.Count(p => p.Entries.Any(entry => entry.PresetSource == VisualiserPresetSource.Imported && entry.PresetName == preset.Name));

        await Dialogs.ShowConfirmationAsync(
            $"Delete the preset <span class=\"kh-emphasis\">{System.Net.WebUtility.HtmlEncode(preset.Name)}</span>?"
                + (used > 0 ? $" {used} playlist(s) use it, and those entries will show black." : ""),
            async () =>
            {
                Presets.DeleteImported(preset.Name);
                await RefreshAsync();
            },
            "Delete Preset",
            "Delete");
    }

    /// <summary>Read by id rather than every video in the library: a list names only a few.</summary>
    private async Task ReadVideosAsync()
    {
        var ids = _playlists.SelectMany(p => p.Entries).Select(e => e.VideoMediaId).OfType<Guid>().Distinct();
        var videos = new Dictionary<Guid, Media>();
        foreach (var id in ids)
            if (await Media.ReadAsync(id) is { } row) videos[id] = row;

        _videos = videos;
    }

    private string DescribePreset(VisualisationEntry entry)
        => entry.PresetSource == VisualiserPresetSource.Video
            ? entry.VideoMediaId is not { } id ? "No video picked"
              : _videos.TryGetValue(id, out var video) ? video.Title
              : "Video not found"
            : _presets.FirstOrDefault(p => p.Source == entry.PresetSource && p.Name == entry.PresetName) is { } preset
            ? preset.Title ?? preset.Name
            : $"{entry.PresetName} (not there any more)";

    private static string DescribeLook(VisualisationEntry entry)
        => $"Brightness {entry.Brightness}% · Colour {entry.Saturation}%"
           // A video plays as it is, deaf to the song.
           + (entry.PresetSource == VisualiserPresetSource.Video ? "" : $" · Sensitivity {entry.Sensitivity}%")
           + (entry.PresetSource != VisualiserPresetSource.BuiltIn ? ""
               : (VisualisationLookEditor.HasBars(entry) ? $" · {entry.BarCount} bars" : "") + entry.ColourScheme switch
               {
                   VisualiserColourScheme.Theme => " · Accent colour",
                   VisualiserColourScheme.Single => $" · {entry.Colour}",
                   _ => " · Classic colours",
               });
}
