using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageNewVenueBackgroundsTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { NewVenueBackgrounds = VenueBackgrounds.Advanced };

    public AppSettingsPageNewVenueBackgroundsTests()
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
        Services.AddBreakMusicSection();
        Services.AddSearchSection();
        Services.AddNewVenuesSection(_card);
    }

    private readonly Media _card = new() { FilePath = "/karaoke/Images/card.png", Title = "Card", Type = MediaType.Image };

    [Fact]
    public void NewVenueBackgrounds_OffersBothPlaylists_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#new-venue-backgrounds option");

        Assert.Equal(["Basic", "Advanced"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["Basic Backgrounds", "Advanced Backgrounds"], options.Select(o => o.TextContent));
        Assert.Equal("Advanced", page.Find("select#new-venue-backgrounds").GetAttribute("value"));
    }

    [Fact]
    public async Task NewVenueBackgrounds_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#new-venue-backgrounds").Change("Basic");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.NewVenueBackgrounds == VenueBackgrounds.Basic));
    }

    [Fact]
    public async Task PlaceholderImage_AStillPicked_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find(".kh-media-picker .kh-combobox__input").Focus();
        page.FindAll(".kh-combobox__option").Single(o => o.TextContent.Trim() == "Card").Click();
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.NewVenuePlaceholderImageId == _card.Id));
    }

    [Fact]
    public void PlaceholderScaling_IsOfferedOnlyOnceThereIsAnImage()
    {
        Assert.Empty(Render<AppSettingsPage>().FindAll("#new-venue-placeholder-scaling"));

        _stored.NewVenuePlaceholderImageId = _card.Id;
        _stored.NewVenuePlaceholderImageScaling = ImageScaling.Original;

        Assert.Equal(["Original"], Render<AppSettingsPage>().FindAll("#new-venue-placeholder-scaling option")
            .Where(o => o.HasAttribute("selected")).Select(o => o.GetAttribute("value")));
    }

    [Fact]
    public async Task PlaceholderScaling_AChoiceMade_IsWhatSaveWrites()
    {
        _stored.NewVenuePlaceholderImageId = _card.Id;
        var page = Render<AppSettingsPage>();

        page.Find("#new-venue-placeholder-scaling").Change("Fill");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.NewVenuePlaceholderImageScaling == ImageScaling.Fill));
    }

    /// <summary>The block's own label names the picker, so the editor's would only repeat it.</summary>
    [Fact]
    public void PlaceholderImage_IsNamedOnce()
    {
        var page = Render<AppSettingsPage>();

        Assert.Empty(page.FindAll(".kh-placeholder-image .kh-form-label").Where(l => l.TextContent.Trim() == "Placeholder image"));
        Assert.Contains("Placeholder image", page.Find(".kh-app-settings__stacked .kh-app-settings__labelled").TextContent);
    }

    [Fact]
    public void PlaceholderImage_ShowsTheStoredOne()
    {
        _stored.NewVenuePlaceholderImageId = _card.Id;

        var page = Render<AppSettingsPage>();

        Assert.Equal("Card", page.Find(".kh-media-picker .kh-combobox__input").GetAttribute("value"));
    }
}
