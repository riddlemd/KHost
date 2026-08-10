using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IInteractiveEditor<T> where T : class
{
    Task RequestEditAsync(T item, Action<T?> onSave, Action? onCancel = null, Action? onClose = null);
}
