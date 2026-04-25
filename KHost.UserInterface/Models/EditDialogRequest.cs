namespace KHost.UserInterface.Models;

public abstract record EditDialogRequest<T> : BaseDialogRequest
    where T : class
{
    protected EditDialogRequest(T? value, Action<T?>? onSave, Action? onCancel, Action? onClose) : base(onClose)
    {
        Value = value;
        OnSave = onSave;
        OnCancel = onCancel;
    }

    public T? Value { get; }
    public Action<T?>? OnSave { get; }
    public Action? OnCancel { get; }
}
