using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>
/// Every cell in a kh-table is white-space: nowrap, so a column that cannot ellipsise sets a floor
/// under the whole table. Title and Artist are the library's only free-text columns and the only
/// ones without a fixed width, which makes them the two that have to give when the page is narrow.
/// Artist was missing the class, and the table could not shrink below the longest artist name —
/// on a half-width window .kh-card clipped the last columns off with nothing to scroll.
/// </summary>
public class MediaManagerPageColumnTests : BunitContext
{
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public MediaManagerPageColumnTests()
    {
        _settings.Current.Returns(new AppSettings());
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(null));

        Services.AddSingleton(_media);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<IMessageBroker>(_broker);

        // The page renders its dialogs eagerly, so their dependencies have to be present too.
        Services.AddSingleton(Substitute.For<IMediaFileParsingService>());
        Services.AddSingleton(Substitute.For<IMediaImportService>());
        Services.AddSingleton(Substitute.For<IMediaPoolService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<KHost.Abstractions.Repositories.IMediaRepository>());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var auth = AddAuthorization();
        auth.SetAuthorized("tester");
        auth.SetPolicies("DeleteMedia", "EditMedia", "ManageMedia", "ImportMedia");
    }

    [Fact]
    public void TitleAndArtist_AreBothFillCells_SoTheTableCanShrink()
    {
        Arrange();

        var cut = Render<CascadingAuthenticationState>(ps => ps.AddChildContent<MediaManagerPage>());
        var fill = cut.FindAll("tbody tr td.kh-table__cell--fill");

        // Two, which is also what _tables.scss keys its 50/50 split on:
        // tr:has(> .kh-table__cell--fill ~ .kh-table__cell--fill). One fill cell leaves that rule
        // dormant and the other column rigid.
        Assert.Equal(2, fill.Count);
        Assert.Equal(["Today", "The Smashing Pumpkins"], fill.Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public void Artist_CarriesItsFullValueAsATitle_SoAnEllipsisIsNotALoss()
    {
        Arrange();

        var cut = Render<CascadingAuthenticationState>(ps => ps.AddChildContent<MediaManagerPage>());
        var artist = cut.FindAll("tbody tr td.kh-table__cell--fill")[1];

        Assert.Equal("The Smashing Pumpkins", artist.GetAttribute("title"));
    }

    private void Arrange()
    {
        var media = new Media
        {
            Id = Guid.NewGuid(),
            Title = "Today",
            Artist = "The Smashing Pumpkins",
            Type = MediaType.Karaoke,
            Format = "MP4",
            Status = MediaStatus.Ready,
            FilePath = "/tmp/today.mp4",
            DateAdded = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc),
        };

        _media.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SortDescriptor?>(), Arg.Any<MediaSearchOptions>())
            .Returns(new PaginatedResult<Media> { Items = [media], TotalCount = 1, PageNumber = 1, PageSize = 25 });
    }
}
