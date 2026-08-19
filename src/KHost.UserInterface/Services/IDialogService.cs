using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public interface IDialogService
{
    event EventHandler<BaseDialogRequest> ShowRequested;

    Task RequestEditAsync(Media item, Action<Media?> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(KHostUser item, Action<KHostUser?> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(KHostUserGroup item, Action<KHostUserGroup?> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(Venue item, Action<Venue?> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestEditAsync(Tip item, Action<Tip?> onSave, Action? onCancel = null, Action? onClose = null);

    Task<bool> ShowConfirmationAsync(string message, Action onConfirm, string title = "Confirm", string confirmText = "Confirm", Action? onCancel = null, Action? onClose = null);
    Task ShowSettingsMenuAsync(Action? onClose = null);
    Task ShowSingerPerformanceHistoryAsync(Guid userId, Action? onClose = null);
    Task ShowLyricsAsync(string query, Action? onClose = null);
    Task ShowScreensAsync(Action? onClose = null);

    /// <summary>Tells the host playback needs a screen, offering to open the Screens dialog.</summary>
    Task ShowNoScreensAsync();

    /// <summary>Presents a failure in the host's own words, with its reference code.</summary>
    Task ShowErrorAsync(KHostException error, string title = "Something went wrong", Action? onRetry = null, Action? onClose = null);
    Task RequestEditAsync(Tip? item, Guid userId, Action<Tip?> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestBulkEditAsync(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel = null, Action? onClose = null);
}
