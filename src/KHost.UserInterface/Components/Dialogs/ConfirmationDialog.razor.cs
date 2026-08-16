using System.Text.Json;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class ConfirmationDialog
{
    private const string _rootClassName = "kh-confirmation-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Title { get; set; } = "Confirm";
    [Parameter] public string TitleIcon { get; set; } = "exclamation-triangle-fill";
    [Parameter] public string Message { get; set; } = "Are you sure?";
    [Parameter] public string ConfirmText { get; set; } = "Confirm";
    [Parameter] public string ConfirmTextClass { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    public async Task ConfirmAsync()
    {
        await OnConfirm.InvokeAsync();

        await CloseAsync();
    }

    public async Task CancelAsync()
    {
        await OnCancel.InvokeAsync();

        await CloseAsync();
    }

    private string GetConfirmButtonClassName()
        => JsonNamingPolicy.KebabCaseLower.ConvertName(ConfirmText);

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(string title, string message, string confirmText, Action onConfirm, Action? onCancel, Action? onClose) : base(onClose)
        {
            Title = title;
            Message = message;
            ConfirmText = confirmText;
            OnConfirm = onConfirm;
            OnCancel = onCancel;
        }

        public string Title { get; }
        public string Message { get; }
        public string ConfirmText { get; }
        public Action OnConfirm { get; }
        public Action? OnCancel { get; }
    }
}
