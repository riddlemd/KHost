using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Messaging;
using KHost.Domain.Services.Messaging;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// A guest who scanned the code lands in the queue beside the singers the host typed in, and
/// nothing on the row said which was which.
/// </summary>
public class SingerQueuePanelRemoteSingerTests : BunitContext
{
    private const string RemoteMarkSelector = ".kh-singer-queue-panel__singer-queue__singer__remote";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    private readonly KHostUser _typedIn = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _fromAPhone = new() { Id = Guid.NewGuid(), Name = "Bob" };

    public SingerQueuePanelRemoteSingerTests()
    {
        _fromAPhone.ForeignKeys.Add(new KHostUserForeignKey
        {
            UserId = _fromAPhone.Id,
            Source = "Example",
            Key = "guest-7",
            IsEphemeral = true,
        });

        _queue.Users.Returns(_ => [_typedIn, _fromAPhone]);
        _queue.SelectedUserId.Returns(_ => _typedIn.Id);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    [Fact]
    public void ASingerWhoJoinedFromAPhone_IsMarked()
    {
        var mark = Assert.Single(Render<SingerQueuePanel>().FindAll(RemoteMarkSelector));

        // Exactly one of the two rows — the mark is worth nothing if it is on both.
        Assert.Equal("Joined from the room", mark.GetAttribute("aria-label"));
    }

    [Fact]
    public void ASingerTheHostTypedIn_IsNotMarked()
    {
        _queue.Users.Returns(_ => [_typedIn]);

        Assert.Empty(Render<SingerQueuePanel>().FindAll(RemoteMarkSelector));
    }

    [Fact]
    public void AKeyThatNamesAPersonRatherThanAConnection_IsNotAMark()
    {
        // A durable key is a provider saying who someone is, not that they are on a phone in the
        // room right now. Only the ephemeral kind means "arrived from the remote".
        _fromAPhone.ForeignKeys.Clear();
        _fromAPhone.ForeignKeys.Add(new KHostUserForeignKey
        {
            UserId = _fromAPhone.Id,
            Source = "SomeProvider",
            Key = "person-42",
            IsEphemeral = false,
        });

        Assert.Empty(Render<SingerQueuePanel>().FindAll(RemoteMarkSelector));
    }

    [Fact]
    public void TheMark_CarriesItsMeaningInWordsRatherThanOnlyInColour()
    {
        // It is a coloured dot: a host on a low-contrast theme, or one who cannot tell the hue
        // apart, has nothing else unless the row says what it means.
        var mark = Assert.Single(Render<SingerQueuePanel>().FindAll(RemoteMarkSelector));

        Assert.False(string.IsNullOrWhiteSpace(mark.GetAttribute("title")));
        Assert.Equal("img", mark.GetAttribute("role"));
    }
}
