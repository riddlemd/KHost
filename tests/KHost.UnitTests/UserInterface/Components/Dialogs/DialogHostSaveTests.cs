using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
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
    private readonly DialogService _dialogService = new(NullLogger<DialogService>.Instance, []);

    public DialogHostSaveTests()
    {
        // Dialog focuses its first input on open; none of that matters to whether save closes clean.
        JSInterop.Mode = JSRuntimeMode.Loose;

        var themeService = Substitute.For<IThemeService>();
        // Every catalog field fails validation without a usable value; only the real defaults pass.
        themeService.ReadVariablesAsync(Arg.Any<string>()).Returns(ThemeVariableCatalog.Defaults());

        Services.AddSingleton<IDialogService>(_dialogService);
        Services.AddSingleton(themeService);

        // [Inject] is resolved for real here (unlike the reflection-built dialog tests), so every
        // nullable dependency EditUserDialog declares still needs a registration or DI throws.
        var userGroupsService = Substitute.For<IUserGroupsService>();
        // NSubstitute hands back a completed task wrapping null for an unstubbed Task<T> return.
        userGroupsService.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PaginatedResult<KHostUserGroup>());
        Services.AddSingleton(userGroupsService);
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IPerformanceService>());
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton(Substitute.For<IPasswordHasher>());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());
        Services.AddSingleton(appSettings);
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

    /// <summary>The dialog's own CancelAsync used to invoke OnClose itself before also calling
    /// CloseAsync, which invoked it again; DialogHost's onCancel fired twice for one click.</summary>
    [Fact]
    public async Task CancelThroughTheDialogHost_InvokesOnCancelOnce_Theme()
    {
        var host = Render<DialogHost>();
        var cancelCount = 0;

        await _dialogService.RequestEditAsync(
            (ThemeDefinition?)null,
            onSave: _ => Task.CompletedTask,
            onCancel: () => cancelCount++);

        host.Find(".kh-theme-edit-dialog__cancel-btn").Click();

        host.WaitForAssertion(() => Assert.Equal(1, cancelCount));
    }

    [Fact]
    public async Task CancelThroughTheDialogHost_InvokesOnCancelOnce_User()
    {
        var host = Render<DialogHost>();
        var cancelCount = 0;

        await _dialogService.RequestEditAsync(
            (KHostUser?)null,
            onSave: _ => Task.CompletedTask,
            onCancel: () => cancelCount++);

        host.Find(".kh-user-edit-dialog__cancel-btn").Click();

        host.WaitForAssertion(() => Assert.Equal(1, cancelCount));
    }

    [Fact]
    public async Task CancelThroughTheDialogHost_InvokesOnCancelOnce_UserGroup()
    {
        var host = Render<DialogHost>();
        var cancelCount = 0;

        await _dialogService.RequestEditAsync(
            (KHostUserGroup?)null,
            onSave: _ => Task.CompletedTask,
            onCancel: () => cancelCount++);

        host.Find(".kh-user-group-edit-dialog__cancel-btn").Click();

        host.WaitForAssertion(() => Assert.Equal(1, cancelCount));
    }
}
