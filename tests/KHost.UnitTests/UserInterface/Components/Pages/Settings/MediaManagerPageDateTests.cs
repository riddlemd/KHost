using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

// Times are stored UTC and converted where they are shown. An evening import is the case that
// catches it: a karaoke host works evenings, so west of UTC the stored date is already tomorrow.
public class MediaManagerPageDateTests : BunitContext
{
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public MediaManagerPageDateTests()
    {
        _settings.Current.Returns(new AppSettings());
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(null));

        Services.AddSingleton(_media);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<IMessageBroker>(_broker);

        // AuthorizeView and the eager dialogs both need their services present or the render throws.
        Services.AddSingleton(Substitute.For<IMediaFileParsingService>());
        Services.AddSingleton(Substitute.For<IMediaImportService>());
        Services.AddSingleton(Substitute.For<IMediaPoolService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<KHost.Abstractions.Repositories.IMediaRepository>());
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Authorized against every policy rather than listing them: this test is about a date,
        // and the page's permissions would otherwise fail it for reasons it does not test.
        var auth = AddAuthorization();
        auth.SetAuthorized("tester");
        auth.SetPolicies("DeleteMedia", "EditMedia", "ManageMedia", "ImportMedia");
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

        // AuthorizeView needs the cascading state, not just the provider behind it.
        var cut = Render<CascadingAuthenticationState>(ps => ps
            .AddChildContent<MediaManagerPage>());
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
