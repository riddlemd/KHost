using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// Rendered rather than asserted on the resolver alone: the point of the feature is what a host
/// sees in the table, and a column set nothing draws is a column set that does not exist.
/// </summary>
public class MediaSearchPanelColumnsTests : BunitContext
{
    private const string ThumbSelector = ".kh-media-search-panel__thumb";
    private const string BadgeSelector = ".kh-media-search-panel__queued-badge";

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private static readonly IReadOnlyList<MediaResultColumn> YouTubeShape =
    [
        new() { Key = "thumbnail", Header = "", Kind = MediaResultColumnKind.Thumbnail, Essential = false },
        new() { Key = MediaResultColumn.TitleKey, Header = "Title" },
        new() { Key = "publisher", Header = "Published by", Essential = false },
        new() { Key = MediaResultColumn.DurationKey, Header = "Duration" },
    ];

    public MediaSearchPanelColumnsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _queue.Users.Returns(_ => []);
        _performances.ReadQueuedAsync().Returns([]);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        Services.AddSingleton(_search);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_queue);
        Services.AddSingleton(permissions);
        Services.AddSingleton(_performances);
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    private static MediaSearchEntity Result(Dictionary<string, string>? fields = null) => new()
    {
        Source = "YouTube",
        SourceDisplayName = "YouTube",
        ForeignKey = "CRrZlEF7-SU",
        Title = "Toto - Africa (Karaoke Version)",
        Artist = "Toto",
        Duration = TimeSpan.FromSeconds(310),
        Fields = fields ?? new Dictionary<string, string>
        {
            ["thumbnail"] = "https://i.ytimg.com/vi/CRrZlEF7-SU/hq720.jpg",
            ["publisher"] = "Sing King ✓",
        },
    };

    private IRenderedComponent<MediaSearchPanel> RenderWith(
        IReadOnlyList<MediaResultColumn> columns, params MediaSearchEntity[] results)
    {
        var provider = Substitute.For<IMediaProvider>();
        provider.SourceName.Returns("YouTube");
        provider.DisplayName.Returns("YouTube");
        provider.Columns.Returns(columns);

        _search.Providers.Returns([provider]);
        _search.SearchAsync(Arg.Any<string>()).Returns([.. results]);

        var panel = Render<MediaSearchPanel>();

        panel.Find(".kh-split-button, button").Click();

        return panel;
    }

    [Fact]
    public void AProvidersColumns_BecomeTheTablesHeadings()
    {
        var panel = RenderWith(YouTubeShape, Result());

        var headers = panel.FindAll("thead th").Select(th => th.TextContent.Trim()).ToArray();

        // Actions is the console's, appended after whatever the provider asked for.
        Assert.Equal(["", "Title", "Published by", "Duration", "Actions"], headers);
    }

    /// <summary>The complaint this feature exists for: the channel was computed and then dropped.</summary>
    [Fact]
    public void AProvidersOwnField_ReachesTheRow()
    {
        var panel = RenderWith(YouTubeShape, Result());

        Assert.Contains("Sing King", panel.Find("tbody tr").TextContent);
    }

    [Fact]
    public void AThumbnailColumn_RendersThePicture()
    {
        var panel = RenderWith(YouTubeShape, Result());

        Assert.Equal("https://i.ytimg.com/vi/CRrZlEF7-SU/hq720.jpg", panel.Find(ThumbSelector).GetAttribute("src"));
    }

    [Fact]
    public void AThumbnailColumn_WithNoPicture_RendersNoBrokenImage()
    {
        var panel = RenderWith(YouTubeShape, Result(fields: new() { ["publisher"] = "Sing King" }));

        Assert.Empty(panel.FindAll(ThumbSelector));
    }

    [Fact]
    public void AProviderThatDeclaresNoColumns_KeepsTheDefaultShape()
    {
        var panel = RenderWith([], Result());

        var headers = panel.FindAll("thead th").Select(th => th.TextContent.Trim()).ToArray();

        Assert.Equal(["Title", "Artist", "Duration", "Actions"], headers);
    }

    /// <summary>
    /// The badge names who already has the song queued, so it has to sit on the cell that names
    /// the song — never on the picture, which says nothing on its own. Driven through a local
    /// result because the badge is local-only: a remote result's key is the provider's, not a
    /// library id.
    /// </summary>
    [Fact]
    public void TheQueuedBadge_RidesTheColumnThatNamesTheRow_NotThePicture()
    {
        var singer = Guid.NewGuid();
        var media = Guid.NewGuid();

        _queue.Users.Returns(_ => [new KHostUser { Id = singer, Name = "Mike" }]);
        _performances.ReadQueuedAsync().Returns([
            new Performance { Id = Guid.NewGuid(), SingerId = singer, MediaId = media, QueuePosition = 1 }
        ]);

        var local = Substitute.For<IMediaProvider>();
        local.SourceName.Returns(nameof(LocalMediaProvider));
        local.DisplayName.Returns("Library");
        local.Columns.Returns(YouTubeShape);

        _search.Providers.Returns([local]);
        _search.SearchAsync(Arg.Any<string>()).Returns([
            new MediaSearchEntity
            {
                Source = nameof(LocalMediaProvider),
                SourceDisplayName = "Library",
                ForeignKey = media.ToString(),
                Title = "Africa",
                Artist = "Toto",
                Duration = TimeSpan.FromSeconds(310),
                Fields = new Dictionary<string, string> { ["thumbnail"] = "https://example.test/a.jpg" },
            }
        ]);

        var panel = Render<MediaSearchPanel>();
        panel.Find(".kh-split-button, button").Click();

        var cells = panel.FindAll("tbody tr td").ToList();
        var badgeCell = cells.FindIndex(cell => cell.QuerySelector(BadgeSelector) is not null);

        // -1 would mean the badge vanished entirely; 0 would mean it landed on the thumbnail.
        Assert.Equal(1, badgeCell);
    }
}
