using KHost.Abstractions.Models;

namespace KHost.Abstractions.Interactions.Requests;

/// <summary>Asks the host to open its edit-media dialog for one row.</summary>
/// <param name="Media">The row to edit.</param>
/// <remarks>Resolves to the edited row, or null if the host cancelled.</remarks>
public sealed record EditMediaRequest(Media Media) : IInteractionRequest<Media?>;
