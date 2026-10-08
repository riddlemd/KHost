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

/// <summary>Browse beside the placeholder image: a picture from the host's own disk, added to the
/// library and put on the venue in one step.</summary>
public class EditVenueDialogPlaceholderBrowseTests : BunitContext
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47];

    private readonly IImageUploader _uploader = Substitute.For<IImageUploader>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly Media _existing = new() { FilePath = "/karaoke/Images/old.png", Title = "Old card", Type = MediaType.Image };

    public EditVenueDialogPlaceholderBrowseTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var mediaPools = Substitute.For<IMediaPoolService>();
        mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>()).Returns(new List<MediaPool>());
        var breakMusic = Substitute.For<IBreakMusicService>();
        breakMusic.Providers.Returns(new List<IBreakMusicProvider>());
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);

        _media.ReadAllByTypesAsync(Arg.Any<MediaType[]>()).Returns(new List<Media> { _existing });
        _uploader.MaxBytes.Returns(1024);

        Services.AddSingleton(breakMusic);
        Services.AddSingleton(mediaPools);
        Services.AddSingleton(visualisations);
        Services.AddSingleton(_media);
        Services.AddSingleton(_uploader);
        Services.AddSingleton(_flash);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(plugins);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void Browsing_APicture_PutsItOnTheVenueThatIsSaved()
    {
        var added = new Media { FilePath = "/karaoke/Images/logo.png", Title = "logo", Type = MediaType.Image };
        _uploader.AddAsync("logo.png", Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(added);
        Venue? saved = null;
        var cut = Render(venue => saved = venue);

        Browse(cut, "logo.png", Picture);
        cut.WaitForAssertion(() => Assert.Equal("logo", ImagePicker(cut).GetAttribute("value")));
        cut.Find("form").Submit();

        Assert.Equal(added.Id, saved!.Settings.BrandingImageMediaId);
    }

    /// <summary>The picture just added is offered by the picker too, so a host can switch back to it.</summary>
    [Fact]
    public void Browsing_APicture_OffersItInThePicker()
    {
        var added = new Media { FilePath = "/karaoke/Images/logo.png", Title = "logo", Type = MediaType.Image };
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(added);
        var cut = Render();

        Browse(cut, "logo.png", Picture);
        cut.WaitForAssertion(() => Assert.Equal("logo", ImagePicker(cut).GetAttribute("value")));
        ImagePicker(cut).Input("");
        ImagePicker(cut).Focus();

        Assert.Equal(["logo", "Old card"], Options(cut));
    }

    /// <summary>Picking a picture already copied in hands back its existing row; it is listed once.</summary>
    [Fact]
    public void Browsing_APictureAlreadyInTheLibrary_ListsItOnce()
    {
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(_existing);
        var cut = Render();

        Browse(cut, "old.png", Picture);
        cut.WaitForAssertion(() => Assert.Equal("Old card", ImagePicker(cut).GetAttribute("value")));
        ImagePicker(cut).Input("");
        ImagePicker(cut).Focus();

        Assert.Equal(["Old card"], Options(cut));
    }

    [Fact]
    public void Browsing_APictureTooLarge_SaysSoWithoutReadingIt()
    {
        var cut = Render();

        Browse(cut, "poster.png", new byte[2048]);

        cut.WaitForAssertion(() => _flash.Received(1).Show(Arg.Is<string>(t => t.Contains("too large")), FlashType.Warning));
        _uploader.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default);
    }

    [Fact]
    public void Browsing_AFileTheLibraryRefuses_SaysWhyAndKeepsTheVenuesImage()
    {
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns<Media>(_ => throw new NotSupportedException("song.mp3 is not a picture the screen can show."));
        Venue? saved = null;
        var cut = Render(venue => saved = venue, _existing.Id);

        Browse(cut, "song.mp3", Picture);
        cut.WaitForAssertion(() => _flash.Received(1).Show(
            Arg.Is<string>(t => t.Contains("is not a picture the screen can show")), FlashType.Warning));
        cut.Find("form").Submit();

        Assert.Equal(_existing.Id, saved!.Settings.BrandingImageMediaId);
    }

    /// <summary>The system picker greys out anything the screen could not show.</summary>
    [Fact]
    public void ThePicker_OffersOnlyPictures()
    {
        var accept = Render().Find("#venue-branding-browse").GetAttribute("accept")!.Split(',');

        Assert.Contains(".png", accept);
        Assert.Contains(".jpg", accept);
        Assert.DoesNotContain(".mp3", accept);
        Assert.DoesNotContain(".mp4", accept);
    }

    private static void Browse(IRenderedComponent<EditVenueDialog> cut, string name, byte[] bytes)
        => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

    private static AngleSharp.Dom.IElement ImagePicker(IRenderedComponent<EditVenueDialog> cut)
        => cut.Find(".kh-venue-general__image .kh-combobox__input");

    private static List<string> Options(IRenderedComponent<EditVenueDialog> cut)
        => [.. cut.FindAll(".kh-venue-general__image .kh-combobox__option").Select(option => option.TextContent.Trim())];

    private IRenderedComponent<EditVenueDialog> Render(Action<Venue>? onSave = null, Guid? brandingImage = null)
        => Render<EditVenueDialog>(ps =>
        {
            ps.Add(p => p.IsOpen, true)
              .Add(p => p.Venue, new Venue { Name = "The Lounge", Settings = { BrandingImageMediaId = brandingImage } });

            if (onSave is not null)
                ps.Add(p => p.OnSave, onSave);
        });
}
