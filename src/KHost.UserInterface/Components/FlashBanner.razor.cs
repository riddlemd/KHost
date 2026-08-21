using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class FlashBanner : IDisposable
{
    /// <summary>Long enough to notice and read, short enough not to sit over the queue.</summary>
    private const int VisibleMilliseconds = 4000;

    [Inject] private IFlashService? Flash { get; set; }

    private FlashMessage? _counting;

    protected override void OnInitialized()
    {
        if (Flash is not null)
            Flash.StateChanged += OnFlashChanged;
    }

    private void OnFlashChanged(object? sender, EventArgs e)
    {
        var message = Flash?.Current;

        if (message is not null && !ReferenceEquals(message, _counting))
        {
            _counting = message;
            _ = WithdrawAsync(message);
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private async Task WithdrawAsync(FlashMessage message)
    {
        await Task.Delay(VisibleMilliseconds);

        // Reference equality: a message shown since owns the banner, and this countdown is stale.
        if (ReferenceEquals(Flash?.Current, message))
            Flash.Dismiss();
    }

    public void Dispose()
    {
        if (Flash is not null)
            Flash.StateChanged -= OnFlashChanged;
    }
}
