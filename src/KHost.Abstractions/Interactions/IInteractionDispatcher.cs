namespace KHost.Abstractions.Interactions;

/// <summary>Asks the host, the person at the console, for something: a typed value, a look at a
/// table, a confirmation.</summary>
/// <remarks>
/// <para>A plugin takes this in its constructor; it never implements it. Send the request types in
/// <see cref="Requests"/>: <see cref="Requests.TextPromptRequest"/> to have the host type values in,
/// <see cref="Requests.ShowPluginTableRequest"/> to show a list.</para>
/// <para>What comes back from a <see cref="Requests.TextPromptRequest"/> is the plugin's to keep, not
/// the host's: nothing from that round trip is saved to the plugin's settings. A value worth keeping
/// goes in <see cref="Services.IPluginContext.SetSecretAsync"/>; a password is hashed before it is
/// kept, never stored raw.</para>
/// <para>A singleton, callable from any thread. The request appears in every open console window,
/// and the first answer from any of them completes the call. With no console open it is shown
/// nowhere and completes at once as dismissed, with the request type's dismissed answer. Either way
/// the answer waits on a person, so do not await one on a path the show depends on.</para>
/// </remarks>
public interface IInteractionDispatcher
{
    /// <summary>Shows <paramref name="request"/> and waits for the host's answer.</summary>
    /// <param name="request">The request to show; its type decides the dialog and the answer.</param>
    /// <returns>The answer; what a dismissed request returns is defined by the request type
    /// (null for <see cref="Requests.TextPromptRequest"/>).</returns>
    /// <param name="cancellationToken">Cancelling before the host answers ends the wait with
    /// <see cref="OperationCanceledException"/>; it does not close what is on screen.</param>
    /// <exception cref="InvalidOperationException">The host has no handler for this request type
    /// and <typeparamref name="TResponse"/>.</exception>
    Task<TResponse> RequestAsync<TResponse>(IInteractionRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Shows <paramref name="request"/> and completes when the host closes it.</summary>
    /// <param name="request">The request to show; its type decides the dialog.</param>
    /// <param name="cancellationToken">As for <see cref="RequestAsync{TResponse}"/>.</param>
    /// <exception cref="InvalidOperationException">The host has no handler for this request type.</exception>
    Task RequestAsync(IInteractionRequest request, CancellationToken cancellationToken = default);
}
