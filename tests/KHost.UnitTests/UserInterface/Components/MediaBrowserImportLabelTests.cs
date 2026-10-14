using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
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
        Services.AddSingleton(ReaderWithNoMusicFolder());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ImportButton_NothingSelected_ReadsImport()
    {
        try
        {
            var cut = Render<MediaBrowser>();
            cut.WaitForState(() => cut.FindAll(".kh-media-importer__loading").Count == 0);

            // The header offers Import only once a folder is open.
            cut.Find("button[title='Edit path']").Click();
            cut.Find(".kh-media-importer__path-input").Input(_tempDir);
            cut.Find(".kh-media-importer__path-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

            Assert.Equal("Import", cut.WaitForElement(".kh-split-btn__primary").TextContent.Trim());
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
            // The default folder is asked off the circuit thread; a click landing while it answers is lost.
            cut.WaitForState(() => cut.FindAll(".kh-media-importer__loading").Count == 0);

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

    /// <summary>The real reader, except that no default music folder exists: the browser opens on
    /// the drive list, so nothing is read in the background while the test drives it.</summary>
    private static IMediaFolderReader ReaderWithNoMusicFolder()
    {
        var real = new MediaFolderReader();
        var reader = Substitute.For<IMediaFolderReader>();
        reader.ExistsAsync(Arg.Any<string>()).Returns(call => real.ExistsAsync(call.Arg<string>()));
        reader.ExistsAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)).Returns(false);
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(call => real.ReadAsync(call.ArgAt<string>(0), call.ArgAt<IReadOnlySet<string>>(1)));
        reader.CountFilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => real.CountFilesAsync(call.ArgAt<IReadOnlyList<string>>(0), call.ArgAt<IReadOnlySet<string>>(1), call.ArgAt<CancellationToken>(2)));
        return reader;
    }
}
