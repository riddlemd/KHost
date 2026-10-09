using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UnitTests.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Components.Setup;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Setup;

public class WizardStepPlaceholderImageTests : BunitContext
{
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly Media _card = new() { FilePath = "/karaoke/Images/card.png", Title = "Card", Type = MediaType.Image, ImageScaling = ImageScaling.Stretch };
    private IMediaService _media = default!;

    public WizardStepPlaceholderImageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _appSettings.Current.Returns(new AppSettings());
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(Substitute.For<IFlashService>());
        _media = Services.AddNewVenuesSection(_card);
        _media.ReadAsync(_card.Id).Returns(_card);
    }

    [Fact]
    public void NothingStored_PicksNone_AndShowsNoImage()
    {
        var cut = Render<WizardStepPlaceholderImage>();

        Assert.Equal("", Picker(cut).GetAttribute("value"));
        Assert.Empty(cut.FindAll(".kh-placeholder-image__image"));
        Assert.Empty(cut.FindAll("#wizard-placeholder-scaling"));
    }

    [Fact]
    public void ThePicker_IsLabelled()
        => Assert.Contains(Render<WizardStepPlaceholderImage>().FindAll(".kh-placeholder-image .kh-form-label"), l => l.TextContent.Trim() == "Placeholder image");

    [Fact]
    public void TheStoredImage_IsPicked_AndPreviewedFromTheHostsImageRoute()
    {
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id });

        var cut = Render<WizardStepPlaceholderImage>();

        Assert.Equal("Card", Picker(cut).GetAttribute("value"));
        Assert.Equal($"/media/image/{_card.Id}", cut.Find(".kh-placeholder-image__image").GetAttribute("src"));
    }

    [Fact]
    public async Task Next_SavesThePickedImage_ThenMovesOn()
    {
        var completed = false;
        var cut = Render<WizardStepPlaceholderImage>(ps => ps.Add(p => p.OnComplete, () => completed = true));

        Picker(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(o => o.TextContent.Trim() == "Card").Click();
        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        await _appSettings.Received(1).SaveNewVenuePlaceholderImageAsync(_card.Id, null);
        Assert.True(completed);
    }

    [Fact]
    public void TheStoredScaling_IsChosen_AndThePreviewDrawsIt()
    {
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id, NewVenuePlaceholderImageScaling = ImageScaling.Fill });

        var cut = Render<WizardStepPlaceholderImage>();

        Assert.Equal(["Fill"], cut.FindAll("#wizard-placeholder-scaling option").Where(o => o.HasAttribute("selected")).Select(o => o.GetAttribute("value")));
        Assert.Contains("kh-placeholder-image__image--fill", cut.Find(".kh-placeholder-image__image").ClassList);
    }

    /// <summary>With no choice of its own the preview draws the picture as the library says it fills.</summary>
    [Fact]
    public void NoScalingChosen_PreviewsThePicturesOwn()
    {
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id });

        var cut = Render<WizardStepPlaceholderImage>();

        Assert.Equal([""], cut.FindAll("#wizard-placeholder-scaling option").Where(o => o.HasAttribute("selected")).Select(o => o.GetAttribute("value")));
        Assert.Contains("kh-placeholder-image__image--stretch", cut.Find(".kh-placeholder-image__image").ClassList);
    }

    [Fact]
    public async Task Next_SavesThePickedScaling_WithTheImage()
    {
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id });
        var cut = Render<WizardStepPlaceholderImage>();

        cut.Find("#wizard-placeholder-scaling").Change("Original");
        Assert.Contains("kh-placeholder-image__image--original", cut.Find(".kh-placeholder-image__image").ClassList);
        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        await _appSettings.Received(1).SaveNewVenuePlaceholderImageAsync(_card.Id, ImageScaling.Original);
    }

    /// <summary>The preview stands for a 1920-wide screen: a 960-wide picture at its own size covers half.</summary>
    [Fact]
    public async Task Original_DrawsThePictureAtItsShareOfTheScreen_OnceItsWidthIsKnown()
    {
        JSInterop.Setup<int>("Reflect.get", _ => true).SetResult(960);
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id, NewVenuePlaceholderImageScaling = ImageScaling.Original });
        var cut = Render<WizardStepPlaceholderImage>();
        Assert.Null(cut.Find(".kh-placeholder-image__image").GetAttribute("style"));

        await cut.Find(".kh-placeholder-image__image").TriggerEventAsync("onload", new Microsoft.AspNetCore.Components.Web.ProgressEventArgs());

        Assert.Equal("width: 50%", cut.Find(".kh-placeholder-image__image").GetAttribute("style"));
    }

    /// <summary>Only Original sizes the picture itself; every other choice fills the screen's box.</summary>
    [Fact]
    public async Task AnyOtherScaling_LeavesThePictureToTheBox()
    {
        JSInterop.Setup<int>("Reflect.get", _ => true).SetResult(960);
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id, NewVenuePlaceholderImageScaling = ImageScaling.Fill });
        var cut = Render<WizardStepPlaceholderImage>();

        await cut.Find(".kh-placeholder-image__image").TriggerEventAsync("onload", new Microsoft.AspNetCore.Components.Web.ProgressEventArgs());

        Assert.Null(cut.Find(".kh-placeholder-image__image").GetAttribute("style"));
    }

    /// <summary>A scaling kept with no image to scale would surprise the next venue that gets one.</summary>
    [Fact]
    public async Task Next_WithTheImageCleared_SavesNoScalingEither()
    {
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = _card.Id, NewVenuePlaceholderImageScaling = ImageScaling.Fill });
        var cut = Render<WizardStepPlaceholderImage>();

        Picker(cut).Input("");
        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        await _appSettings.Received(1).SaveNewVenuePlaceholderImageAsync(null, null);
    }

    /// <summary>The step is optional: moving on with nothing picked saves none, a blank screen.</summary>
    [Fact]
    public async Task Next_WithNothingPicked_SavesNone()
    {
        var cut = Render<WizardStepPlaceholderImage>();

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        await _appSettings.Received(1).SaveNewVenuePlaceholderImageAsync(null, null);
    }

    [Fact]
    public async Task Next_ASaveThatFails_SaysSoAndStays()
    {
        _appSettings.SaveNewVenuePlaceholderImageAsync(Arg.Any<Guid?>(), Arg.Any<ImageScaling?>()).Returns(Task.FromException(new IOException("disk full")));
        var completed = false;
        var cut = Render<WizardStepPlaceholderImage>(ps => ps.Add(p => p.OnComplete, () => completed = true));

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        Assert.Contains("disk full", cut.Find(".kh-alert--danger").TextContent);
        Assert.False(completed);
    }

    private static AngleSharp.Dom.IElement Picker(IRenderedComponent<WizardStepPlaceholderImage> cut)
        => cut.Find(".kh-media-picker .kh-combobox__input");
}
