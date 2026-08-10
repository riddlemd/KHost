using KHost.Abstractions.Models;

namespace KHost.UserInterface.Models;

public abstract record BaseDialogRequest
{
    protected BaseDialogRequest(Action? onClose)
    {
        OnClose = onClose;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public Action? OnClose { get; }
}
