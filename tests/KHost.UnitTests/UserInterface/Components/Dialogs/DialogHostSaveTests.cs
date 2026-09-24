using Bunit;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>A dialog's own SaveAsync closing itself after OnSave fires DialogHost's OnClose too,
/// which invokes onCancel on top of the save that already succeeded.</summary>
public class DialogHostSaveTests : BunitContext
{
    private readonly DialogService _dialogService = new(NullLogger<DialogService>.Instance, Substitute.For<IScreenLauncher>());

    public DialogHostSaveTests()
    {
        // Dialog focuses its first input on open; none of that matters to whether save closes clean.
        JSInterop.Mode = JSRuntimeMode.Loose;

        var themeService = Substitute.For<IThemeService>();
        // Every catalog field fails validation without a usable value; only the real defaults pass.
        themeService.ReadVariablesAsync(Arg.Any<string>()).Returns(ThemeVariableCatalog.Defaults());

        Services.AddSingleton<IDialogService>(_dialogService);
        Services.AddSingleton(themeService);
    }

    [Fact]
    public async Task SavingThroughTheDialogHost_CompletesAsSavedAndNeverInvokesCancel()
    {
        var host = Render<DialogHost>();

        ThemeDefinition? saved = null;
        var cancelled = false;

        await _dialogService.RequestEditAsync(
            new ThemeDefinition { Id = "t1", Name = "Neon" },
            onSave: theme => { saved = theme; return Task.CompletedTask; },
            onCancel: () => cancelled = true);

        host.Find(".kh-theme-edit-dialog__save-btn").Click();

        host.WaitForAssertion(() => Assert.NotNull(saved));

        Assert.Equal("Neon", saved!.Name);
        Assert.False(cancelled);
    }
}
