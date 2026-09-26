using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class FlashBanner : IDisposable
{
    /// <summary>Long enough to notice and read, short enough not to sit over the queue.</summary>
    private const int SuccessVisibleMilliseconds = 4000;

    /// <summary>Warnings and errors get longer on screen: the host is more likely to be mid-task
    /// when one appears and needs a moment before it reads as background noise.</summary>
    private const int WarningVisibleMilliseconds = 8000;

    /// <summary>Test seam: a mutation sweep or an auto-dismiss assertion swaps this for something
    /// that resolves at once, rather than waiting out the real duration.</summary>
    internal Func<int, Task> Delay { get; set; } = ms => Task.Delay(ms);

    [Inject] private IFlashService? Flash { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();
    private readonly HashSet<Guid> _counting = [];

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<FlashChanged>(OnFlashChanged));
    }

    private void OnFlashChanged(FlashChanged flashChanged)
    {
        foreach (var message in Flash?.Messages ?? [])
        {
            // One countdown per message: a second Show while the first is still up must not reset
            // or duplicate the timer already running for it.
            if (_counting.Add(message.Id))
                _ = WithdrawAsync(message);
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private async Task WithdrawAsync(FlashMessage message)
    {
        var visibleMilliseconds = message.Type == FlashType.Success
            ? SuccessVisibleMilliseconds
            : WarningVisibleMilliseconds;

        await Delay(visibleMilliseconds);

        _counting.Remove(message.Id);
        Flash?.Dismiss(message);
    }

    public void Dispose() => _subscriptions.Dispose();
}
