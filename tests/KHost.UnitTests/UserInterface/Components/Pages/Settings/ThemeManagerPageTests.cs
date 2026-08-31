using Bunit;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>
/// The switches and the clone button are the page's whole point, so they are clicked rather than
/// called: a handler wired to nothing passes every test that invokes it directly.
/// </summary>
public class ThemeManagerPageTests : BunitContext
{
    private const string SwitchSelector = ".kh-switch";
    private const string CloneSelector = ".kh-theme-manager__clone-btn";
    private const string EditSelector = ".kh-theme-manager__edit-btn";
    private const string DeleteSelector = ".kh-theme-manager__delete-btn";

    private readonly IThemeService _themes = Substitute.For<IThemeService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public ThemeManagerPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _themes.CurrentTheme.Returns("grape");
        _themes.AllThemes.Returns(
        [
            new ThemeDefinition { Id = "grape", Name = "Grape", IsBuiltIn = true, IsEnabled = true },
            new ThemeDefinition { Id = "cherry", Name = "Cherry", IsBuiltIn = true, IsEnabled = true },
            new ThemeDefinition { Id = "night-shift", Name = "Night Shift", IsBuiltIn = false, IsEnabled = false }
        ]);

        Services.AddSingleton(_themes);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void Render_ListsEveryThemeIncludingDisabledOnes()
    {
        var page = Render<ThemeManagerPage>();

        Assert.Equal(3, page.FindAll("tbody tr").Count);
        Assert.Contains("Night Shift", page.Markup);

        // The column header carries the wording, so state reaches a reader through aria-checked
        // rather than a label beside each switch.
        Assert.Equal(["true", "true", "false"],
            page.FindAll(SwitchSelector).Select(s => s.GetAttribute("aria-checked")));
    }

    [Fact]
    public void Render_MarksTheThemeInUse()
    {
        var page = Render<ThemeManagerPage>();

        var inUse = page.FindAll(".kh-theme-manager__in-use");

        Assert.Single(inUse);
        Assert.Contains("Grape", page.FindAll("tbody tr")[0].TextContent);
    }

    [Fact]
    public void ClickingTheSwitch_OnADisabledTheme_EnablesIt()
    {
        var page = Render<ThemeManagerPage>();

        page.FindAll(SwitchSelector)[2].Click();

        _themes.Received(1).SetEnabledAsync("night-shift", true);
    }

    [Fact]
    public void ClickingTheSwitch_OnAnEnabledTheme_DisablesIt()
    {
        var page = Render<ThemeManagerPage>();

        page.FindAll(SwitchSelector)[1].Click();

        _themes.Received(1).SetEnabledAsync("cherry", false);
    }

    [Fact]
    public void TheSwitch_ForTheThemeInUse_IsDisabled()
    {
        var page = Render<ThemeManagerPage>();

        var inUseSwitch = page.FindAll(SwitchSelector)[0];

        Assert.True(inUseSwitch.HasAttribute("disabled"));
        Assert.Contains("Switch to another theme", inUseSwitch.GetAttribute("title"));
    }

    [Fact]
    public void TheSwitch_ForTheLastEnabledTheme_IsDisabled()
    {
        _themes.CurrentTheme.Returns("night-shift");
        _themes.AllThemes.Returns(
        [
            new ThemeDefinition { Id = "grape", Name = "Grape", IsBuiltIn = true, IsEnabled = true },
            new ThemeDefinition { Id = "night-shift", Name = "Night Shift", IsBuiltIn = false, IsEnabled = false }
        ]);

        var page = Render<ThemeManagerPage>();

        Assert.True(page.FindAll(SwitchSelector)[0].HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingClone_CopiesTheTheme()
    {
        var page = Render<ThemeManagerPage>();

        page.FindAll(CloneSelector)[1].Click();

        _themes.Received(1).CloneAsync("cherry");
    }

    [Fact]
    public void Clone_IsOfferedForABuiltInTheme()
    {
        var page = Render<ThemeManagerPage>();

        Assert.False(page.FindAll(CloneSelector)[0].HasAttribute("disabled"));
    }

    [Fact]
    public void EditAndDelete_AreRefusedForABuiltInTheme()
    {
        var page = Render<ThemeManagerPage>();

        Assert.True(page.FindAll(EditSelector)[0].HasAttribute("disabled"));
        Assert.True(page.FindAll(DeleteSelector)[0].HasAttribute("disabled"));
        Assert.False(page.FindAll(EditSelector)[2].HasAttribute("disabled"));
        Assert.False(page.FindAll(DeleteSelector)[2].HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingEdit_OnACustomTheme_OpensTheEditor()
    {
        var page = Render<ThemeManagerPage>();

        page.FindAll(EditSelector)[2].Click();

        _dialogs.Received(1).RequestEditAsync(
            Arg.Is<ThemeDefinition>(t => t.Id == "night-shift"),
            Arg.Any<Func<ThemeDefinition?, Task>>(),
            Arg.Any<Action?>(),
            Arg.Any<Action?>());
    }

    [Fact]
    public void ClickingAddTheme_OpensTheEditorWithNoTheme()
    {
        var page = Render<ThemeManagerPage>();

        page.Find(".kh-card__header .kh-button--primary").Click();

        _dialogs.Received(1).RequestEditAsync(
            (ThemeDefinition?)null,
            Arg.Any<Func<ThemeDefinition?, Task>>(),
            Arg.Any<Action?>(),
            Arg.Any<Action?>());
    }

    [Fact]
    public void ClickingDelete_AsksBeforeDeleting()
    {
        var page = Render<ThemeManagerPage>();

        page.FindAll(DeleteSelector)[2].Click();

        _dialogs.Received(1).ShowConfirmationAsync(
            Arg.Is<string>(m => m.Contains("Night Shift")),
            Arg.Any<Func<Task>>(),
            "Delete Theme",
            "Delete",
            Arg.Any<Action?>(),
            Arg.Any<Action?>());

        _themes.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SavingANewTheme_GivesItAnIdBeforeStoringIt()
    {
        _themes.BuildId("Night Shift", Arg.Any<string?>()).Returns("night-shift");

        var page = Render<ThemeManagerPage>();
        page.Find(".kh-card__header .kh-button--primary").Click();

        var onSave = (Func<ThemeDefinition?, Task>)_dialogs.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IDialogService.RequestEditAsync))
            .GetArguments()[1]!;

        await onSave(new ThemeDefinition { Id = "", Name = "Night Shift" });

        _ = _themes.Received(1).SaveAsync(Arg.Is<ThemeDefinition>(t => t.Id == "night-shift"));
    }

    [Fact]
    public void AThemesChangedAnnouncement_RedrawsTheList()
    {
        var page = Render<ThemeManagerPage>();

        _themes.AllThemes.Returns(
        [
            new ThemeDefinition { Id = "grape", Name = "Grape", IsBuiltIn = true, IsEnabled = true }
        ]);

        _broker.Announce(new ThemesChanged());

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
    }
}
