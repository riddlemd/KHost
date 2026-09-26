using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Confirms something whose effect the operator can't see, e.g. a settings save.</summary>
/// <remarks>Fixed to the viewport, over the content, wherever the host is scrolled. Several may
/// show at once. A plugin may take it to tell the host something brief, such as why an action was
/// refused; it is for a line the host reads and moves on from, not for a failure that needs a
/// decision. A host singleton, callable from any thread. Announces
/// <see cref="KHost.Abstractions.Messaging.Messages.FlashChanged"/> on every show and dismiss.
/// </remarks>
public interface IFlashService
{
    /// <summary>The most recently shown message still on screen, or null when none is.</summary>
    FlashMessage? Current { get; }

    /// <summary>Every message showing now, oldest first.</summary>
    /// <remarks>Default body wraps <see cref="Current"/>, so an implementation written before
    /// stacking landed still compiles and behaves as a single message.</remarks>
    IReadOnlyList<FlashMessage> Messages => Current is { } message ? [message] : [];

    /// <summary>Shows <paramref name="text"/> as one more message in the stack.</summary>
    /// <remarks>The console withdraws it after a while on its own; a caller need not dismiss
    /// it.</remarks>
    void Show(string text, FlashType type = FlashType.Success);

    /// <summary>Withdraws the most recently shown message. Does nothing, and announces nothing,
    /// when none is showing.</summary>
    void Dismiss();

    /// <summary>Withdraws one message from the stack, leaving any others showing.</summary>
    /// <remarks>Default body defers to <see cref="Dismiss()"/> when <paramref name="message"/> is
    /// the current one, for the same single-message compatibility as <see cref="Messages"/>.
    /// </remarks>
    void Dismiss(FlashMessage message)
    {
        if (ReferenceEquals(Current, message)) Dismiss();
    }
}
