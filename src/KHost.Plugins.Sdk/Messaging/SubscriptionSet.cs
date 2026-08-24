namespace KHost.Plugins.Sdk.Messaging;

/// <summary>
/// Holds the subscriptions of something with a lifetime — a component, a service — so disposing it
/// once drops all of them. A missed unsubscribe keeps the subscriber alive on the broker, which for
/// a Blazor component means holding its whole circuit.
/// </summary>
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
