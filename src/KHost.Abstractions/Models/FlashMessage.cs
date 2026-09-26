namespace KHost.Abstractions.Models;

/// <summary>How a <see cref="FlashMessage"/> is styled.</summary>
public enum FlashType
{
    /// <summary>Something completed normally.</summary>
    Success,

    /// <summary>Something needs the host's attention.</summary>
    Warning
}

/// <summary>A message shown over the viewport and then withdrawn.</summary>
/// <param name="Text">The message shown to the host.</param>
/// <param name="Type">How it is styled.</param>
public sealed record FlashMessage(string Text, FlashType Type)
{
    /// <summary>Tells two messages with the same words apart, so a stack of several can each be
    /// dismissed on its own.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}
