using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageSegmentTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { SegmentSeconds = 4 };

    public AppSettingsPageSegmentTests()
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
        Services.AddBreakMusicSection();
        Services.AddSearchSection();
    }

    [Fact]
    public void Segment_OffersOneToTenSeconds_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#segment-seconds option");

        Assert.Equal(Enumerable.Range(1, 10).Select(n => n.ToString()), options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["1 second", "2 seconds"], options.Take(2).Select(o => o.TextContent));
        Assert.Equal("4", page.Find("select#segment-seconds").GetAttribute("value"));
        Assert.DoesNotContain("(seconds)", page.Find("select#segment-seconds").Closest(".kh-app-settings__row")!.TextContent);
    }

    [Fact]
    public async Task Segment_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#segment-seconds").Change("7");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.SegmentSeconds == 7));
    }

    [Fact]
    public void StopFade_OffersNoFadeToThirtySeconds()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#stop-fade option");

        Assert.Equal(["0", "1", "2", "3", "4", "5", "6", "8", "10", "15", "20", "30"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["No fade", "1 second", "2 seconds"], options.Take(3).Select(o => o.TextContent));
        Assert.DoesNotContain("(seconds)", page.Find("select#stop-fade").Closest(".kh-app-settings__row")!.TextContent);
    }
}
