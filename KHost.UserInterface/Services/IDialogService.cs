using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public interface IDialogService :
    IInteractiveEditor<Media>,
    IInteractiveEditor<Singer>,
    IInteractiveEditor<Venue>
{
    event EventHandler<BaseDialogRequest> ShowRequested;
    event EventHandler HideRequested;

    Task<bool> ShowConfirmationAsync(string message, Action onConfirm, string title = "Confirm", string confirmText = "Confirm", Action? onCancel = null, Action? onClose = null);
    Task ShowSettingsMenuAsync(Action? onClose = null);
    Task ShowSingerPerformanceHistoryAsync(Guid singerId, Action? onClose = null);
    Task ShowLyricsAsync(string query, Action? onClose = null);
}
