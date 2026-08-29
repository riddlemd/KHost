using Bunit;
using Bunit.TestDoubles;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>
/// The page edits a copy and only writes it on Save, so navigating away used to drop the changes
/// without a word. Leaving is now a question with two answers, and closing the question is a third.
/// </summary>
public class AppSettingsPageGuardTests : BunitContext
{
    private const string Elsewhere = "http://localhost/settings/media-manager";

    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly IDialogService _dialog = Substitute.For<IDialogService>();
    private readonly AppSettings _stored = new();

    public AppSettingsPageGuardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(_dialog);
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
    }

    [Fact]
    public async Task Leaving_WithNoEdits_AsksNothing()
    {
        var page = Render<AppSettingsPage>();

        await NavigateAsync(page);

        Assert.Equal(Elsewhere, Navigation.Uri);
        await _dialog.DidNotReceive().ShowUnsavedChangesAsync(
            Arg.Any<Func<Task>>(), Arg.Any<Func<Task>>(), Arg.Any<string?>(), Arg.Any<Action?>());
    }

    [Fact]
    public async Task Leaving_WithEdits_HoldsTheNavigationAndAsks()
    {
        var page = Render<AppSettingsPage>();
        Edit(page, stopFadeSeconds: 9);

        await NavigateAsync(page);

        // Held, not cancelled: the host has not answered yet, and the page must still be here
        // for them to answer on.
        Assert.DoesNotContain("media-manager", Navigation.Uri);
        await _dialog.Received(1).ShowUnsavedChangesAsync(
            Arg.Any<Func<Task>>(), Arg.Any<Func<Task>>(), Arg.Any<string?>(), Arg.Any<Action?>());
    }

    [Fact]
    public async Task Saving_WritesTheChangesAndThenLeaves()
    {
        var page = Render<AppSettingsPage>();
        Edit(page, stopFadeSeconds: 9);
        await NavigateAsync(page);

        await AnswerAsync(page, save: true);

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.StopFadeSeconds == 9));
        Assert.Equal(Elsewhere, Navigation.Uri);
    }

    [Fact]
    public async Task Discarding_LeavesWithoutWriting()
    {
        var page = Render<AppSettingsPage>();
        Edit(page, stopFadeSeconds: 9);
        await NavigateAsync(page);

        await AnswerAsync(page, save: false);

        await _settings.DidNotReceive().SaveAsync(Arg.Any<AppSettings>());
        Assert.Equal(Elsewhere, Navigation.Uri);
    }

    [Fact]
    public async Task Discarding_DoesNotAskAgainOnTheWayOut()
    {
        var page = Render<AppSettingsPage>();
        Edit(page, stopFadeSeconds: 9);
        await NavigateAsync(page);

        await AnswerAsync(page, save: false);

        // The edits are still on the model, so without a bypass the guard would catch the very
        // navigation the host just asked for and the page would never let go.
        await _dialog.Received(1).ShowUnsavedChangesAsync(
            Arg.Any<Func<Task>>(), Arg.Any<Func<Task>>(), Arg.Any<string?>(), Arg.Any<Action?>());
    }

    [Fact]
    public async Task ARefusedSave_KeepsTheHostOnThePage()
    {
        _settings.SaveAsync(Arg.Any<AppSettings>())
            .Returns(new AppSettingsSaveResult(false, "No admin user has a password yet"));

        var page = Render<AppSettingsPage>();
        Edit(page, stopFadeSeconds: 9);
        await NavigateAsync(page);

        await AnswerAsync(page, save: true);

        // Carrying them away would hide the reason the settings did not take.
        Assert.DoesNotContain("media-manager", Navigation.Uri);
    }

    [Fact]
    public async Task EditingBackToWhatIsStored_IsNotAChange()
    {
        var page = Render<AppSettingsPage>();
        Edit(page, stopFadeSeconds: 9);
        Edit(page, stopFadeSeconds: _stored.StopFadeSeconds);

        await NavigateAsync(page);

        // Comparing against what is stored, rather than tracking that a field was touched.
        Assert.Equal(Elsewhere, Navigation.Uri);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    /// <summary>Navigates on the renderer's own thread, as a link click in the app would.</summary>
    private Task NavigateAsync(IRenderedComponent<AppSettingsPage> page)
        => page.InvokeAsync(() => Navigation.NavigateTo(Elsewhere));

    /// <summary>Runs the answer the dialog was handed, as the host clicking that button would.</summary>
    private Task AnswerAsync(IRenderedComponent<AppSettingsPage> page, bool save)
    {
        var call = _dialog.ReceivedCalls().Last(c =>
            c.GetMethodInfo().Name == nameof(IDialogService.ShowUnsavedChangesAsync));

        var handler = (Func<Task>)call.GetArguments()[save ? 0 : 1]!;

        return page.InvokeAsync(handler);
    }

    private static void Edit(IRenderedComponent<AppSettingsPage> page, double stopFadeSeconds)
        => page.Find("input[step='0.5']").Change(stopFadeSeconds.ToString());
}
