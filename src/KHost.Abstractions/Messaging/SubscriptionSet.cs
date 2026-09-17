namespace KHost.Abstractions.Messaging;

/// <summary>Holds subscriptions so disposing once drops all; a miss leaks a component's circuit.</summary>
public sealed class SubscriptionSet : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public void Add(IDisposable subscription) => _subscriptions.Add(subscription);

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        _subscriptions.Clear();
    }
}
