using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Re-queueing brings a turn's own background back only at the venue it was sung at;
/// anywhere else the new turn takes that venue's playlist, while its key still comes back.</summary>
public class SingerPerformanceHistoryDialogBackgroundTests : BunitContext
{
    private const string EnqueueSelector = ".kh-split-btn__primary";

    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly Guid _singerId = Guid.NewGuid();
    private readonly Guid _here = Guid.NewGuid();
    private readonly Media _media = new() { Id = Guid.NewGuid(), FilePath = "/music/song.mp4", Title = "Africa", Status = MediaStatus.Ready };

    private readonly PerformanceBackground _look = new()
    {
        Type = PerformanceBackgroundType.Look,
        PresetSource = VisualiserPresetSource.BuiltIn,
        PresetName = "spectrum-bars",
        Brightness = 140,
    };

    public SingerPerformanceHistoryDialogBackgroundTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaService.ReadAsync(_media.Id).Returns(_media);
        _venues.SelectedVenueId.Returns(_here);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(_venues);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void Enqueue_SungAtThisVenue_CarriesItsBackgroundAsACopy()
    {
        EnqueueSungAt(_here);

        _performances.Received(1).CreateAndEnqueueAsync(Arg.Is<Performance>(p =>
            p.Background != null && !ReferenceEquals(p.Background, _look)
            && p.Background.Type == PerformanceBackgroundType.Look
            && p.Background.PresetName == "spectrum-bars" && p.Background.Brightness == 140));
    }

    [Fact]
    public void Enqueue_SungAtAnotherVenue_LeavesItToThisVenuesPlaylist_ButKeepsTheKey()
    {
        EnqueueSungAt(Guid.NewGuid());

        _performances.Received(1).CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.Background == null && p.Pitch == -2));
    }

    [Fact]
    public void Enqueue_SungWithNoVenueRecorded_LeavesItToThisVenuesPlaylist()
    {
        EnqueueSungAt(null);

        _performances.Received(1).CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.Background == null && p.Pitch == -2));
    }

    [Fact]
    public void Enqueue_SungAtAVenueWhileNoneIsSelected_LeavesItToThePlaylist()
    {
        _venues.SelectedVenueId.Returns((Guid?)null);

        EnqueueSungAt(_here);

        _performances.Received(1).CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.Background == null));
    }

    [Fact]
    public void Enqueue_SungWithNoVenueWhileNoneIsSelected_LeavesItToThePlaylist()
    {
        _venues.SelectedVenueId.Returns((Guid?)null);

        EnqueueSungAt(null);

        _performances.Received(1).CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.Background == null));
    }

    private void EnqueueSungAt(Guid? venueId)
    {
        var sung = new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, VenueId = venueId, Pitch = -2, Background = _look };
        _performances
            .ReadBySingerIdAsync(_singerId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(new PaginatedResult<Performance> { Items = [sung], TotalCount = 1, PageNumber = 1, PageSize = 25 });

        var dialog = Render<SingerPerformanceHistoryDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.UserId, _singerId));

        dialog.Find(EnqueueSelector).Click();
    }
}
