using System.Reflection;
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
    [Parameter] public bool CloseOnScrimClick { get; set; }
    [Parameter] public string Class { get; set; } = "";

    [Parameter] public EventCallback<KHostUserGroup> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private EditUserGroupModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;
    private ElementReference _permissionsTableElement;
    private Dictionary<string, object?> _permissionsTableAttrs = [];

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    protected override void OnParametersSet()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _model = Group is null
                ? new EditUserGroupModel()
                : new EditUserGroupModel
                {
                    Id = Group.Id,
                    Name = Group.Name,
                    Description = Group.Description,
                    IsAdmin = Group.IsAdmin,
                    Permissions = [.. Group.Permissions]
                };

            _editContext = new EditContext(_model);
        }
        _prevIsOpen = IsOpen;

        _permissionsTableAttrs = _model.IsAdmin
            ? new Dictionary<string, object?> { { "style", "opacity: 0.6;" } }
            : [];
    }

    private void TogglePermission(KHostPermission permission, bool enabled)
    {
        if (_model.IsAdmin) return;

        if (enabled)
        {
            if (!_model.Permissions.Contains(permission))
                _model.Permissions.Add(permission);
        }
        else
        {
            _model.Permissions.Remove(permission);
        }
    }

    private static IEnumerable<IGrouping<string, KHostPermission>> GetGroupedPermissions()
        => Enum.GetValues<KHostPermission>()
            .GroupBy(p => typeof(KHostPermission)
                .GetField(p.ToString())!
                .GetCustomAttribute<PermissionGroupAttribute>()?.GroupName ?? "Other");

    private static string GetPermissionDescription(KHostPermission permission) => permission switch
    {
        KHostPermission.EditUser => "Edit user profiles and settings",
        KHostPermission.DeleteUser => "Delete user accounts",
        KHostPermission.EditGroup => "Edit user group properties",
        KHostPermission.DeleteGroup => "Delete user groups",
        KHostPermission.EditVenue => "Edit venue settings",
        KHostPermission.DeleteVenue => "Delete venues",
        KHostPermission.ImportLibrary => "Import songs into the library",
        KHostPermission.DeleteMedia => "Delete songs from the library",
        KHostPermission.AddToQueue => "Add songs to the performance queue",
        KHostPermission.RemoveFromQueue => "Remove songs from the queue",
        KHostPermission.ReorderQueue => "Reorder songs in the queue",
        KHostPermission.SkipQueue => "Skip the current song",
        KHostPermission.ViewPerformanceHistory => "View user performance history",
        _ => ""
    };

    public async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    private async Task CancelAsync()
    {
        await OnClose.InvokeAsync();
        await CloseAsync();
    }

    private async Task SaveAsync()
    {
        if (!_editContext.Validate()) return;

        var group = Group ?? new KHostUserGroup { Id = _model.Id, Name = _model.Name };
        group.Name = _model.Name;
        group.Description = _model.Description;
        group.IsAdmin = _model.IsAdmin;
        group.Permissions = _model.IsAdmin ? [] : [.. _model.Permissions];

        await OnSave.InvokeAsync(group);
        await CloseAsync();
    }

    public record DialogRequest : EditDialogRequest<KHostUserGroup>
    {
        public DialogRequest(KHostUserGroup? value, Action<KHostUserGroup?> onSave, Action? onCancel, Action? onClose)
            : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
