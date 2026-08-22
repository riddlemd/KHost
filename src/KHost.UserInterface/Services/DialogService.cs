using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using Microsoft.Extensions.Logging;


namespace KHost.UserInterface.Services;

public class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;

    public event EventHandler<BaseDialogRequest>? ShowRequested;

    public DialogService(ILogger<DialogService> logger)
    {
        _logger = logger;
    }

    public Task<bool> ShowConfirmationAsync(
        string message,
        Func<Task> onConfirm,
        string title = "Confirm",
        string confirmText = "Confirm",
        Action? onCancel = null,
        Action? onClose = null)
    {
        var request = new ConfirmationDialog.DialogRequest(title, message, confirmText, onConfirm, onCancel, onClose);
        _logger.LogDebug("Dialog requested: {DialogType} title={Title}", nameof(ConfirmationDialog), title);
        ShowRequested?.Invoke(this, request);

        return Task.FromResult(false);
    }

    public Task ShowSingerPerformanceHistoryAsync(Guid userId, Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType} userId={UserId}", nameof(SingerPerformanceHistoryDialog), userId);
        ShowRequested?.Invoke(this, new SingerPerformanceHistoryDialog.DialogRequest(userId, onClose));

        return Task.CompletedTask;
    }

    public async Task RequestEditAsync(Media? item, Func<Media?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditMediaDialog.DialogRequest, Media>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(KHostUser? item, Func<KHostUser?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditUserDialog.DialogRequest, KHostUser>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(KHostUserGroup? item, Func<KHostUserGroup?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditUserGroupDialog.DialogRequest, KHostUserGroup>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(Venue? item, Func<Venue?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditVenueDialog.DialogRequest, Venue>(item, onSave, onCancel, onClose);

    public async Task RequestEditAsync(Tip? item, Func<Tip?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditTipDialog.DialogRequest, Tip>(item, onSave, onCancel, onClose);

    public Task RequestEditAsync(Tip? item, Guid userId, Func<Tip?, Task> onSave, Action? onCancel = null, Action? onClose = null, bool showDate = true)
    {
        var request = new EditTipDialog.DialogRequest(item, userId, onSave, onCancel, onClose, showDate);
        _logger.LogDebug("Dialog requested: {DialogType} userId={UserId}", nameof(EditTipDialog), userId);
        ShowRequested?.Invoke(this, request);
        return Task.CompletedTask;
    }

    public Task RequestBulkEditAsync(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel = null, Action? onClose = null)
    {
        var request = new BulkEditMediaDialog.DialogRequest(items, onSave, onCancel, onClose);
        _logger.LogDebug("Dialog requested: {DialogType} count={Count}", nameof(BulkEditMediaDialog), items.Count);
        ShowRequested?.Invoke(this, request);
        return Task.CompletedTask;
    }

    public Task ShowLyricsAsync(string query, Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType} query={Query}", nameof(ShowLyricsDialog), query);
        ShowRequested?.Invoke(this, new ShowLyricsDialog.DialogRequest(query, onClose));

        return Task.CompletedTask;
    }

    public Task ShowShortcutsAsync(Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType}", nameof(ShortcutsDialog));
        ShowRequested?.Invoke(this, new ShortcutsDialog.DialogRequest(onClose));

        return Task.CompletedTask;
    }

    public Task ShowScreensAsync(Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType}", nameof(ScreensDialog));
        ShowRequested?.Invoke(this, new ScreensDialog.DialogRequest(onClose));

        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(
        KHostException error,
        string title = "Something went wrong",
        Action? onRetry = null,
        Action? onClose = null)
    {
        // The stack trace is for the collapsed section, so it never reaches the host unless asked.
        var request = new ErrorDialog.DialogRequest(error, title, error.ToString(), onRetry, onClose);

        _logger.LogError(error, "Error shown to the host: {Reference}", error.ReferenceCode);
        ShowRequested?.Invoke(this, request);

        return Task.CompletedTask;
    }

    public Task ShowNoScreensAsync()
        => ShowConfirmationAsync(
            "Playback needs a screen for audio and video output.",
            onConfirm: () => ShowScreensAsync(),
            title: "No screens connected",
            confirmText: "Launch Screen");

    private Task RequestEditAsync<TRequest, TInput>(TInput? item, Func<TInput?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        where TInput : class
        where TRequest : EditDialogRequest<TInput>
    {
        if (Activator.CreateInstance(typeof(TRequest), item, onSave, onCancel, onClose) is not TRequest output)
        {
            _logger.LogWarning("Dialog request construction failed for {RequestType}", typeof(TRequest).Name);
            return Task.CompletedTask;
        }

        _logger.LogDebug("Dialog requested: {DialogType}", typeof(TRequest).Name);
        ShowRequested?.Invoke(this, output);

        return Task.CompletedTask;
    }
}
