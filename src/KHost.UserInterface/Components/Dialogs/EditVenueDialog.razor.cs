using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditVenueDialog
{
    private const string _rootClassName = "kh-venue-edit-dialog";

    private static readonly int[] DuplicateWindowOptions = [1, 2, 4, 8, 12];

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Venue? Venue { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback<Venue> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnOpen { get; set; }

    [Inject] private IMediaService Media { get; set; } = default!;

    private IReadOnlyList<Media> _images = [];

    private bool _isNew;
    private EditVenueModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;
    private bool _rotationDialogOpen;

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _isNew = Venue is null;
            _model = Venue is null
                ? new EditVenueModel()
                : new EditVenueModel
                {
                    Id = Venue.Id,
                    Name = Venue.Name,
                    Notes = Venue.Notes,
                    Enabled = Venue.Enabled,
                    DefaultVolume = Venue.Settings.DefaultVolume,
                    OnScreenDisconnect = Venue.Settings.OnScreenDisconnect,
                    ShowEstimatedWaitTime = Venue.Settings.ShowEstimatedWaitTime,
                    TippingEnabled = Venue.Settings.TippingEnabled,
                    WarnOnDuplicateSong = Venue.Settings.WarnOnDuplicateSong,
                    // Venues saved before this setting existed read back 0, which is not an option.
                    DuplicateSongWindowHours = DuplicateWindowOptions.Contains(Venue.Settings.DuplicateSongWindowHours)
                        ? Venue.Settings.DuplicateSongWindowHours
                        : 4,
                    PromptBeforeRemovingSinger = Venue.Settings.PromptBeforeRemovingSinger,
                    PromptBeforeRemovingPerformance = Venue.Settings.PromptBeforeRemovingPerformance,
                    ClearQueueOnClose = Venue.Settings.ClearQueueOnClose,
                    // Clone so Cancel discards rotation edits along with the rest of the model.
                    QueueRotation = Venue.Settings.QueueRotation?.Clone() ?? new(),
                    BreakMusicPoolId = Venue.Settings.BreakMusicPoolId,
                    AdPoolId = Venue.Settings.AdPoolId,
                    BrandingImageMediaId = Venue.Settings.BrandingImageMediaId,
                    BreakMusicProvider = Venue.Settings.BreakMusicProvider,

                };
            _editContext = new EditContext(_model);

            await LoadChoicesAsync();
        }
        _prevIsOpen = IsOpen;
    }

    /// <summary>
    /// Read when the dialog opens rather than held: a playlist added on the manager page while
    /// this venue was last edited would otherwise be missing from the list.
    /// </summary>
    private async Task LoadChoicesAsync()
    {

        // Stills only: anything else handed to the screen as a card is a URL that serves nothing.
        // Read by type rather than paged, or a card past the first page would never be offered.
        _images = await Media.ReadAllByTypesAsync(MediaType.Image);
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        var venue = Venue ?? new Venue { Id = _model.Id, Name = _model.Name };
        venue.Name = _model.Name;
        venue.Notes = _model.Notes;
        venue.Enabled = _model.Enabled;
        venue.Settings.DefaultVolume = _model.DefaultVolume;
        venue.Settings.OnScreenDisconnect = _model.OnScreenDisconnect;
        venue.Settings.ShowEstimatedWaitTime = _model.ShowEstimatedWaitTime;
        venue.Settings.TippingEnabled = _model.TippingEnabled;
        venue.Settings.WarnOnDuplicateSong = _model.WarnOnDuplicateSong;
        venue.Settings.DuplicateSongWindowHours = _model.DuplicateSongWindowHours;
        venue.Settings.PromptBeforeRemovingSinger = _model.PromptBeforeRemovingSinger;
        venue.Settings.PromptBeforeRemovingPerformance = _model.PromptBeforeRemovingPerformance;
        venue.Settings.ClearQueueOnClose = _model.ClearQueueOnClose;
        venue.Settings.QueueRotation = _model.QueueRotation;
        // Round-tripped rather than edited here: the Break Music and Ads managers own these, and
        // saving a venue from this dialog must not wipe what they set.
        venue.Settings.BreakMusicPoolId = _model.BreakMusicPoolId;
        venue.Settings.AdPoolId = _model.AdPoolId;
        venue.Settings.BrandingImageMediaId = _model.BrandingImageMediaId;
        venue.Settings.BreakMusicProvider = _model.BreakMusicProvider;

        await OnSave.InvokeAsync(venue);

        await CloseAsync();
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    private void CloseRotationDialog() => _rotationDialogOpen = false;

    private string GetClassString()
        => $"kh-singer-edit-dialog {Class?.Trim()}".Trim();

    public record DialogRequest : EditDialogRequest<Venue>
    {
        public DialogRequest(Venue? value, Func<Venue?, Task> onSave, Action? onCancel, Action onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
