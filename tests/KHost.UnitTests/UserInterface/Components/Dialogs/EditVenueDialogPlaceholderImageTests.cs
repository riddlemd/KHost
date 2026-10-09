using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.QrCodes;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The placeholder image under General: a still from the library, or one browsed for.</summary>
public class EditVenueDialogPlaceholderImageTests : BunitContext
{
    private readonly IMediaUploader _uploader = Substitute.For<IMediaUploader>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly Media _card = new() { FilePath = "/karaoke/Images/card.png", Title = "Card", Type = MediaType.Image };
    private readonly Media _logo = new() { FilePath = "/karaoke/Images/logo.png", Title = "logo", Type = MediaType.Image };

    public EditVenueDialogPlaceholderImageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAppSettings();

        var mediaPools = Substitute.For<IMediaPoolService>();
        mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>()).Returns(new List<MediaPool>());
        var breakMusic = Substitute.For<IBreakMusicService>();
        breakMusic.Providers.Returns(new List<IBreakMusicProvider>());
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);

        _media.ReadAllByTypesAsync(Arg.Any<MediaType[]>()).Returns(new List<Media> { _card });
        _uploader.ExtensionsFor(Arg.Any<IEnumerable<MediaType>>()).Returns([".png"]);
        _uploader.TypeFor(Arg.Any<string>(), Arg.Any<IEnumerable<MediaType>>()).Returns(MediaType.Image);
        _uploader.MaxBytesFor(MediaType.Image).Returns(1024);
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns(_logo);

        Services.AddSingleton(breakMusic);
        Services.AddSingleton(mediaPools);
        Services.AddSingleton(visualisations);
        Services.AddSingleton(_media);
        Services.AddSingleton(_uploader);
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(plugins);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void ThePicker_OffersStillsOnly()
    {
        Render();

        _media.Received().ReadAllByTypesAsync(Arg.Is<MediaType[]>(t => t.SequenceEqual(new[] { MediaType.Image })));
        _uploader.Received().ExtensionsFor(Arg.Is<IEnumerable<MediaType>>(t => t.SequenceEqual(new[] { MediaType.Image })));
    }

    [Fact]
    public void PickingAStill_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(venue => saved = venue);

        Picker(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(o => o.TextContent.Trim() == "Card").Click();
        cut.Find("form").Submit();

        Assert.Equal(_card.Id, saved!.Settings.BrandingImageMediaId);
    }

    [Fact]
    public void BrowsingForAStill_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(venue => saved = venue);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "logo.png"));
        cut.WaitForAssertion(() => Assert.Equal("logo", Picker(cut).GetAttribute("value")));
        cut.Find("form").Submit();

        Assert.Equal(_logo.Id, saved!.Settings.BrandingImageMediaId);
    }

    /// <summary>How a still fills the screen means nothing until there is one.</summary>
    [Fact]
    public void HowItFills_AppearsOnlyOnceAStillIsPicked()
    {
        var cut = Render();
        Assert.Empty(cut.FindAll("#venue-branding-scaling"));

        Picker(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(o => o.TextContent.Trim() == "Card").Click();

        Assert.NotNull(cut.Find("#venue-branding-scaling"));
    }

    [Fact]
    public void ANewVenue_StartsWithTheImageAppSettingsNames()
    {
        Services.AddAppSettings(new KHost.UserInterface.Services.AppSettings
        {
            NewVenuePlaceholderImageId = _card.Id,
            NewVenuePlaceholderImageScaling = ImageScaling.Fill,
        });

        var cut = Render<EditVenueDialog>(ps => ps.Add(p => p.IsOpen, true).Add(p => p.Venue, (Venue?)null));

        Assert.Equal("Card", Picker(cut).GetAttribute("value"));
        Assert.Equal(["Fill"], cut.FindAll("#venue-branding-scaling option").Where(o => o.HasAttribute("selected")).Select(o => o.GetAttribute("value")));
    }

    /// <summary>The dialog previews the picture as the screen draws it, as App Settings and the wizard do.</summary>
    [Fact]
    public void TheVenuesStill_IsPreviewedWithItsOwnScaling()
    {
        var cut = Render<EditVenueDialog>(ps => ps.Add(p => p.IsOpen, true).Add(p => p.Venue, new Venue
        {
            Name = "The Lounge",
            Settings = new Venue.VenueSettings { BrandingImageMediaId = _card.Id, BrandingImageScaling = ImageScaling.Fill },
        }));

        var image = cut.Find(".kh-placeholder-image__image");
        Assert.Equal($"/media/image/{_card.Id}", image.GetAttribute("src"));
        Assert.Contains("kh-placeholder-image__image--fill", image.ClassList);
        Assert.Equal(["Fill"], cut.FindAll("#venue-branding-scaling option").Where(o => o.HasAttribute("selected")).Select(o => o.GetAttribute("value")));
    }

    [Fact]
    public void PickingAScaling_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(venue => saved = venue);

        Picker(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(o => o.TextContent.Trim() == "Card").Click();
        cut.Find("#venue-branding-scaling").Change("Stretch");
        cut.Find("form").Submit();

        Assert.Equal((_card.Id, ImageScaling.Stretch), (saved!.Settings.BrandingImageMediaId, saved.Settings.BrandingImageScaling));
    }

    private static AngleSharp.Dom.IElement Picker(IRenderedComponent<EditVenueDialog> cut)
        => cut.Find(".kh-media-picker .kh-combobox__input");

    private IRenderedComponent<EditVenueDialog> Render(Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps =>
        {
            ps.Add(p => p.IsOpen, true)
              .Add(p => p.Venue, new Venue { Name = "The Lounge" });

            if (onSave is not null)
                ps.Add(p => p.OnSave, onSave);
        });
}
