using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>One library row picked by id, with Browse beside it adding a file from the host's disk.</summary>
public class MediaPickerTests : BunitContext
{
    private static readonly byte[] Bytes = [0x89, 0x50, 0x4E, 0x47];

    private readonly IMediaUploader _uploader = Substitute.For<IMediaUploader>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly Media _old = new() { FilePath = "/karaoke/Images/old.png", Title = "Old card", Type = MediaType.Image };
    private readonly Media _added = new() { FilePath = "/karaoke/Images/logo.png", Title = "logo", Type = MediaType.Image };

    public MediaPickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _media.ReadAllByTypesAsync(Arg.Any<MediaType[]>()).Returns(new List<Media> { _old });
        _uploader.ExtensionsFor(Arg.Any<IEnumerable<MediaType>>()).Returns([".png", ".jpg"]);
        _uploader.TypeFor(Arg.Any<string>(), Arg.Any<IEnumerable<MediaType>>())
            .Returns(ci => ci.ArgAt<string>(0).EndsWith(".png") ? MediaType.Image : null);
        _uploader.MaxBytesFor(MediaType.Image).Returns(1024);
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns(_added);

        Services.AddSingleton(_media);
        Services.AddSingleton(_uploader);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void Opening_ReadsOnlyTheTypesAsked()
    {
        Render(types: [MediaType.Video, MediaType.Audio]);

        _media.Received(1).ReadAllByTypesAsync(Arg.Is<MediaType[]>(t => t.SequenceEqual(new[] { MediaType.Video, MediaType.Audio })));
    }

    [Fact]
    public void Opening_ShowsThePickedRow()
        => Assert.Equal("Old card", Search(Render(mediaId: _old.Id)).GetAttribute("value"));

    [Fact]
    public void PickingARow_ReportsItsId()
    {
        Guid? picked = null;
        var cut = Render(onChanged: id => picked = id);

        Search(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(o => o.TextContent.Trim() == "Old card").Click();

        Assert.Equal(_old.Id, picked);
    }

    /// <summary>A Reset or Duplicate can move the id after the rows are read.</summary>
    [Fact]
    public void TheIdChangingFromOutside_ShowsTheNewRow()
    {
        var cut = Render();

        cut.Render(ps => ps.Add(p => p.MediaId, _old.Id));

        Assert.Equal("Old card", Search(cut).GetAttribute("value"));
    }

    [Fact]
    public void Browsing_AFile_AddsItAsTheTypeItFitsAndReportsItsId()
    {
        Guid? picked = null;
        var cut = Render(onChanged: id => picked = id);

        Browse(cut, "logo.png", Bytes);

        cut.WaitForAssertion(() => Assert.Equal(_added.Id, picked));
        _uploader.Received(1).AddAsync("logo.png", Arg.Any<Stream>(), MediaType.Image, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Browsing_AFile_OffersItInTheList()
    {
        var cut = Render();

        Browse(cut, "logo.png", Bytes);
        cut.WaitForAssertion(() => Assert.Equal("logo", Search(cut).GetAttribute("value")));
        Search(cut).Input("");
        Search(cut).Focus();

        Assert.Equal(["logo", "Old card"], Options(cut));
    }

    /// <summary>A file already copied in hands back its existing row; it is listed once.</summary>
    [Fact]
    public void Browsing_AFileAlreadyInTheLibrary_ListsItOnce()
    {
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns(_old);
        var cut = Render();

        Browse(cut, "old.png", Bytes);
        cut.WaitForAssertion(() => Assert.Equal("Old card", Search(cut).GetAttribute("value")));
        Search(cut).Input("");
        Search(cut).Focus();

        Assert.Equal(["Old card"], Options(cut));
    }

    /// <summary>The system picker's filter can be switched off; the name is asked again.</summary>
    [Fact]
    public void Browsing_AFileOfAnotherType_SaysSoWithoutReadingIt()
    {
        var cut = Render();

        Browse(cut, "song.mp3", Bytes);

        cut.WaitForAssertion(() => _flash.Received(1).Show(Arg.Is<string>(t => t.Contains("cannot be picked here")), FlashType.Warning));
        _uploader.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default, default);
    }

    [Fact]
    public void Browsing_AFileTooLarge_SaysSoWithoutReadingIt()
    {
        var cut = Render();

        Browse(cut, "poster.png", new byte[2048]);

        cut.WaitForAssertion(() => _flash.Received(1).Show(Arg.Is<string>(t => t.Contains("too large")), FlashType.Warning));
        _uploader.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default, default);
    }

    [Fact]
    public void Browsing_AFileTheLibraryRefuses_SaysWhyAndKeepsThePick()
    {
        _uploader.AddAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns<Media>(_ => throw new InvalidOperationException("the disk is full"));
        var changed = false;
        var cut = Render(mediaId: _old.Id, onChanged: _ => changed = true);

        Browse(cut, "logo.png", Bytes);

        cut.WaitForAssertion(() => _flash.Received(1).Show(Arg.Is<string>(t => t.Contains("the disk is full")), FlashType.Warning));
        Assert.False(changed);
        Assert.Equal("Old card", Search(cut).GetAttribute("value"));
    }

    [Fact]
    public void Browse_FiltersTheSystemPickerToWhatTheTypesAllow()
    {
        _uploader.ExtensionsFor(Arg.Is<IEnumerable<MediaType>>(t => t.SequenceEqual(new[] { MediaType.Video, MediaType.Audio })))
            .Returns([".mp4", ".mp3"]);

        var cut = Render(types: [MediaType.Video, MediaType.Audio]);

        Assert.Equal(".mp4,.mp3", cut.Find("#picker-browse").GetAttribute("accept"));
    }

    /// <summary>Karaoke alone has nothing a single picked file could be.</summary>
    [Fact]
    public void Browse_IsHiddenWhenNoTypeCanBeAddedFromAFile()
    {
        _uploader.ExtensionsFor(Arg.Any<IEnumerable<MediaType>>()).Returns([]);

        Assert.Empty(Render(types: [MediaType.Karaoke]).FindAll("#picker-browse"));
    }

    [Fact]
    public void Browse_IsHiddenWhenTheCallerTurnsItOff()
        => Assert.Empty(Render(allowBrowse: false).FindAll("#picker-browse"));

    private static void Browse(IRenderedComponent<MediaPicker> cut, string name, byte[] bytes)
        => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

    private static AngleSharp.Dom.IElement Search(IRenderedComponent<MediaPicker> cut)
        => cut.Find(".kh-combobox__input");

    private static List<string> Options(IRenderedComponent<MediaPicker> cut)
        => [.. cut.FindAll(".kh-combobox__option").Select(option => option.TextContent.Trim())];

    private IRenderedComponent<MediaPicker> Render(
        MediaType[]? types = null, Guid? mediaId = null, Action<Guid?>? onChanged = null, bool allowBrowse = true)
        => Render<MediaPicker>(ps => ps
            .Add(p => p.Types, types ?? [MediaType.Image])
            .Add(p => p.MediaId, mediaId)
            .Add(p => p.AllowBrowse, allowBrowse)
            .Add(p => p.BrowseId, "picker-browse")
            .Add(p => p.MediaIdChanged, EventCallback.Factory.Create<Guid?>(this, id => onChanged?.Invoke(id))));
}
