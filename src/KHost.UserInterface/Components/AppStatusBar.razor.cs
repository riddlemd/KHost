using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class AppStatusBar : IDisposable
{
    [Inject] private IMediaImportService? ImportService { get; set; }

    private string _version = "_ERROR_";
    private string _toastMessage = "Sing that song Comrade!";

    protected override void OnInitialized()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        _version = version?.ToString() ?? "_ERROR_";

        ImportService!.StateChanged += OnImportStateChanged;
    }

    private void OnImportStateChanged(object? sender, EventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    public void SetToastMessage(string message)
    {
        _toastMessage = message;
        StateHasChanged();
    }

    public void Dispose()
    {
        if (ImportService is not null)
            ImportService.StateChanged -= OnImportStateChanged;
    }
}
