namespace KHost.Abstractions.Interactions;

public interface IInteractionHandler<in TRequest>
    where TRequest : IInteractionRequest
{
    Task HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

public interface IInteractionHandler<in TRequest, TResponse>
    where TRequest : IInteractionRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
