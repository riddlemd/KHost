using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class DialogHost : IDisposable
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private readonly List<BaseDialogRequest> _stack = [];

    protected override void OnInitialized()
    {
        DialogService.ShowRequested += OnShowRequested;
    }

    private void OnShowRequested(object? sender, BaseDialogRequest request)
    {
        InvokeAsync(() =>
        {
            _stack.Add(request);
            StateHasChanged();
        });
    }

    private void Close(BaseDialogRequest request)
    {
        InvokeAsync(() =>
        {
            _stack.Remove(request);
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        DialogService.ShowRequested -= OnShowRequested;
    }
}
