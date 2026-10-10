using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Visualisations;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using System.Text.Json;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>Visualisation playlists, their entries and how each is drawn, and the presets a host
/// imported. A playlist's edits are held until Save; adding or deleting a playlist or preset acts at once.</summary>
public partial class VisualisationsManagerPage : IDisposable
{
    [Inject] private IVisualisationPlaylistService Playlists { get; set; } = default!;
    [Inject] private IVisualiserPresetService Presets { get; set; } = default!;
    [Inject] private IVenuesService Venues { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();
    private IDisposable? _navigationGuard;

    // Set while leaving on the host's answer, so the guard does not stop the navigation it asked for.
    private bool _leaving;

    private List<VisualisationPlaylist> _playlists = [];
    private IReadOnlyList<VisualiserPreset> _presets = [];
    private Guid? _selectedId;
    private int _selectedEntry = -1;
    private Guid? _activePlaylistId;
    private bool _saving;

    /// <summary>The selected playlist as edited, a copy of the stored one until Save writes it back.</summary>
    private VisualisationPlaylist? _draft;

    /// <summary>The library rows video entries name, by id; a row since gone is absent.</summary>
    private Dictionary<Guid, Media> _videos = [];

    /// <summary>As stored: the list, its counts and the delete check read this, never the draft.</summary>
    private VisualisationPlaylist? Selected => _playlists.FirstOrDefault(p => p.Id == _selectedId);

    private VisualisationEntry? SelectedEntry
        => _draft is { } playlist && _selectedEntry >= 0 && _selectedEntry < playlist.Entries.Count
            ? playlist.Entries[_selectedEntry]
            : null;

    /// <summary>Compared with what is stored, so an edit put back reads as not dirty.</summary>
    private bool HasUnsavedChanges
        => _draft is not null && Selected is { } stored && Fingerprint(_draft) != Fingerprint(stored);

    private bool CanSave => HasUnsavedChanges && !_saving && !string.IsNullOrWhiteSpace(_draft?.Name);

    private IReadOnlyList<VisualiserPreset> Imported
        => [.. _presets.Where(p => p.Source == VisualiserPresetSource.Imported)];

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<VisualisationPlaylistsChanged>(_ => OnChanged()));
        _subscriptions.Add(Broker.Subscribe<VisualiserPresetsChanged>(_ => OnChanged()));

        // The "In use" badge follows the venue, wherever it is edited.
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => OnChanged()));
        _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(_ => OnChanged()));

        // Registered before the first await: a navigation asked for while loading must still be guarded.
        _navigationGuard = Navigation.RegisterLocationChangingHandler(OnLocationChangingAsync);

        await RefreshAsync();

        Open(_playlists.FirstOrDefault()?.Id);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _navigationGuard?.Dispose();

        GC.SuppressFinalize(this);
    }

    private void OnChanged()
        => _ = InvokeAsync(async () =>
        {
            await RefreshAsync();
            StateHasChanged();
        });

    /// <summary>Re-reads the store. A draft with edits is kept, so another page's change, or this
    /// page's own add or import, never throws away what the host has not saved yet.</summary>
    private async Task RefreshAsync()
    {
        var dirty = HasUnsavedChanges;

        _playlists = [.. await Playlists.ReadAllWithEntriesAsync()];
        _presets = Presets.ReadAll();
        _activePlaylistId = (await Venues.ReadSelectedVenueAsync())?.Settings.VisualisationPlaylistId;
        await ReadVideosAsync();

        if (Selected is null) Open(_playlists.FirstOrDefault()?.Id);
        else if (!dirty) _draft = Copy(Selected);

        if (SelectedEntry is null)
            _selectedEntry = _draft is { Entries.Count: > 0 } d ? Math.Min(Math.Max(_selectedEntry, 0), d.Entries.Count - 1) : -1;
    }

    /// <summary>Selects a playlist and starts a fresh draft of it, dropping any edits to the last.</summary>
    private void Open(Guid? id)
    {
        _selectedId = id;
        _draft = Selected is { } stored ? Copy(stored) : null;
        _selectedEntry = _draft is { Entries.Count: > 0 } ? 0 : -1;
    }

    /// <summary>Runs <paramref name="then"/> once unsaved edits are saved or discarded, or at once with none.</summary>
    private Task LeaveDraftAsync(Func<Task> then)
    {
        if (!HasUnsavedChanges) return then();

        return Dialogs.ShowUnsavedChangesAsync(
            onSave: async () =>
            {
                // A refused save keeps the host on the draft, with the reason flashed.
                if (await SaveAsync()) await then();
            },
            onDiscard: then,
            message: $"{_draft!.Name} has changes that have not been saved yet.");
    }

    private async ValueTask OnLocationChangingAsync(LocationChangingContext context)
    {
        if (_leaving || !HasUnsavedChanges) return;

        // Held rather than cancelled: the host has not chosen yet, and the target has to survive
        // long enough to be navigated to once they do.
        context.PreventNavigation();

        var target = context.TargetLocation;
        await LeaveDraftAsync(() =>
        {
            _leaving = true;
            Navigation.NavigateTo(target);
            return Task.CompletedTask;
        });
    }

    // --- playlists ---

    private Task AddPlaylistAsync() => LeaveDraftAsync(async () =>
    {
        var name = "New playlist";
        for (var n = 2; _playlists.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"New playlist {n}";

        var created = await Playlists.CreateAsync(new VisualisationPlaylist { Name = name });

        await RefreshAsync();
        Open(created.Id);
        StateHasChanged();
    });

    private Task SelectPlaylistAsync(Guid id)
    {
        if (_selectedId == id) return Task.CompletedTask;

        return LeaveDraftAsync(() =>
        {
            Open(id);
            StateHasChanged();
            return Task.CompletedTask;
        });
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

    /// <summary>Writes the draft back: the playlist's own row only if it changed, then its entries.</summary>
    /// <returns>False when nothing was saved, with the reason flashed.</returns>
    private async Task<bool> SaveAsync()
    {
        if (_draft is not { } draft || Selected is not { } stored) return false;

        var name = draft.Name.Trim();

        // An empty name would leave a row nobody can pick out.
        if (name.Length == 0)
        {
            Flash.Show("A playlist needs a name.", FlashType.Warning);
            return false;
        }

        draft.Name = name;
        _saving = true;

        try
        {
            if (draft.Name != stored.Name || draft.Shuffle != stored.Shuffle)
                await Playlists.UpdateAsync(Header(draft));

            if (!await Playlists.ReplaceEntriesAsync(draft.Id, draft.Entries))
            {
                Flash.Show("That playlist is gone, so the change was not saved.", FlashType.Warning);
                return false;
            }
        }
        finally
        {
            _saving = false;
        }

        await RefreshAsync();
        Reopen();

        return true;
    }

    private void Revert() => Reopen();

    /// <summary>A fresh draft of the same playlist, keeping the entry the editor had open.</summary>
    private void Reopen()
    {
        var entry = _selectedEntry;
        Open(_selectedId);

        if (entry >= 0 && _draft is { Entries.Count: > 0 } draft) _selectedEntry = Math.Min(entry, draft.Entries.Count - 1);
    }

    /// <summary>The playlist's own row, without its entries: an update saves only the playlist,
    /// and entries go through <see cref="IVisualisationPlaylistService.ReplaceEntriesAsync"/>.</summary>
    private static VisualisationPlaylist Header(VisualisationPlaylist playlist) => new()
    {
        Id = playlist.Id,
        Name = playlist.Name,
        NameFolded = playlist.NameFolded,
        Shuffle = playlist.Shuffle,
    };

    /// <summary>A deep copy: the editor changes entries in place, and the stored list must not move with it.</summary>
    private static VisualisationPlaylist Copy(VisualisationPlaylist playlist)
        => JsonSerializer.Deserialize<VisualisationPlaylist>(JsonSerializer.Serialize(playlist))!;

    private static string Fingerprint(VisualisationPlaylist playlist) => JsonSerializer.Serialize(playlist);

    // --- entries ---

    private void SelectEntry(int index) => _selectedEntry = index;

    private void AddEntry()
    {
        // Starts on the first preset and opens in the editor below, where its preset is chosen:
        // a second picker beside the button read as the editor's own.
        if (_draft is not { } playlist || _presets.FirstOrDefault() is not { } preset) return;

        playlist.Entries.Add(new VisualisationEntry { PresetSource = preset.Source, PresetName = preset.Name });
        _selectedEntry = playlist.Entries.Count - 1;
    }

    private void MoveEntry(int index, int by)
    {
        if (_draft is not { } playlist) return;

        var to = index + by;
        if (to < 0 || to >= playlist.Entries.Count) return;

        (playlist.Entries[index], playlist.Entries[to]) = (playlist.Entries[to], playlist.Entries[index]);
        if (_selectedEntry == index) _selectedEntry = to;
        else if (_selectedEntry == to) _selectedEntry = index;
    }

    private void RemoveEntry(int index)
    {
        if (_draft is not { } playlist || index < 0 || index >= playlist.Entries.Count) return;

        playlist.Entries.RemoveAt(index);
        if (_selectedEntry >= playlist.Entries.Count) _selectedEntry = playlist.Entries.Count - 1;
    }

    /// <summary>The editor changed the selected entry in place; a newly picked video is read for its
    /// title, so the list names it before it is saved.</summary>
    private async Task LookChangedAsync(IVisualisationLook look)
    {
        if (look.VideoMediaId is { } videoId && !_videos.ContainsKey(videoId))
            await ReadVideosAsync();
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
    /// <remarks>The draft too: a video picked but not yet saved is named in the list all the same.</remarks>
    private async Task ReadVideosAsync()
    {
        var ids = _playlists.Append(_draft).OfType<VisualisationPlaylist>().SelectMany(p => p.Entries).Select(e => e.VideoMediaId).OfType<Guid>().Distinct();
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
