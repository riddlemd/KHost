using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class BulkEditMediaDialog
{
    private const string _rootClassName = "kh-media-bulk-edit-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public IReadOnlyList<Media>? Items { get; set; }

    [Parameter] public EventCallback<BulkEditMediaModel> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private BulkEditMediaModel _model = new();
    private EditContext _editContext = default!;

    // DialogHost keys every dialog by request id, so a fresh instance (and _model) is created per open.
    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        // DialogHost closes after awaiting this itself; closing again here would also fire
        // OnClose's onCancel, marking a successful save as a cancel.
        await OnSave.InvokeAsync(_model);
    }

    public async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel, Action? onClose) : base(onClose)
        {
            Items = items;
            OnSave = onSave;
            OnCancel = onCancel;
        }

        public IReadOnlyList<Media> Items { get; }
        public Func<BulkEditMediaModel, Task> OnSave { get; }
        public Action? OnCancel { get; }
    }
}
