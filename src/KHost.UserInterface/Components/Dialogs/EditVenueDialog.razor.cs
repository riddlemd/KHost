using KHost.Abstractions.Models;
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
    [Inject] private IVisualisationPlaylistService VisualisationPlaylists { get; set; } = default!;

    private bool _isNew;
    private EditVenueModel _model = new();
    private EditContext _editContext = default!;

    private IReadOnlyList<VisualisationPlaylist> _visualisationPlaylists = [];
    private VisualisationPlaylist? _visualisationPlaylist;
    private string _visualisationPlaylistText = "";

    // DialogHost keys every dialog by request id, so a fresh instance is created per open; this
    // runs exactly once with Venue already bound.
    protected override async Task OnInitializedAsync()
    {
        _isNew = Venue is null;
        _model = EditVenueModel.From(Venue, BreakMusic.ActiveProvider?.SourceName);
        _editContext = new EditContext(_model);

        // Read when the dialog opens, not held, since a new playlist would be missing.
        _visualisationPlaylists = await VisualisationPlaylists.ReadAllWithEntriesAsync();
        // The picker shows the chosen playlist's name itself, so the text needs no seeding.
        _visualisationPlaylist = _visualisationPlaylists.FirstOrDefault(p => p.Id == _model.VisualisationPlaylistId);
    }

    /// <summary>Clearing the field is how a venue goes back to black under the words.</summary>
    private void OnVisualisationPlaylistChanged(VisualisationPlaylist? playlist)
    {
        _visualisationPlaylist = playlist;
        _model.VisualisationPlaylistId = playlist?.Id;
    }

    private Task<IReadOnlyList<VisualisationPlaylist>> SearchVisualisationPlaylistsAsync(string term)
        => Task.FromResult<IReadOnlyList<VisualisationPlaylist>>(
            [.. _visualisationPlaylists.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))]);

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
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
