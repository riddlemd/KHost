namespace KHost.IPC.SignalR;

/// <summary>Fails a request the host accepted but never answered, so SignalR's negotiate can't hold a
/// reconnect for the minutes its own HttpClient would wait.</summary>
internal sealed class AttemptTimeoutHandler(HttpMessageHandler inner, TimeSpan timeout) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        try
        {
            return await base.SendAsync(request, bounded.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No answer from {request.RequestUri} within {timeout}.");
        }
    }
}
