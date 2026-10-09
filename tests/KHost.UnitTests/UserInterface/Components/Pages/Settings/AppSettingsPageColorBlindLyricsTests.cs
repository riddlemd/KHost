using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageColorBlindLyricsTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new();

    public AppSettingsPageColorBlindLyricsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddNewVenuesSection();

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
        Services.AddSearchSection();
        Services.AddBreakMusicSection();
    }

    [Fact]
    public void ColorBlindFriendlyLyrics_IsACheckbox_OffByDefault_WithItsNoteUnderTheRow()
    {
        var page = Render<AppSettingsPage>();

        var box = page.Find("input#colour-blind-lyrics");

        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.False(box.HasAttribute("checked"));
        var row = box.Closest(".kh-app-settings__row")!;
        Assert.Null(row.QuerySelector(".kh-note"));
        Assert.Contains("colour-blind", row.NextElementSibling!.QuerySelector(".kh-app-settings__description .kh-note, .kh-note")!.TextContent);
        Assert.Contains("kh-app-settings__description", row.NextElementSibling.ClassList);
    }

    [Fact]
    public async Task ColorBlindFriendlyLyrics_Ticked_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("input#colour-blind-lyrics").Change(true);
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.ColorBlindFriendlyLyrics));
    }
}
