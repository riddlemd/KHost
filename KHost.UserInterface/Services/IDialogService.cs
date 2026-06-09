using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public interface IDialogService :
    IInteractiveEditor<Media>,
    IInteractiveEditor<KHostUser>,
    IInteractiveEditor<KHostUserGroup>,
    IInteractiveEditor<Venue>,
    IInteractiveEditor<Tip>
{
    event EventHandler<BaseDialogRequest> ShowRequested;
    event EventHandler HideRequested;

    Task<bool> ShowConfirmationAsync(string message, Action onConfirm, string title = "Confirm", string confirmText = "Confirm", Action? onCancel = null, Action? onClose = null);
    Task ShowSettingsMenuAsync(Action? onClose = null);
    Task ShowSingerPerformanceHistoryAsync(Guid userId, Action? onClose = null);
    Task ShowLyricsAsync(string query, Action? onClose = null);
    Task ShowScreensAsync(Action? onClose = null);
    Task RequestEditAsync(Tip? item, Guid userId, Action<Tip?> onSave, Action? onCancel = null, Action? onClose = null);
    Task RequestBulkEditAsync(IReadOnlyList<Media> items, Func<BulkEditMediaModel, Task> onSave, Action? onCancel = null, Action? onClose = null);
}
