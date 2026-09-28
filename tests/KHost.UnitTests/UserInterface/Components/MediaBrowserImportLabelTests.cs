using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>The Import button counts what it will take. Written as Import@Name, Razor reads an email
/// address and prints the text "Import@ImportSelectionSummary" instead.</summary>
public class MediaBrowserImportLabelTests : BunitContext
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "khost-media-browser-label-tests-" + Guid.NewGuid());

    private readonly IMediaImportService _importService = Substitute.For<IMediaImportService>();
    private readonly IMediaRepository _mediaRepository = Substitute.For<IMediaRepository>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public MediaBrowserImportLabelTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "song.cdg"), "");
        File.WriteAllText(Path.Combine(_tempDir, "song.mp3"), "");

        _importService.SupportedExtensions.Returns(new List<string> { ".cdg", ".mp3" });
        _importService.TypeOverrides.Returns(new Dictionary<string, MediaType>());
        _mediaRepository.GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new HashSet<string>());

        Services.AddSingleton(_importService);
        Services.AddSingleton(_mediaRepository);
        Services.AddSingleton(Substitute.For<IMediaFileParsingService>());
        Services.AddSingleton<IMessageBroker>(_broker);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ImportButton_NothingSelected_ReadsImport()
    {
        try
        {
            var cut = Render<MediaBrowser>();

            Assert.Equal("Import", cut.Find(".kh-split-btn__primary").TextContent.Trim());
        }
        finally
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ImportButton_ACdgPairSelected_CountsBothFiles()
    {
        try
        {
            var cut = Render<MediaBrowser>();

            cut.Find("button[title='Edit path']").Click();
            cut.Find(".kh-media-importer__path-input").Input(_tempDir);
            cut.Find(".kh-media-importer__path-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
            cut.WaitForAssertion(() => Assert.Contains("CDG + MP3", cut.Markup));

            cut.Find("thead .kh-media-importer__check").Click();

            cut.WaitForAssertion(() =>
                Assert.Equal("Import (2 files)", cut.Find(".kh-split-btn__primary").TextContent.Trim()));
        }
        finally
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }
}
