using System.Reflection;
using KHost.Abstractions.Models;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class EditTipDialogTests
{
    [Fact]
    public void Opening_WithNoTip_IsAnAdd()
    {
        Assert.True(IsNew(Open(null)));
    }

    [Fact]
    public void Opening_WithAnExistingTip_IsAnEdit()
    {
        Assert.False(IsNew(Open(new Tip { UserId = Guid.NewGuid(), AmountInCents = 500 })));
    }

    [Fact]
    public void Opening_WithNoTip_LeavesTheSingerUnchosen()
    {
        Assert.Null(Model(Open(null)).UserId);
    }

    // Guid.Empty has to read as "nothing chosen": [Required] rejects only null.
    [Fact]
    public void Opening_WithABlankTip_LeavesTheSingerUnchosen()
    {
        Assert.Null(Model(Open(new Tip { AmountInCents = 0 })).UserId);
    }

    [Fact]
    public void Opening_WithAnExistingTip_KeepsItsSinger()
    {
        var userId = Guid.NewGuid();

        var model = Model(Open(new Tip { UserId = userId, AmountInCents = 1250 }));

        Assert.Equal(userId, model.UserId);
        Assert.Equal(1250, model.AmountInCents);
    }

    [Fact]
    public void Opening_WithASingerAlreadyChosen_UsesIt()
    {
        var userId = Guid.NewGuid();

        var model = Model(Open(new Tip { AmountInCents = 0 }, lockedUserId: userId));

        Assert.Equal(userId, model.UserId);
    }

    private static EditTipDialog Open(Tip? tip, Guid? lockedUserId = null)
    {
        var dialog = new EditTipDialog();

        // Reflection rather than an initialiser: BL0005 warns on assigning a [Parameter] directly.
        Set(dialog, nameof(EditTipDialog.IsOpen), true);
        Set(dialog, nameof(EditTipDialog.Tip), tip);
        Set(dialog, nameof(EditTipDialog.UserId), lockedUserId);

        typeof(EditTipDialog)
            .GetMethod("OnParametersSet", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null);

        return dialog;
    }

    private static void Set(EditTipDialog dialog, string parameter, object? value)
        => typeof(EditTipDialog).GetProperty(parameter)!.SetValue(dialog, value);

    private static T Field<T>(EditTipDialog dialog, string name)
        => (T)typeof(EditTipDialog)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dialog)!;

    private static EditTipModel Model(EditTipDialog dialog) => Field<EditTipModel>(dialog, "_model");

    private static bool IsNew(EditTipDialog dialog) => Field<bool>(dialog, "_isNew");
}
