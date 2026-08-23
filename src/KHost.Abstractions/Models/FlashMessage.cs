namespace KHost.Abstractions.Models;

public enum FlashKind
{
    Success,
    Warning
}

/// <summary>A message shown across the top of the console and then withdrawn.</summary>
public sealed record FlashMessage(string Text, FlashKind Kind);
