using KHost.Abstractions.Models;

namespace KHost.Abstractions.Interactions.Requests;

public sealed record EditMediaRequest(Media Media) : IInteractionRequest<Media?>;
