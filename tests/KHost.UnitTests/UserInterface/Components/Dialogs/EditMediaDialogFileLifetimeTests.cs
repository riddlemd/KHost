using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What a provider asked the host to do with a file, and the state of a row whose file is gone.</summary>
public class EditMediaDialogFileLifetimeTests : BunitContext
{
    private const string Tag = ".kh-media-edit-dialog__tag";

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();

    public EditMediaDialogFileLifetimeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _search.Providers.Returns([]);
        Services.AddSingleton(_search);
    }

    private IRenderedComponent<EditMediaDialog> Render(Media media) => Render<EditMediaDialog>(p => p
        .Add(d => d.IsOpen, true)
        .Add(d => d.Media, media));

    private static Media Song(MediaStatus status = MediaStatus.Ready, bool ephemeral = false, bool singleUse = false) => new()
    {
        Title = "Africa", FilePath = "/karaoke/youtube/abc.mp4", Source = "YouTube",
        Status = status, IsEphemeral = ephemeral, IsSingleUse = singleUse,
    };

    [Theory]
    [InlineData(true, false, new[] { "Ephemeral" })]
    [InlineData(false, true, new[] { "Single Use" })]
    [InlineData(true, true, new[] { "Ephemeral", "Single Use" })]
    public void Render_AMarkedRow_ShowsATagPerFlag(bool ephemeral, bool singleUse, string[] tags)
    {
        var cut = Render(Song(ephemeral: ephemeral, singleUse: singleUse));

        Assert.Equal(tags, cut.FindAll(Tag).Select(t => t.TextContent.Trim()));
    }

    [Theory]
    [InlineData(true, false, "deleted when KHost closes")]
    [InlineData(false, true, "deleted once the song has been sung")]
    public void Render_EachTag_ExplainsItselfInAHint(bool ephemeral, bool singleUse, string says)
    {
        var cut = Render(Song(ephemeral: ephemeral, singleUse: singleUse));

        Assert.Contains(says, cut.Find(".kh-hint .kh-hint__text").TextContent);
        Assert.NotNull(cut.Find(".kh-hint").QuerySelector(Tag));
    }

    /// <summary>A scanned file is the host's own and never has these, so the field is not there at all.</summary>
    [Fact]
    public void Render_AnUnmarkedRow_HasNoTags()
    {
        var cut = Render(Song());

        Assert.Empty(cut.FindAll(Tag));
        Assert.DoesNotContain("Tags", cut.Markup);
    }

    [Fact]
    public void Render_ARowWhoseFileWasRemoved_SaysSoAndStrikesThePath()
    {
        var cut = Render(Song(MediaStatus.NotDownloaded, ephemeral: true));

        Assert.Contains("Not downloaded", cut.Find(".kh-badge").TextContent);
        Assert.Contains("kh-media-edit-dialog__path--gone", cut.Find("input[readonly]").ClassList);
        Assert.Contains("downloaded again the next time", cut.Find(".kh-media-edit-dialog__path-note").TextContent);
    }

    /// <summary>There is no file to be broken, and queuing the song is what fetches it again, so the
    /// dialog offers neither a broken flag nor a download of its own.</summary>
    [Fact]
    public void Render_ARowWhoseFileWasRemoved_OffersNoMarkAsBrokenAndNoDownload()
    {
        var cut = Render(Song(MediaStatus.NotDownloaded, ephemeral: true));

        Assert.DoesNotContain("Mark as broken", cut.Markup);
        Assert.DoesNotContain("Download now", cut.Markup);
    }

    [Fact]
    public void Render_ARowWithItsFile_StrikesNothingThrough()
    {
        var cut = Render(Song(ephemeral: true));

        Assert.Empty(cut.FindAll(".kh-media-edit-dialog__path--gone"));
        Assert.Empty(cut.FindAll(".kh-media-edit-dialog__path-note"));
    }
}
