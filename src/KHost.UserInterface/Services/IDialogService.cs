using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public interface IDialogService
{
    event EventHandler<BaseDialogRequest> ShowRequested;

    /// <summary>A null item is a request to add: the dialog opens empty and titles itself so.</summary>
    Task RequestEditAsync(Media? item, Func<Media?, Task> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(KHostUser? item, Func<KHostUser?, Task> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(KHostUserGroup? item, Func<KHostUserGroup?, Task> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(Venue? item, Func<Venue?, Task> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(Tip? item, Func<Tip?, Task> onSave, Action? onCancel = null, Action? onClose = null);

    Task<bool> ShowConfirmationAsync(string message, Func<Task> onConfirm, string title = "Confirm", string confirmText = "Confirm", Action? onCancel = null, Action? onClose = null);
    Task ShowSingerPerformanceHistoryAsync(Guid userId, Action? onClose = null);
    Task ShowLyricsAsync(string query, Action? onClose = null);
    Task ShowScreensAsync(Action? onClose = null);
    Task ShowShortcutsAsync(Action? onClose = null);

    /// <summary>Tells the host playback needs a screen, offering to open the Screens dialog.</summary>
    Task ShowNoScreensAsync();

    /// <summary>Presents a failure in the host's own words, with its reference code.</summary>
    Task ShowErrorAsync(KHostException error, string title = "Something went wrong", Action? onRetry = null, Action? onClose = null);
    Task RequestEditAsync(Tip? item, Guid userId, Func<Tip?, Task> onSave, Action? onCancel = null, Action? onClose = null, bool showDate = true);
    Task RequestBulkEditAsync(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel = null, Action? onClose = null);
}
