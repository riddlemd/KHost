using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The button inside the File Path field that shows the file in the OS file manager.</summary>
public class EditMediaDialogRevealTests : BunitContext
{
    private const string Button = ".kh-media-edit-dialog__reveal-btn";
    private const string Path = "/karaoke/youtube/abc.mp4";

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly IFileRevealService _reveal = Substitute.For<IFileRevealService>();

    public EditMediaDialogRevealTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _search.Providers.Returns([]);
        _reveal.Label.Returns("Show in Finder");
        Services.AddSingleton(_search);
        Services.AddSingleton(_reveal);
    }

    private IRenderedComponent<EditMediaDialog> Render() => Render<EditMediaDialog>(p => p
        .Add(d => d.IsOpen, true)
        .Add(d => d.Media, new Media { Title = "Africa", FilePath = Path }));

    [Fact]
    public void Click_FileThere_RevealsThatPath()
    {
        _reveal.CanReveal(Path).Returns(true);

        Render().Find(Button).Click();

        _reveal.Received(1).Reveal(Path);
    }

    [Fact]
    public void Render_FileThere_NamesTheActionForThisOs()
    {
        _reveal.CanReveal(Path).Returns(true);

        Assert.Equal("Show in Finder", Render().Find(Button).GetAttribute("title"));
    }

    [Fact]
    public void Render_NothingOnDiskToShow_HasNoButton()
    {
        _reveal.CanReveal(Path).Returns(false);

        Assert.Empty(Render().FindAll(Button));
    }
}
