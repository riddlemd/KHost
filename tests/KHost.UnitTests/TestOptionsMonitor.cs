using Microsoft.Extensions.Options;

namespace KHost.UnitTests;

/// <summary>An options monitor a test can move. <see cref="Options.Create{TOptions}"/> hands back
/// an <see cref="IOptions{TOptions}"/>, which cannot change, and the services under test now read
/// their settings live precisely so a host does not have to restart.</summary>
internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public T CurrentValue { get; private set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Unsubscriber(_listeners, listener);
    }

    /// <summary>Publishes a new value the way a rewritten settings overlay does.</summary>
    public void Set(T value)
    {
        CurrentValue = value;

        // Copied first: a listener that unsubscribes itself would otherwise mutate the list we
        // are walking.
        foreach (var listener in _listeners.ToArray())
            listener(value, null);
    }

    private sealed class Unsubscriber(List<Action<T, string?>> listeners, Action<T, string?> listener) : IDisposable
    {
        public void Dispose() => listeners.Remove(listener);
    }
}
