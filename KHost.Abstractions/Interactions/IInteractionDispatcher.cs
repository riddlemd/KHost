namespace KHost.Abstractions.Interactions;

public interface IInteractionDispatcher
{
    Task<TResponse> RequestAsync<TResponse>(IInteractionRequest<TResponse> request, CancellationToken cancellationToken = default);

    Task RequestAsync(IInteractionRequest request, CancellationToken cancellationToken = default);
}
