using System.Collections.Concurrent;
using System.Reflection;
using KHost.Abstractions.Interactions;

namespace KHost.UserInterface.Interactions;

public class DialogInteractionDispatcher : IInteractionDispatcher
{
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), MethodInfo> _typedHandleMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _voidHandleMethodCache = new();

    private readonly IServiceProvider _serviceProvider;

    public DialogInteractionDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> RequestAsync<TResponse>(IInteractionRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var handlerType = typeof(IInteractionHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No IInteractionHandler<{requestType.Name}, {typeof(TResponse).Name}> registered.");

        var method = _typedHandleMethodCache.GetOrAdd(
            (requestType, typeof(TResponse)),
            _ => handlerType.GetMethod("HandleAsync")!);

        return (Task<TResponse>)method.Invoke(handler, [request, cancellationToken])!;
    }

    public Task RequestAsync(IInteractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var handlerType = typeof(IInteractionHandler<>).MakeGenericType(requestType);
        var handler = _serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No IInteractionHandler<{requestType.Name}> registered.");

        var method = _voidHandleMethodCache.GetOrAdd(
            requestType,
            _ => handlerType.GetMethod("HandleAsync")!);

        return (Task)method.Invoke(handler, [request, cancellationToken])!;
    }
}
