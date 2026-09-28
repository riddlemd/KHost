using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditVenueDialog
{
    private const string _rootClassName = "kh-venue-edit-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Venue? Venue { get; set; }

    [Parameter] public EventCallback<Venue> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    // Only for the fallback on a new venue's break music mode; every other break-music, QR,
    // marquee and queue-rotation concern lives on the section component that draws it.
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;
    [Inject] private IBackgroundPackService BackgroundPacks { get; set; } = default!;

    private bool _isNew;
    private EditVenueModel _model = new();
    private EditContext _editContext = default!;

    // DialogHost keys every dialog by request id, so a fresh instance is created per open; this
    // runs exactly once with Venue already bound.
    protected override async Task OnInitializedAsync()
    {
        _isNew = Venue is null;
        _model = EditVenueModel.From(Venue, BreakMusic.ActiveProvider?.SourceName);
        _editContext = new EditContext(_model);

        await ReloadBackgroundPackAsync();
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private BackgroundPack _backgroundPack = new();

    /// <summary>Read when the dialog opens: the folders are machine settings, not this venue's.
    /// </summary>
    private async Task ReloadBackgroundPackAsync()
        => _backgroundPack = await BackgroundPacks.ReadAsync();

    /// <summary>A stable DOM id for a background's checkbox.</summary>
    /// <remarks>Derived from the file name, which may hold spaces and dots — both legal in an id
    /// attribute but not in the selector a test or a stylesheet would reach it with.</remarks>
    private static string BackgroundInputId(BackgroundPackEntry entry)
        => "venue-background-" + string.Concat(entry.File.Select(c => char.IsLetterOrDigit(c) ? c : '-'));

    private bool IsBackgroundEnabled(BackgroundPackEntry entry)
        => _model.SongBackgrounds.Contains(entry.File, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ticking a background adds it to the pool a song is picked from.</summary>
    /// <remarks>Names of clips no longer in the folder are left alone rather than tidied away: a
    /// folder that is temporarily unreachable would otherwise silently empty a venue's choices, and
    /// a name nothing matches costs nothing at render time.</remarks>
    private void ToggleBackground(BackgroundPackEntry entry, bool enabled)
    {
        _model.SongBackgrounds.RemoveAll(name => string.Equals(name, entry.File, StringComparison.OrdinalIgnoreCase));

        if (enabled) _model.SongBackgrounds.Add(entry.File);
    }

    /// <summary>The still is asked for by name, never by path — the browser cannot reach the
    /// folder, and does not need to know where it is.</summary>
    private static string BackgroundStillUrl(BackgroundPackEntry entry)
        => $"/venue/background-still?file={Uri.EscapeDataString(entry.File)}";

    /// <summary>What the venue is about to get, said back to them.</summary>
    private string BackgroundSummary()
    {
        var enabled = _backgroundPack.Entries.Count(IsBackgroundEnabled);

        return enabled switch
        {
            0 => "Songs render on plain black.",
            1 => $"Every song uses {_backgroundPack.Entries.First(IsBackgroundEnabled).Name}.",
            _ => $"A different one of these {enabled} for each song, picked as it renders.",
        };
    }

    private async Task SaveAsync()
    {
        // Applied to a copy, never to Venue itself: a save the caller ends up refusing must leave
        // nothing half-edited on the instance the rest of the app is still showing.
        var venue = Venue is null
            ? new Venue { Id = _model.Id, Name = _model.Name }
            : new Venue
            {
                Id = Venue.Id,
                Name = Venue.Name,
                NameFolded = Venue.NameFolded,
                Notes = Venue.Notes,
                Address = Venue.Address,
                Phone = Venue.Phone,
                Enabled = Venue.Enabled,
                Settings = Venue.Settings.Clone(),
            };

        _model.ApplyTo(venue);

        // DialogHost closes after awaiting this itself; closing again here would also fire
        // OnClose's onCancel, marking a successful save as a cancel.
        await OnSave.InvokeAsync(venue);
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    public record DialogRequest : EditDialogRequest<Venue>
    {
        public DialogRequest(Venue? value, Func<Venue?, Task> onSave, Action? onCancel, Action onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
