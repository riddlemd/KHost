using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Confirms something whose effect the operator can't see, e.g. a settings save.</summary>
public interface IFlashService
{
    FlashMessage? Current { get; }

    void Show(string text, FlashType type = FlashType.Success);

    void Dismiss();
}
