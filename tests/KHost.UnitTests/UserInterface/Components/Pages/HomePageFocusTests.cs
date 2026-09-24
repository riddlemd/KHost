using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Pages;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages;

/// <summary>Only a real page and form submission catch a handler that never reaches the field.</summary>
public class HomePageFocusTests : BunitContext
{
    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";
    private const string SongSearchInputSelector = "[data-kh-shortcut='media-search']";
    private const string AddSingerFormSelector = "form[name='add-singer-form']";
    private const string AddSingerInputSelector = $"{AddSingerFormSelector} input";
    private const string SingerRowSelector = ".kh-singer-queue-panel__singer-queue__singer";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IUsersService _users = Substitute.For<IUsersService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private readonly List<KHostUser> _queuedUsers = [];
    private KHostUser? _selectedUser;

    public HomePageFocusTests()
    {
        // The page, the queue panel's combo box, and the search panel all reach for JS on render;
        // none of that matters to whether the right element was asked to focus.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _queue.Users.Returns(_ => _queuedUsers.AsReadOnly());
        _queue.SelectedUser.Returns(_ => _selectedUser);
        _queue.SelectedUserId.Returns(_ => _selectedUser?.Id);

        _queue.SelectUserAsync(Arg.Any<Guid?>()).Returns(callInfo =>
        {
            var id = callInfo.ArgAt<Guid?>(0);
            _selectedUser = _queuedUsers.FirstOrDefault(u => u.Id == id);
            return Task.CompletedTask;
        });

        // The add form's one call now adds and selects together; a bare AddUserAsync (a plugin's
        // remote sign-up) must not move this fixture's selection.
        _queue.AddAndSelectUserAsync(Arg.Any<Guid>()).Returns(callInfo =>
        {
            var id = callInfo.ArgAt<Guid>(0);
            _selectedUser = _queuedUsers.FirstOrDefault(u => u.Id == id);
            return Task.CompletedTask;
        });

        // The combo box's own debounced search overload also needs a non-null result, unread here.
        _users.SearchAsync(Arg.Any<string>()).Returns(new PaginatedResult<KHostUser>());
        _users.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<UserSearchOptions>())
            .Returns(new PaginatedResult<KHostUser>());
        _users.CreateAsync(Arg.Any<KHostUser>()).Returns(callInfo =>
        {
            var created = callInfo.ArgAt<KHostUser>(0);
            created.Id = Guid.NewGuid();
            _queuedUsers.Add(created);
            return Task.FromResult(created);
        });

        _performances.ReadQueuedAsync().Returns([]);
        _performances.ReadLastVenueBySingersAsync(Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, RecentVenueVisit>());

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(pageSize: 0).ReturnsForAnyArgs(new PaginatedResult<Venue>());
        venues.ReadSelectedVenueAsync().Returns(new Venue { Id = Guid.NewGuid(), Name = "Bar" });

        var media = Substitute.For<IMediaService>();
        media.HasAnyAsync().Returns(true);

        var search = Substitute.For<IMediaSearchService>();
        search.Providers.Returns([]);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_users);
        Services.AddSingleton(_performances);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(media);
        Services.AddSingleton(search);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<INextSingerCardService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IAdService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
        Services.AddSingleton(Substitute.For<IAppSettingsService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IControlState>(new ControlState());
    }

    [Fact]
    public void AddingASinger_MovesFocusToTheSongSearchInput()
    {
        var page = Render<HomePage>();

        page.Find(AddSingerInputSelector).Input("Debbie");
        page.Find(AddSingerFormSelector).Submit();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SongSearchInputSelector)));

        JSInterop.VerifyInvoke(FocusIdentifier);
    }

    /// <summary>Viewing an existing singer isn't asking to type a song, so it must not steal focus.</summary>
    [Fact]
    public async Task SelectingAnExistingSinger_DoesNotStealFocus()
    {
        _queuedUsers.Add(new KHostUser { Id = Guid.NewGuid(), Name = "Ada" });

        var page = Render<HomePage>();

        page.Find(SingerRowSelector).Click();

        // The domain service would announce this itself; the substitute here stands in for it.
        await _broker.PublishAsync(new SingerQueueChanged());

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SongSearchInputSelector)));

        JSInterop.VerifyNotInvoke(FocusIdentifier);
    }
}
