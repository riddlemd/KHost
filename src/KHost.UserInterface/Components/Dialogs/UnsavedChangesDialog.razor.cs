using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class UnsavedChangesDialog
{
    [Parameter] public bool IsOpen { get; set; }

    [Parameter] public string Message { get; set; } =
        "You have changes that have not been saved yet.";

    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnDiscard { get; set; }

    /// <summary>Closing without choosing means staying put, so the header X is a real answer.</summary>
    [Parameter] public EventCallback OnStay { get; set; }

    private Task SaveAsync() => OnSave.InvokeAsync();

    private Task DiscardAsync() => OnDiscard.InvokeAsync();

    private Task StayAsync() => OnStay.InvokeAsync();

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(string message, Func<Task> onSave, Func<Task> onDiscard, Action? onStay)
            : base(onStay)
        {
            Message = message;
            OnSave = onSave;
            OnDiscard = onDiscard;
        }

        public string Message { get; }
        public Func<Task> OnSave { get; }
        public Func<Task> OnDiscard { get; }
    }
}
