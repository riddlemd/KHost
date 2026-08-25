using KHost.Plugins.Sdk.Exceptions;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class ErrorDialog
{
    private const string _rootClassName = "kh-error-dialog";

    private bool _sendReport;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Title { get; set; } = "Something went wrong";
    [Parameter] public string WhatHappened { get; set; } = "";
    [Parameter] public string Suggestion { get; set; } = "";
    [Parameter] public string ReferenceCode { get; set; } = "";

    /// <summary>The stack trace and anything else only a developer wants. Collapsed until asked.</summary>
    [Parameter] public string? Detail { get; set; }

    [Parameter] public string Class { get; set; } = "";

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Null hides the retry button: not every failure is worth trying twice.</summary>
    [Parameter] public EventCallback? OnRetry { get; set; }

    /// <summary>Off until sending reports is actually built.</summary>
    [Parameter] public bool ReportingAvailable { get; set; }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    public async Task RetryAsync()
    {
        if (OnRetry is { } retry) await retry.InvokeAsync();

        await CloseAsync();
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(KHostException error, string title, string? detail, Action? onRetry, Action? onClose)
            : base(onClose)
        {
            Title = title;
            WhatHappened = error.WhatHappened;
            Suggestion = error.Suggestion;
            ReferenceCode = error.ReferenceCode;
            Detail = detail;
            OnRetry = onRetry;
        }

        public string Title { get; }
        public string WhatHappened { get; }
        public string Suggestion { get; }
        public string ReferenceCode { get; }
        public string? Detail { get; }
        public Action? OnRetry { get; }
    }
}
