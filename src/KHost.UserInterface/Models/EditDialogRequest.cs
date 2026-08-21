namespace KHost.UserInterface.Models;

public abstract record EditDialogRequest<T> : BaseDialogRequest
    where T : class
{
    protected EditDialogRequest(T? value, Func<T?, Task>? onSave, Action? onCancel, Action? onClose) : base(onClose)
    {
        Value = value;
        OnSave = onSave;
        OnCancel = onCancel;
    }

    public T? Value { get; }
    /// <summary>
    /// A Task, not an Action: an async void handler's failure never reaches the error boundary, it
    /// reaches the circuit.
    /// </summary>
    public Func<T?, Task>? OnSave { get; }
    public Action? OnCancel { get; }
}
