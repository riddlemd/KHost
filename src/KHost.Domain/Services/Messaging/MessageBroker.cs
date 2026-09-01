using System.Collections.Concurrent;
using System.Collections.Immutable;
using KHost.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Messaging;

public class MessageBroker : IMessageBroker
{
    // Copy-on-write, and never invoked under a lock: these events reach us on the SignalR hub
    // thread while it holds a lock the handlers themselves need to take.
    private readonly ConcurrentDictionary<Type, ImmutableList<Func<object, CancellationToken, Task>>> _handlers = new();

    private readonly ILogger<MessageBroker> _logger;

    public MessageBroker(ILogger<MessageBroker> logger)
    {
        _logger = logger;
    }

    public IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        Task Invoke(object message, CancellationToken cancellationToken) => handler((TMessage)message, cancellationToken);

        _handlers.AddOrUpdate(typeof(TMessage), _ => [Invoke], (_, existing) => existing.Add(Invoke));

        return new Subscription(() =>
            _handlers.AddOrUpdate(typeof(TMessage), _ => [], (_, existing) => existing.Remove(Invoke)));
    }

    public IDisposable Subscribe<TMessage>(Action<TMessage> handler) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Subscribe<TMessage>((message, _) =>
        {
            handler(message);
            return Task.CompletedTask;
        });
    }

    public void Announce<TMessage>(TMessage message) where TMessage : notnull => _ = PublishAsync(message);

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);

        // The message's own type, not TMessage: a publisher holding one as object — a service
        // exposing its change message through a base-class property — would otherwise post it
        // under object and reach nobody.
        if (!_handlers.TryGetValue(message.GetType(), out var handlers)) return;

        // One at a time, in subscription order: what one handler does decides what the next is
        // allowed to do, and a show where an ad and the bed race for the room cannot be reproduced.
        foreach (var handler in handlers)
        {
            try
            {
                await handler(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A {MessageType} handler failed", message.GetType().Name);
            }
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        // Exchanged rather than checked: disposing twice must not remove a later subscription that
        // happens to compare equal to this one.
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
