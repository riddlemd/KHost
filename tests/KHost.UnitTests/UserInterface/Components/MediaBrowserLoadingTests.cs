using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
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

/// <summary>A folder on a network share can take seconds to list; the browser says it is reading
/// rather than freezing, and counts each subfolder only once the list is up.</summary>
public class MediaBrowserLoadingTests : BunitContext
{
    private const string LoadingSelector = ".kh-media-importer__loading";

    // Never created: the folder reader is the only thing that looks at it.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "khost-browser-" + Guid.NewGuid().ToString("N"));
    private readonly string _otherFolder = Path.Combine(Path.GetTempPath(), "khost-browser-" + Guid.NewGuid().ToString("N"));

    private readonly IMediaFolderReader _reader = Substitute.For<IMediaFolderReader>();
    private readonly IMediaImportService _importService = Substitute.For<IMediaImportService>();
    private readonly IMediaRepository _mediaRepository = Substitute.For<IMediaRepository>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public MediaBrowserLoadingTests()
    {
        // No default music folder, so the browser opens on the drive list and waits to be pointed somewhere.
        _reader.ExistsAsync(Arg.Any<string>()).Returns(false);
        _reader.ExistsAsync(_folder).Returns(true);
        _reader.ExistsAsync(_otherFolder).Returns(true);
        _reader.CountFilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        _importService.SupportedExtensions.Returns(new List<string> { ".mp3" });
        _importService.TypeOverrides.Returns(new Dictionary<string, MediaType>());
        _mediaRepository.GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>()).Returns(new HashSet<string>());

        Services.AddSingleton(_reader);
        Services.AddSingleton(_importService);
        Services.AddSingleton(_mediaRepository);
        Services.AddSingleton(Substitute.For<IMediaFileParsingService>());
        Services.AddSingleton<IMessageBroker>(_broker);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task AFolderStillBeingRead_ShowsTheLoadingState_ThenItsRows()
    {
        var listing = new TaskCompletionSource<MediaFolderListing>();
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>()).Returns(listing.Task);
        var cut = Render<MediaBrowser>();

        GoTo(cut, _folder);

        cut.WaitForElement(LoadingSelector);
        Assert.DoesNotContain("song.mp3", cut.Markup);

        await cut.InvokeAsync(() => listing.SetResult(Listing(folders: [], files: [Item(_folder, "song.mp3")])));

        cut.WaitForAssertion(() => Assert.Contains("song.mp3", cut.Find("tbody").TextContent));
        Assert.Empty(cut.FindAll(LoadingSelector));
    }

    [Fact]
    public async Task SubfolderCounts_ArriveAfterTheRowsAreShown()
    {
        var sub = Path.Combine(_folder, "Duets");
        var counts = new TaskCompletionSource<IReadOnlyDictionary<string, int>>();
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>())
            .Returns(Listing(folders: [Item(_folder, "Duets"), Item(_folder, "Empty")], files: []));
        _reader.CountFilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(counts.Task);
        var cut = Render<MediaBrowser>();

        GoTo(cut, _folder);

        cut.WaitForAssertion(() => Assert.Contains("Duets", cut.Find("tbody").TextContent));
        Assert.Empty(cut.FindAll(".kh-media-importer__folder-count"));

        await cut.InvokeAsync(() => counts.SetResult(new Dictionary<string, int> { [sub] = 12, [Path.Combine(_folder, "Empty")] = 0 }));

        // A folder with nothing to import carries no count at all.
        cut.WaitForAssertion(() => Assert.Contains("12", cut.Find(".kh-media-importer__folder-count").TextContent));
        Assert.Single(cut.FindAll(".kh-media-importer__folder-count"));
    }

    [Fact]
    public async Task AListingForAFolderAlreadyLeft_IsDropped()
    {
        var slow = new TaskCompletionSource<MediaFolderListing>();
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>()).Returns(slow.Task);
        _reader.ReadAsync(_otherFolder, Arg.Any<IReadOnlySet<string>>())
            .Returns(Listing(folders: [], files: [Item(_otherFolder, "kept.mp3")]));
        var cut = Render<MediaBrowser>();

        GoTo(cut, _folder);
        cut.WaitForElement(LoadingSelector);
        GoTo(cut, _otherFolder, editing: true);
        cut.WaitForAssertion(() => Assert.Contains("kept.mp3", cut.Find("tbody").TextContent));

        await cut.InvokeAsync(() => slow.SetResult(Listing(folders: [], files: [Item(_folder, "dropped.mp3")])));

        Assert.Contains("kept.mp3", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("dropped.mp3", cut.Markup);
    }

    [Fact]
    public async Task APathTypedBeforeTheDefaultFolderAnswers_IsWhereTheBrowserStays()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var musicExists = new TaskCompletionSource<bool>();
        _reader.ExistsAsync(music).Returns(musicExists.Task);
        _reader.ReadAsync(music, Arg.Any<IReadOnlySet<string>>()).Returns(Listing(folders: [], files: [Item(music, "default.mp3")]));
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>()).Returns(Listing(folders: [], files: [Item(_folder, "typed.mp3")]));
        var cut = Render<MediaBrowser>();

        GoTo(cut, _folder);
        cut.WaitForAssertion(() => Assert.Contains("typed.mp3", cut.Find("tbody").TextContent));
        await cut.InvokeAsync(() => musicExists.SetResult(true));

        Assert.Contains("typed.mp3", cut.Find("tbody").TextContent);
        await _reader.DidNotReceive().ReadAsync(music, Arg.Any<IReadOnlySet<string>>());
    }

    /// <summary>The re-read after an import replaces the rows, so it says so like any other read.</summary>
    [Fact]
    public async Task AnImportFinishing_ShowsTheLoadingStateWhileTheFolderIsReread()
    {
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>()).Returns(Listing(folders: [], files: [Item(_folder, "song.mp3")]));
        var cut = Render<MediaBrowser>();
        GoTo(cut, _folder);
        cut.WaitForAssertion(() => Assert.Contains("song.mp3", cut.Find("tbody").TextContent));

        var reread = new TaskCompletionSource<MediaFolderListing>();
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>()).Returns(reread.Task);
        _importService.State.Returns(ImportState.Running);
        await _broker.PublishAsync(new MediaImportChanged());
        _importService.State.Returns(ImportState.Idle);
        await _broker.PublishAsync(new MediaImportChanged());

        cut.WaitForElement(LoadingSelector);
        await cut.InvokeAsync(() => reread.SetResult(Listing(folders: [], files: [Item(_folder, "song.mp3")])));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(LoadingSelector)));
    }

    /// <summary>Only the rows in view are drawn, behind a spacer row, so the stripe follows the
    /// row's place in the list rather than its place in the DOM.</summary>
    [Fact]
    public void Rows_AreStripedByTheirPlaceInTheList()
    {
        _reader.ReadAsync(_folder, Arg.Any<IReadOnlySet<string>>())
            .Returns(Listing(folders: [], files: [Item(_folder, "a.mp3"), Item(_folder, "b.mp3"), Item(_folder, "c.mp3")]));
        var cut = Render<MediaBrowser>();

        GoTo(cut, _folder);
        cut.WaitForAssertion(() => Assert.Contains("c.mp3", cut.Find("tbody").TextContent));

        var striped = cut.FindAll(".kh-media-importer__row")
            .Select(row => row.ClassList.Contains("kh-media-importer__row--striped"));
        Assert.Equal([false, true, false, true], striped);
    }

    private static void GoTo(IRenderedComponent<MediaBrowser> cut, string path, bool editing = false)
    {
        if (!editing)
            cut.Find("button[title='Edit path']").Click();
        cut.Find(".kh-media-importer__path-input").Input(path);
        cut.Find(".kh-media-importer__path-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static MediaFolderListing Listing(IReadOnlyList<MediaFolderItem> folders, IReadOnlyList<MediaFolderItem> files)
        => new(folders, files);

    private static MediaFolderItem Item(string folder, string name)
        => new(Path.Combine(folder, name), 1024, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
