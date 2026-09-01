using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class FlashBanner : IDisposable
{
    /// <summary>Long enough to notice and read, short enough not to sit over the queue.</summary>
    private const int VisibleMilliseconds = 4000;

    [Inject] private IFlashService? Flash { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private FlashMessage? _counting;

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<FlashChanged>(OnFlashChanged));
    }

    private void OnFlashChanged(FlashChanged flashChanged)
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

    public void Dispose() => _subscriptions.Dispose();
}
