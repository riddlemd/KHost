using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Confirmation for something whose effect is somewhere the operator is not looking — a save on a
/// settings page changes nothing on screen, and work finishing in the background changes nothing
/// they are watching. Domain code can reach this, so it does not have to be handed a UI callback.
/// </summary>
public interface IFlashService
{
    FlashMessage? Current { get; }

    void Show(string text, FlashType type = FlashType.Success);

    void Dismiss();
}
