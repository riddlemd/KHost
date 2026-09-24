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

/// <summary>ToggleSelection and its own deselect branch both walk PairedPaths; select-all's select
/// branch added only the row's own FullPath, leaving a CDG pair's .mp3 half out.</summary>
public class MediaBrowserSelectAllTests : BunitContext
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "khost-media-browser-tests-" + Guid.NewGuid());

    private readonly IMediaImportService _importService = Substitute.For<IMediaImportService>();
    private readonly IMediaRepository _mediaRepository = Substitute.For<IMediaRepository>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public MediaBrowserSelectAllTests()
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

        // The dialog and the split button's dropdown module both reach for JS on render.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SelectAll_SelectsBothHalvesOfACdgPair()
    {
        try
        {
            var cdgPath = Path.Combine(_tempDir, "song.cdg");
            var mp3Path = Path.Combine(_tempDir, "song.mp3");

            var cut = Render<MediaBrowser>();

            cut.Find("button[title='Edit path']").Click();
            cut.Find(".kh-media-importer__path-input").Input(_tempDir);
            cut.Find(".kh-media-importer__path-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

            cut.WaitForAssertion(() => Assert.Contains("CDG + MP3", cut.Markup));

            cut.Find("thead .kh-media-importer__check").Click();

            // Confirms the select-all committed before Import reads it, rather than assuming the
            // click's dispatch already settled synchronously.
            cut.WaitForAssertion(() => Assert.Contains("bi-check-square", cut.Find("thead .kh-media-importer__check").ClassName));

            cut.Find(".kh-split-btn__primary").Click();

            _importService.Received(1).StartAsync(Arg.Is<IEnumerable<string>>(
                paths => paths.Contains(cdgPath) && paths.Contains(mp3Path)));
        }
        finally
        {
            // BunitContext's own Dispose() is not virtual, so a base-class cleanup hook cannot
            // intercept it; the temp directory is cleaned up here instead.
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }
}
