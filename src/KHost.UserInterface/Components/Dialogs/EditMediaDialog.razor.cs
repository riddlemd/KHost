using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditMediaDialog
{
    private const string _rootClassName = "kh-media-edit-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Media? Media { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback<Media> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private EditMediaModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    protected override void OnParametersSet()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _model = Media is null
                ? new EditMediaModel()
                : new EditMediaModel
                {
                    Id = Media.Id,
                    Title = Media.Title,
                    Artist = Media.Artist,
                    Notes = Media.Notes,
                    Status = Media.Status,
                    ImageScaling = Media.ImageScaling
                };
            _editContext = new EditContext(_model);
        }
        _prevIsOpen = IsOpen;
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (Media is not null)
        {
            Media.Title = _model.Title;
            Media.Artist = _model.Artist;
            Media.Notes = _model.Notes;
            Media.Status = _model.Status;
            Media.ImageScaling = _model.ImageScaling;

            await OnSave.InvokeAsync(Media);
        }

        await CloseAsync();
    }

    // Applied on Save like the rest of the form, so Cancel backs it out.
    private void ToggleBroken()
        => _model.Status = _model.Status == MediaStatus.Broken ? MediaStatus.Ready : MediaStatus.Broken;

    private void SwapTitleAndArtist()
    {
        (_model.Title, _model.Artist) = (_model.Artist, _model.Title);
    }

    public async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    public record DialogRequest : EditDialogRequest<Media>
    {
        public DialogRequest(Media? value, Func<Media?, Task> onSave, Action? onCancel, Action? onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
