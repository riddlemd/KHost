using System.Reflection;
using KHost.Abstractions.Models;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class TipsManagerPageTests
{
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly TipsManagerPage _page = new();

    public TipsManagerPageTests()
    {
        typeof(TipsManagerPage)
            .GetProperty("DialogService", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_page, _dialogService);
    }

    // A blank tip standing in for "nothing yet" reads as an edit, and carries Guid.Empty as its
    // singer — the value [Required] cannot reject.
    [Fact]
    public async Task OpenAddDialog_AsksForAnAdd_NotAnEditOfABlankTip()
    {
        await Invoke("OpenAddDialogAsync");

        await _dialogService.Received(1).RequestEditAsync(
            (Tip?)null, Arg.Any<Action<Tip?>>(), Arg.Any<Action?>(), Arg.Any<Action?>());
    }

    private Task Invoke(string method)
        => (Task)typeof(TipsManagerPage)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_page, null)!;
}
