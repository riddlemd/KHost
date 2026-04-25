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

    public Task ShowSingerPerformanceHistoryAsync(Guid singerId, Action? onClose)
    {
        ShowRequested?.Invoke(this, new SingerPerformanceHistoryDialog.DialogRequest(singerId, onClose));

        return Task.CompletedTask;
    }

    public Task ShowSettingsMenuAsync(Action? onClose = null)
    {
        ShowRequested?.Invoke(this, new SettingsMenuDialog.DialogRequest(onClose));

        return Task.CompletedTask;
    }

    public async Task RequestEditAsync(Media item, Action<Media?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<MediaEditDialog.DialogRequest, Media>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(Singer item, Action<Singer?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<SingerEditDialog.DialogRequest, Singer>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(Venue item, Action<Venue?> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<VenueEditDialog.DialogRequest, Venue>(item, onSave, onCancel, onClose);

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
