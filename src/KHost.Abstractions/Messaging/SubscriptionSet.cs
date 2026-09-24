namespace KHost.Abstractions.Messaging;

/// <summary>Holds subscriptions so disposing once drops all; a miss leaks a component's circuit.</summary>
public sealed class SubscriptionSet : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    /// <summary>Keeps a subscription so disposing this set disposes it too.</summary>
    public void Add(IDisposable subscription) => _subscriptions.Add(subscription);

    /// <summary>Disposes every held subscription and empties the set.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        _subscriptions.Clear();
    }
}
