using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditUserGroupDialog
{
    private const string _rootClassName = "kh-user-group-edit-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public KHostUserGroup? Group { get; set; }

    [Parameter] public EventCallback<KHostUserGroup> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private EditUserGroupModel _model = new();
    private EditContext _editContext = default!;

    // DialogHost keys every dialog by request id, so a fresh instance is created per open; this
    // runs exactly once with Group already bound.
    protected override void OnInitialized()
    {
        _model = Group is null
            ? new EditUserGroupModel()
            : new EditUserGroupModel
            {
                Id = Group.Id,
                Name = Group.Name,
                Description = Group.Description,
                ExcludeFromSingerQueue = Group.ExcludeFromSingerQueue
            };

        _editContext = new EditContext(_model);
    }

    public async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    private async Task SaveAsync()
    {
        if (!_editContext.Validate()) return;

        var group = Group ?? new KHostUserGroup { Id = _model.Id, Name = _model.Name };
        group.Name = _model.Name;
        group.Description = _model.Description;
        group.ExcludeFromSingerQueue = _model.ExcludeFromSingerQueue;

        // DialogHost closes after awaiting this itself; closing again here would also fire
        // OnClose's onCancel, marking a successful save as a cancel.
        await OnSave.InvokeAsync(group);
    }

    public record DialogRequest : EditDialogRequest<KHostUserGroup>
    {
        public DialogRequest(KHostUserGroup? value, Func<KHostUserGroup?, Task> onSave, Action? onCancel, Action? onClose)
            : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
