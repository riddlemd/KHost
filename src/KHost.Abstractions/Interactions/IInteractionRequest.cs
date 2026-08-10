namespace KHost.Abstractions.Interactions;

public interface IInteractionRequest { }

public interface IInteractionRequest<TResponse> : IInteractionRequest { }
