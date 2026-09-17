using System.Reflection;
using KHost.Abstractions.Models;
using KHost.UserInterface.Components.Dialogs;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>
/// Changing the name one turn is announced under. The singer it belongs to is not touched — the
/// name rides on the performance, which is what makes a song-first queue's aliases possible.
/// </summary>
public class SingingAsDialogTests
{
    private readonly Performance _performance = new()
    {
        Id = Guid.NewGuid(),
        SingerId = Guid.NewGuid(),
        MediaId = Guid.NewGuid(),
    };

    [Fact]
    public void Opening_OffersTheNameTheTurnAlreadyCarries()
    {
        _performance.SungAs = "flo";

        Assert.Equal("flo", TypedName(Open(_performance)));
    }

    [Fact]
    public void Opening_ATurnWithNoNameOfItsOwn_StartsBlank()
    {
        _performance.SungAs = null;

        Assert.True(string.IsNullOrEmpty(TypedName(Open(_performance))));
    }

    [Fact]
    public async Task Saving_PutsTheTypedNameOnThePerformance()
    {
        var dialog = Open(_performance);
        Type(dialog, "flo");

        await SaveAsync(dialog);

        Assert.Equal("flo", _performance.SungAs);
    }

    /// <summary>
    /// Blank means "their own name", and null is how every reader spells that — an empty string
    /// would be a recorded name that happens to be empty, which the marquee would compose into a
    /// blank where a singer should be.
    /// </summary>
    [Fact]
    public async Task Saving_ABlankName_ClearsItRatherThanRecordingAnEmptyOne()
    {
        _performance.SungAs = "flo";
        var dialog = Open(_performance);
        Type(dialog, "   ");

        await SaveAsync(dialog);

        Assert.Null(_performance.SungAs);
    }

    [Fact]
    public async Task Saving_TrimsTheNameRatherThanStoringTheSpace()
    {
        var dialog = Open(_performance);
        Type(dialog, "  flo  ");

        await SaveAsync(dialog);

        Assert.Equal("flo", _performance.SungAs);
    }

    /// <summary>The singer is whose turn it is, not what the name is; changing one must not move the other.</summary>
    [Fact]
    public async Task Saving_LeavesTheSingerTheTurnBelongsTo()
    {
        var singerId = _performance.SingerId;
        var dialog = Open(_performance);
        Type(dialog, "flo");

        await SaveAsync(dialog);

        Assert.Equal(singerId, _performance.SingerId);
    }

    // ---- reflection helpers: BL0005 forbids assigning a [Parameter] from outside the framework ----

    private static SingingAsDialog Open(Performance? performance, string? singerName = "Ann")
    {
        var dialog = new SingingAsDialog();

        Set(dialog, nameof(SingingAsDialog.IsOpen), true);
        Set(dialog, nameof(SingingAsDialog.Performance), performance);
        Set(dialog, nameof(SingingAsDialog.SingerName), singerName);

        typeof(SingingAsDialog)
            .GetMethod("OnParametersSet", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null);

        return dialog;
    }

    private static void Set(SingingAsDialog dialog, string parameter, object? value)
        => typeof(SingingAsDialog).GetProperty(parameter)!.SetValue(dialog, value);

    private static object Model(SingingAsDialog dialog)
        => typeof(SingingAsDialog)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dialog)!;

    private static string? TypedName(SingingAsDialog dialog)
        => (string?)Model(dialog).GetType().GetProperty("SungAs")!.GetValue(Model(dialog));

    private static void Type(SingingAsDialog dialog, string? value)
    {
        var model = Model(dialog);
        model.GetType().GetProperty("SungAs")!.SetValue(model, value);
    }

    private static Task SaveAsync(SingingAsDialog dialog)
        => (Task)typeof(SingingAsDialog)
            .GetMethod("SaveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null)!;
}
