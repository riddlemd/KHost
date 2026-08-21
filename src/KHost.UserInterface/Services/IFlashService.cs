namespace KHost.UserInterface.Services;

public enum FlashKind
{
    Success,
    Warning
}

/// <summary>A message shown across the top of the console and then withdrawn.</summary>
/// <param name="Text">What the operator reads.</param>
/// <param name="Kind">Which way it went.</param>
public sealed record FlashMessage(string Text, FlashKind Kind);

/// <summary>
/// Confirmation for an action whose effect is somewhere the operator is not looking — a save on a
/// settings page changes nothing on screen, so without this it is indistinguishable from a no-op.
/// </summary>
public interface IFlashService
{
    event EventHandler? StateChanged;

    FlashMessage? Current { get; }

    void Show(string text, FlashKind kind = FlashKind.Success);

    void Dismiss();
}
