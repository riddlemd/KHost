using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Confirms something whose effect the operator can't see, e.g. a settings save.</summary>
/// <remarks>One message across the top of the console at a time. A plugin may take it to tell the
/// host something brief, such as why an action was refused; it is for a line the host reads and
/// moves on from, not for a failure that needs a decision. A host singleton, callable from any
/// thread. Announces <see cref="KHost.Abstractions.Messaging.Messages.FlashChanged"/> on every show
/// and dismiss.</remarks>
public interface IFlashService
{
    /// <summary>The message showing now, or null when none is.</summary>
    FlashMessage? Current { get; }

    /// <summary>Shows <paramref name="text"/>, replacing whatever was showing.</summary>
    /// <remarks>The console withdraws it after a while on its own; a caller need not dismiss
    /// it.</remarks>
    void Show(string text, FlashType type = FlashType.Success);

    /// <summary>Withdraws the current message. Does nothing, and announces nothing, when none is
    /// showing.</summary>
    void Dismiss();
}
