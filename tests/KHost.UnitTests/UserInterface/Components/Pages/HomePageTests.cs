using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Pages;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages;

public class HomePageTests : BunitContext
{
    private const string NowPlayingSelector = ".kh-now-playing";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();

    public HomePageTests()
    {
        // The page and its panels reach for JS on first render; none of it matters here.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _queue.Users.Returns([]);
        _playback.State.Returns(PlaybackState.Stopped);
        _playback.Position.Returns(TimeSpan.Zero);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        var media = Substitute.For<IMediaService>();
        media.HasAnyAsync().Returns(true);

        // The queue panel labels rows by venue and reads the list on init. NSubstitute hands
        // back a task wrapping null without this, and the panel dereferences Items.
        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(pageSize: 0).ReturnsForAnyArgs(new PaginatedResult<Venue>());

        Services.AddSingleton(_queue);
        Services.AddSingleton(_playback);
        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(media);
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IAdService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        // Same trap: with a singer queued the panel counts that singer's performances, and a
        // substitute's null list reaches Count() rather than an empty one.
        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);
        Services.AddSingleton(performances);
        Services.AddSingleton(venues);
        Services.AddSingleton(Substitute.For<IPermissionService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
    }

    /// <summary>
    /// The card carries the break music band, and the moments a host reaches for break music are
    /// exactly the ones with nobody up: before the first singer, and between them. Rendering it
    /// only for a selected singer put the controls out of reach when they were most wanted.
    /// </summary>
    [Fact]
    public void Render_NoSingersInTheQueue_StillShowsNowPlaying()
    {
        var page = Render<HomePage>();

        Assert.NotNull(page.Find(NowPlayingSelector));
    }

    [Fact]
    public void Render_SingersQueuedButNoneSelected_StillShowsNowPlaying()
    {
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ada" }]);
        _queue.SelectedUser.Returns((KHostUser?)null);

        var page = Render<HomePage>();

        Assert.NotNull(page.Find(NowPlayingSelector));
    }
}
