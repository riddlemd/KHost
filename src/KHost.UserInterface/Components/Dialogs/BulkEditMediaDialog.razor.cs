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
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback<BulkEditMediaModel> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private BulkEditMediaModel _model = new();
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
            _model = new BulkEditMediaModel();
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
        await OnSave.InvokeAsync(_model);
        await CloseAsync();
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
