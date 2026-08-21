namespace KHost.Abstractions.Models;

public enum FlashKind
{
    Success,
    Warning
}

/// <summary>A message shown across the top of the console and then withdrawn.</summary>
/// <param name="Text">What the operator reads.</param>
/// <param name="Kind">Which way it went.</param>
public sealed record FlashMessage(string Text, FlashKind Kind);
