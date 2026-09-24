namespace KHost.Abstractions.Interactions;

/// <summary>Presents one request type that expects no answer; what <see cref="IInteractionDispatcher"/>
/// finds for it.</summary>
/// <remarks>Host-only: the host's user interface implements these to draw its dialogs. A plugin has
/// no business with this interface; it cannot register a handler, and sends requests through
/// <see cref="IInteractionDispatcher"/> instead.</remarks>
public interface IInteractionHandler<in TRequest>
    where TRequest : IInteractionRequest
{
    /// <summary>Presents the request; completes when the host has closed it.</summary>
    /// <remarks>Must complete at once when there is nowhere to show it, and complete as cancelled when
    /// <paramref name="cancellationToken"/> fires before the host closes it.</remarks>
    Task HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Presents one request type and returns the host's answer; what
/// <see cref="IInteractionDispatcher"/> finds for it.</summary>
/// <remarks>Host-only, as <see cref="IInteractionHandler{TRequest}"/> is.</remarks>
public interface IInteractionHandler<in TRequest, TResponse>
    where TRequest : IInteractionRequest<TResponse>
{
    /// <summary>Presents the request; completes with the host's answer.</summary>
    /// <remarks>Must complete at once with the dismissed answer when there is nowhere to show it, and
    /// complete as cancelled when <paramref name="cancellationToken"/> fires before the host
    /// answers.</remarks>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
