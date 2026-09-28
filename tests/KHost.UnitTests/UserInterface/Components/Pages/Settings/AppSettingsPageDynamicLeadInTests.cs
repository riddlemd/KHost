using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageDynamicLeadInTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new();

    public AppSettingsPageDynamicLeadInTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
    }

    [Fact]
    public void DynamicLeadIns_OffersOneToFiveSeconds_WithThreeChosen_AndTheToggleOff()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#dynamic-lead-in-pause option");

        Assert.Equal(["1", "2", "3", "4", "5"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal("1 second", options[0].TextContent.Trim());
        Assert.Equal("3", page.Find("select#dynamic-lead-in-pause").GetAttribute("value"));
        Assert.False(page.Find("input#dynamic-lead-ins").HasAttribute("checked"));
    }

    [Fact]
    public async Task DynamicLeadIns_WhatIsChosen_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("input#dynamic-lead-ins").Change(true);
        page.Find("select#dynamic-lead-in-pause").Change("2");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.DynamicLeadIns && s.DynamicLeadInPauseSeconds == 2));
    }
}
