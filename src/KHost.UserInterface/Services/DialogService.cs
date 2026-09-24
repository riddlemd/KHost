using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using Microsoft.Extensions.Logging;


namespace KHost.UserInterface.Services;

public class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;
    private readonly IReadOnlyList<IDisplayProvider> _displays;

    public event EventHandler<BaseDialogRequest>? ShowRequested;

    public DialogService(ILogger<DialogService> logger, IEnumerable<IDisplayProvider> displays)
    {
        _logger = logger;
        _displays = [.. displays];
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
        Show(request, onClose ?? onCancel);

        return Task.FromResult(false);
    }

    public Task ShowSingerPerformanceHistoryAsync(Guid userId, Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType} userId={UserId}", nameof(SingerPerformanceHistoryDialog), userId);
        Show(new SingerPerformanceHistoryDialog.DialogRequest(userId, onClose), onClose);

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

    public async Task RequestEditAsync(ThemeDefinition? item, Func<ThemeDefinition?, Task> onSave, Action? onCancel = null, Action? onClose = null)
        => await RequestEditAsync<EditThemeDialog.DialogRequest, ThemeDefinition>(item, onSave, onCancel, onClose);

    public Task RequestEditAsync(Tip? item, Guid userId, Func<Tip?, Task> onSave, Action? onCancel = null, Action? onClose = null, bool showDate = true)
    {
        var request = new EditTipDialog.DialogRequest(item, userId, onSave, onCancel, onClose, showDate);
        _logger.LogDebug("Dialog requested: {DialogType} userId={UserId}", nameof(EditTipDialog), userId);
        Show(request, onClose ?? onCancel);
        return Task.CompletedTask;
    }

    public Task RequestBulkEditAsync(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel = null, Action? onClose = null)
    {
        var request = new BulkEditMediaDialog.DialogRequest(items, onSave, onCancel, onClose);
        _logger.LogDebug("Dialog requested: {DialogType} count={Count}", nameof(BulkEditMediaDialog), items.Count);
        Show(request, onClose ?? onCancel);
        return Task.CompletedTask;
    }

    public Task ShowLyricsAsync(string query, Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType} query={Query}", nameof(ShowLyricsDialog), query);
        Show(new ShowLyricsDialog.DialogRequest(query, onClose), onClose);

        return Task.CompletedTask;
    }

    public Task ShowShortcutsAsync(Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType}", nameof(ShortcutsDialog));
        Show(new ShortcutsDialog.DialogRequest(onClose), onClose);

        return Task.CompletedTask;
    }

    public Task ShowPluginTableAsync(ShowPluginTableRequest table, Action? onClose = null)
    {
        _logger.LogDebug("Dialog requested: {DialogType} title={Title}", nameof(PluginTableDialog), table.Title);
        Show(new PluginTableDialog.DialogRequest(table, onClose), onClose);

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
        Show(request, onClose);

        return Task.CompletedTask;
    }

    public Task ShowUnsavedChangesAsync(
        Func<Task> onSave,
        Func<Task> onDiscard,
        string? message = null,
        Action? onStay = null)
    {
        var request = new UnsavedChangesDialog.DialogRequest(
            message ?? "You have changes that have not been saved yet.", onSave, onDiscard, onStay);

        _logger.LogDebug("Dialog requested: {DialogType}", nameof(UnsavedChangesDialog));
        Show(request, onStay);

        return Task.CompletedTask;
    }

    public Task RequestTextPromptAsync(
        string title, string? message, IReadOnlyList<TextPromptField> fields,
        Func<IReadOnlyDictionary<string, string>, Task> onSubmit, Action? onCancel = null, Action? onClose = null)
    {
        var request = new TextPromptDialog.DialogRequest(title, message, fields, onSubmit, onCancel, onClose);
        _logger.LogDebug("Dialog requested: {DialogType} title={Title}", nameof(TextPromptDialog), title);
        Show(request, onClose ?? onCancel);

        return Task.CompletedTask;
    }

    public Task RequestSingingAsAsync(
        Performance performance, string? singerName, Func<Performance?, Task> onSave,
        Action? onCancel = null, Action? onClose = null)
    {
        var request = new SingingAsDialog.DialogRequest(performance, singerName, onSave, onCancel, onClose);
        _logger.LogDebug("Dialog requested: {DialogType} performance={PerformanceId}",
            nameof(SingingAsDialog), performance.Id);
        Show(request, onClose ?? onCancel);

        return Task.CompletedTask;
    }

    /// <remarks>Confirming does what picking Local Display off the Display menu does, so there is
    /// one way a screen opens and the provider's refusal of a second one covers both.</remarks>
    public Task ShowNoScreensAsync()
        => ShowConfirmationAsync(
            "Playback needs a screen for audio and video output.",
            onConfirm: OpenLocalScreenAsync,
            title: "No screens connected",
            confirmText: "Launch Screen");

    /// <summary>Connects the transport the host opens itself rather than finds, which is the screens.</summary>
    private async Task OpenLocalScreenAsync()
    {
        var screens = _displays.FirstOrDefault(display => !display.SearchesForDevices);
        if (screens?.Devices.FirstOrDefault() is not { } device)
        {
            _logger.LogWarning("No local display is registered, so no screen can be opened");
            return;
        }

        // One display at a time: whatever else holds the song lets go before the screen opens.
        foreach (var other in _displays)
        {
            if (other == screens || other.ConnectedDeviceId is not { Length: > 0 }) continue;

            try { await other.DisconnectAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not disconnect {Provider}", other.Name); }
        }

        try { await screens.ConnectAsync(device.Id); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not open a screen"); }
    }

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
        Show(output, onClose ?? onCancel);

        return Task.CompletedTask;
    }

    /// <summary>Fires the request at whatever console is listening. With no subscriber the event
    /// is a silent no-op, and a caller awaiting the dialog's answer through
    /// <see cref="Abstractions.Interactions.IInteractionDispatcher"/> would hang forever, so this
    /// completes the request the same way a dismissed one does instead.</summary>
    private void Show(BaseDialogRequest request, Action? onUnshown)
    {
        if (ShowRequested is null)
        {
            _logger.LogWarning("No console is open to show {DialogType}; completing without an answer", request.GetType().Name);
            onUnshown?.Invoke();
            return;
        }

        ShowRequested.Invoke(this, request);
    }
}
