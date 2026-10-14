using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

// Times are stored UTC and converted where they are shown. An evening import is the case that
// catches it: a karaoke host works evenings, so west of UTC the stored date is already tomorrow.
public class MediaManagerPageDateTests : BunitContext
{
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();

    public MediaManagerPageDateTests()
    {
        this.AddKHostTestDefaults(media: _media);

        _settings.Current.Returns(new AppSettings());
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(null));

        Services.AddSingleton(_venues);
        Services.AddSingleton(_settings);

        // The eager dialogs need their services present or the render throws.
        Services.AddSingleton(Substitute.For<IMediaFileParsingService>());
        Services.AddSingleton(Substitute.For<IMediaImportService>());
        Services.AddSingleton(Substitute.For<IMediaPoolService>());
        Services.AddSingleton(Substitute.For<KHost.DataAccess.Repositories.IMediaRepository>());
    }

    [Fact]
    public void DateAdded_AUtcStampThatIsAlreadyTomorrow_ShowsTheLocalDate()
    {
        // Chosen so the UTC and local dates differ wherever this runs: just after UTC midnight is
        // the previous day west of it, and just before is the next day east; one always straddles.
        var justAfterUtcMidnight = new DateTime(2026, 8, 26, 0, 30, 0, DateTimeKind.Utc);
        var justBeforeUtcMidnight = new DateTime(2026, 8, 26, 23, 30, 0, DateTimeKind.Utc);

        var straddling = justAfterUtcMidnight.ToLocalTime().Date != justAfterUtcMidnight.Date
            ? justAfterUtcMidnight
            : justBeforeUtcMidnight;

        Arrange(straddling);

        var cut = Render<MediaManagerPage>();
        var shown = cut.FindAll("tbody tr td").Select(c => c.TextContent.Trim()).ToList();
        var local = straddling.ToLocalTime().ToString("yyyy-MM-dd");
        var utc = straddling.ToString("yyyy-MM-dd");

        Assert.Contains(local, shown);

        // Not skipped on a machine already at UTC: the row still carries the converted date,
        // though only a non-UTC machine can tell right from wrong, so this only reproduces away from UTC.
        if (local != utc)
            Assert.DoesNotContain(utc, shown);
    }

    private void Arrange(DateTime dateAddedUtc)
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
            DateAdded = dateAddedUtc,
        };

        _media.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SortDescriptor?>(), Arg.Any<MediaSearchOptions>())
            .Returns(new PaginatedResult<Media> { Items = [media], TotalCount = 1, PageNumber = 1, PageSize = 25 });
    }
}
