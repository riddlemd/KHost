namespace KHost.Abstractions.Interactions;

/// <summary>Marks a request to the host's user interface that expects no answer back.</summary>
/// <remarks>A plugin sends the host's own request types (<see cref="Requests.ShowPluginTableRequest"/>)
/// through <see cref="IInteractionDispatcher"/>. Defining a new request type is pointless: the host
/// has no handler for it, and a plugin cannot register one.</remarks>
public interface IInteractionRequest { }

/// <summary>Marks a request to the host's user interface that answers with a
/// <typeparamref name="TResponse"/>.</summary>
/// <remarks>See <see cref="Requests.TextPromptRequest"/> for the one a plugin most often sends.</remarks>
public interface IInteractionRequest<TResponse> : IInteractionRequest { }
