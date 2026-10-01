using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Visualisations;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

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
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<VisualisationPlaylist> _playlists = [];
    private IReadOnlyList<VisualiserPreset> _presets = [];
    private Guid? _selectedId;
    private int _selectedEntry = -1;
    private Guid? _activePlaylistId;

    private ElementReference _preview;

    /// <summary>What the preview was last told, so a render that changed nothing sends nothing.</summary>
    private string? _previewSent;

    private VisualisationPlaylist? Selected => _playlists.FirstOrDefault(p => p.Id == _selectedId);

    private VisualisationEntry? SelectedEntry
        => Selected is { } playlist && _selectedEntry >= 0 && _selectedEntry < playlist.Entries.Count
            ? playlist.Entries[_selectedEntry]
            : null;

    private IReadOnlyList<VisualiserPreset> Imported
        => [.. _presets.Where(p => p.Source == VisualiserPresetSource.Imported)];

    /// <summary>One string per preset for a select's value; the source first, since a name is
    /// unique only within its source.</summary>
    internal static string PresetKey(VisualiserPresetSource source, string name) => $"{(int)source}:{name}";

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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (SelectedEntry is not { } entry)
        {
            _previewSent = null;
            return;
        }

        var message = new
        {
            presetName = entry.PresetSource == VisualiserPresetSource.Bundled ? entry.PresetName : null,
            presetUrl = entry.PresetSource == VisualiserPresetSource.Imported ? ImportedUrl(entry.PresetName) : null,
            builtIn = entry.PresetSource == VisualiserPresetSource.BuiltIn ? entry.PresetName : null,
            barCount = entry.BarCount,
            colourScheme = entry.ColourScheme.ToString().ToLowerInvariant(),
            colour = entry.Colour,
            brightness = entry.Brightness,
            saturation = entry.Saturation,
            sensitivity = entry.Sensitivity,
        };

        var sent = System.Text.Json.JsonSerializer.Serialize(message);
        if (sent == _previewSent) return;
        _previewSent = sent;

        try { await JS.InvokeVoidAsync("khVisualiserPreview.show", _preview, message); }
        catch (JSDisconnectedException) { /* the circuit is going; nothing to preview on */ }
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

    private async Task SetPresetAsync(ChangeEventArgs e)
    {
        if (Selected is not { } playlist || SelectedEntry is not { } entry || ParseKey(e.Value?.ToString()) is not { } preset) return;

        entry.PresetSource = preset.Source;
        entry.PresetName = preset.Name;

        await SaveEntriesAsync(playlist);
    }

    /// <summary>A slider moving: shown in the preview at once, saved when it is let go.</summary>
    private void Adjust(Setting setting, object? value)
    {
        if (SelectedEntry is { } entry && int.TryParse(value?.ToString(), out var percent))
            Apply(entry, setting, percent);
    }

    private async Task CommitAsync(Setting setting, object? value)
    {
        if (Selected is not { } playlist || SelectedEntry is not { } entry || !int.TryParse(value?.ToString(), out var percent)) return;

        Apply(entry, setting, percent);
        await SaveEntriesAsync(playlist);
    }

    private async Task SetBarCountAsync(ChangeEventArgs e)
    {
        if (Selected is not { } playlist || SelectedEntry is not { } entry || !int.TryParse(e.Value?.ToString(), out var count)) return;

        entry.BarCount = count;
        await SaveEntriesAsync(playlist);
    }

    private async Task SetColourSchemeAsync(ChangeEventArgs e)
    {
        if (Selected is not { } playlist || SelectedEntry is not { } entry
            || !Enum.TryParse<VisualiserColourScheme>(e.Value?.ToString(), out var scheme) || !Enum.IsDefined(scheme)) return;

        entry.ColourScheme = scheme;
        await SaveEntriesAsync(playlist);
    }

    private async Task SetColourAsync(ChangeEventArgs e)
    {
        if (Selected is not { } playlist || SelectedEntry is not { } entry || e.Value?.ToString() is not { } colour) return;

        entry.Colour = colour;
        await SaveEntriesAsync(playlist);
    }

    private static void Apply(VisualisationEntry entry, Setting setting, int percent)
    {
        switch (setting)
        {
            case Setting.Brightness:
                entry.Brightness = Math.Clamp(percent, VisualisationEntry.MinBrightness, VisualisationEntry.MaxBrightness);
                break;
            case Setting.Saturation:
                entry.Saturation = Math.Clamp(percent, VisualisationEntry.MinSaturation, VisualisationEntry.MaxSaturation);
                break;
            case Setting.Sensitivity:
                entry.Sensitivity = Math.Clamp(percent, VisualisationEntry.MinSensitivity, VisualisationEntry.MaxSensitivity);
                break;
        }
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

    private bool IsAvailable(VisualisationEntry entry)
        => _presets.Any(p => p.Source == entry.PresetSource && p.Name == entry.PresetName);

    private static string PresetKey(VisualisationEntry entry) => PresetKey(entry.PresetSource, entry.PresetName);

    private static (VisualiserPresetSource Source, string Name)? ParseKey(string? key)
    {
        var colon = key?.IndexOf(':') ?? -1;
        if (key is null || colon <= 0 || !int.TryParse(key[..colon], out var source) || !Enum.IsDefined((VisualiserPresetSource)source))
            return null;

        return ((VisualiserPresetSource)source, key[(colon + 1)..]);
    }

    private string DescribePreset(VisualisationEntry entry)
        => _presets.FirstOrDefault(p => p.Source == entry.PresetSource && p.Name == entry.PresetName) is { } preset
            ? preset.Title ?? preset.Name
            : $"{entry.PresetName} (not there any more)";

    private static string DescribeLook(VisualisationEntry entry)
        => $"Brightness {entry.Brightness}% · Colour {entry.Saturation}% · Sensitivity {entry.Sensitivity}%"
           + (entry.PresetSource != VisualiserPresetSource.BuiltIn ? ""
               : (HasBars(entry) ? $" · {entry.BarCount} bars" : "") + entry.ColourScheme switch
               {
                   VisualiserColourScheme.Theme => " · Accent colour",
                   VisualiserColourScheme.Single => $" · {entry.Colour}",
                   _ => " · Classic colours",
               });

    /// <summary>What the classic palette is for this entry: a calm scene's mix of colours, a retro
    /// effect's own look, or a meter's green to red.</summary>
    private static string ClassicWording(VisualisationEntry entry)
        => VisualiserPresetService.IsAmbient(entry.PresetSource, entry.PresetName) ? "Classic, a soft mix of colours"
           : VisualiserPresetService.IsRetro(entry.PresetSource, entry.PresetName) ? "Classic, the effect's own colours"
           : "Classic, green to red";

    /// <summary>Whether the entry draws bars, and so has a bar count to choose.</summary>
    private static bool HasBars(VisualisationEntry entry)
        => entry.PresetSource == VisualiserPresetSource.BuiltIn && entry.PresetName is "spectrum-bars" or "mirrored-bars";

    /// <summary>The imported preset for the preview, versioned so a re-import reloads it.</summary>
    private string? ImportedUrl(string name)
        => _presets.FirstOrDefault(p => p.Source == VisualiserPresetSource.Imported && p.Name == name) is { } preset
            ? $"{VisualiserPresetService.RoutePrefix.TrimStart('/')}{Uri.EscapeDataString(name)}?v={preset.ImportedUtc?.Ticks ?? 0}"
            : null;

    private enum Setting { Brightness, Saturation, Sensitivity }
}
