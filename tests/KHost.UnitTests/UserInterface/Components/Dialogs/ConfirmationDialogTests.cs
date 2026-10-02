using Bunit;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>A confirm answers from the keyboard: Cancel takes focus when it opens and Esc backs out.</summary>
public class ConfirmationDialogTests : BunitContext
{
    public ConfirmationDialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Opening_FocusesCancel()
    {
        var cut = Render<ConfirmationDialog>(p => p.Add(d => d.IsOpen, true));

        // focusFirstInput takes [autofocus] first, so this is the button that ends up focused.
        var focused = cut.FindAll("[autofocus]");
        Assert.Single(focused);
        Assert.Equal("Cancel", focused[0].TextContent.Trim());
        JSInterop.VerifyInvoke("focusFirstInput");
    }

    [Fact]
    public void Escape_Cancels()
    {
        var cancelled = false;
        var confirmed = false;
        var cut = Render<ConfirmationDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.OnClose, () => cancelled = true)
            .Add(d => d.OnConfirm, () => confirmed = true));

        cut.Find(".kh-dialog").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(cancelled);
        Assert.False(confirmed);
    }

    [Fact]
    public void AnotherKey_LeavesItOpen()
    {
        var closed = false;
        var cut = Render<ConfirmationDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.OnClose, () => closed = true));

        cut.Find(".kh-dialog").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.False(closed);
    }

    /// <summary>A dialog that did not ask for Esc carries no key handler at all, so typing in one
    /// never crosses the circuit and a ComboBox's own Esc cannot close the dialog around it.</summary>
    [Fact]
    public void ADialogNotAskingForEscape_HasNoKeyHandler()
    {
        var cut = Render<Dialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        Assert.Throws<MissingEventHandlerException>(
            () => cut.Find(".kh-dialog").KeyDown(new KeyboardEventArgs { Key = "Escape" }));
    }
}
