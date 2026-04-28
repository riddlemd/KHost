using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;


namespace KHost.UserInterface.Services;

public class DialogService : IDialogService
{
    public event EventHandler<BaseDialogRequest>? ShowRequested;
    public event EventHandler? HideRequested;

    public Task<bool> ShowConfirmationAsync(
        string message,
        Action onConfirm,
        string title = "Confirm",
        string confirmText = "Confirm",
        Action? onCancel = null,
        Action? onClose = null)
    {
        ShowRequested?.Invoke(this, new ConfirmationDialog.DialogRequest(title, message, confirmText, onConfirm, onCancel, onClose));

        return Task.FromResult(false);
    }

    public Task ShowSingerPerformanceHistoryAsync(Guid userId, Action? onClose = null)
    {
        ShowRequested?.Invoke(this, new SingerPerformanceHistoryDialog.DialogRequest(userId, onClose));

        return Task.CompletedTask;
    }

    public Task ShowSettingsMenuAsync(Action? onClose = null)
    {
        ShowRequested?.Invoke(this, new SettingsMenuDialog.DialogRequest(onClose));

        return Task.CompletedTask;
    }

    public async Task RequestEditAsync(Media item, Action<Media?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditMediaDialog.DialogRequest, Media>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(KHostUser item, Action<KHostUser?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditUserDialog.DialogRequest, KHostUser>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(KHostUserGroup item, Action<KHostUserGroup?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditUserGroupDialog.DialogRequest, KHostUserGroup>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(Venue item, Action<Venue?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditVenueDialog.DialogRequest, Venue>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(Tip item, Action<Tip?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditTipDialog.DialogRequest, Tip>(item, onSave, onCancel, onClose);

    public Task RequestEditAsync(Tip item, Guid userId, Action<Tip?> onSave, Action? onCancel = null, Action? onClose = null)
    {
        var request = new EditTipDialog.DialogRequest(item, userId, onSave, onCancel, onClose);
        ShowRequested?.Invoke(this, request);
        return Task.CompletedTask;
    }

    public Task RequestBulkEditAsync(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel = null, Action? onClose = null)
    {
        var request = new BulkEditMediaDialog.DialogRequest(items, onSave, onCancel, onClose);
        ShowRequested?.Invoke(this, request);
        return Task.CompletedTask;
    }

    public Task ShowLyricsAsync(string query, Action? onClose = null)
    {
        ShowRequested?.Invoke(this, new ShowLyricsDialog.DialogRequest(query, onClose));

        return Task.CompletedTask;
    }

    private Task RequestEditAsync<TRequest, TInput>(TInput item, Action<TInput?> onSave, Action? onCancel = null, Action? onClose = null)
        where TInput : class
        where TRequest : EditDialogRequest<TInput>
    {
        if (Activator.CreateInstance(typeof(TRequest), item, onSave, onCancel, onClose) is not TRequest output)
            return Task.CompletedTask;

        ShowRequested?.Invoke(this, output);

        return Task.CompletedTask;
    }
}
