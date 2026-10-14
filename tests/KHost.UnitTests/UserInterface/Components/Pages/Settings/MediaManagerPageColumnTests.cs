using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>Artist once missed the fill class, clipping the last columns with nothing to scroll.</summary>
public class MediaManagerPageColumnTests : BunitContext
{
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();

    public MediaManagerPageColumnTests()
    {
        this.AddKHostTestDefaults(media: _media);

        _settings.Current.Returns(new AppSettings());
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(null));

        Services.AddSingleton(_venues);
        Services.AddSingleton(_settings);

        // The page renders its dialogs eagerly, so their dependencies have to be present too.
        Services.AddSingleton(Substitute.For<IMediaFileParsingService>());
        Services.AddSingleton(Substitute.For<IMediaImportService>());
        Services.AddSingleton(Substitute.For<IMediaPoolService>());
        Services.AddSingleton(Substitute.For<KHost.DataAccess.Repositories.IMediaRepository>());
    }

    [Fact]
    public void TitleAndArtist_AreBothFillCells_SoTheTableCanShrink()
    {
        Arrange();

        var cut = Render<MediaManagerPage>();
        var fill = cut.FindAll("tbody tr td.kh-table__cell--fill");

        // Two, matching _tables.scss's 50/50 split; one fill cell leaves it dormant, the other rigid.
        Assert.Equal(2, fill.Count);
        Assert.Equal(["Today", "The Smashing Pumpkins"], fill.Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public void Artist_CarriesItsFullValueAsATitle_SoAnEllipsisIsNotALoss()
    {
        Arrange();

        var cut = Render<MediaManagerPage>();
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
