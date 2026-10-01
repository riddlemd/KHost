using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>The profile opens on the stored singer, not the queue's cached copy: a save replaces
/// group memberships with whatever the dialog was handed.</summary>
public class SelectedSingerInfoPanelEditUserTests : BunitContext
{
    private const string EditProfileSelector = "button[title=\"Click to edit Singer's Profile\"]";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IUsersService _users = Substitute.For<IUsersService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly KHostUser _cached = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _stored;

    public SelectedSingerInfoPanelEditUserTests()
    {
        _stored = new KHostUser { Id = _cached.Id, Name = "Ann", Groups = [new KHostUserGroup { Name = "Staff" }] };

        _queue.SelectedUser.Returns(_cached);
        _queue.SelectedUserId.Returns(_cached.Id);
        _users.ReadAsync(_cached.Id).Returns(_stored);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_queue);
        Services.AddSingleton(performances);
        Services.AddSingleton(permissions);
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(_users);
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task EditProfile_OpensOnTheStoredSinger()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        await panel.Find(EditProfileSelector).ClickAsync(new());

        await _dialogs.Received(1).RequestEditAsync(
            Arg.Is<KHostUser?>(u => ReferenceEquals(u, _stored)),
            Arg.Any<Func<KHostUser?, Task>>(), Arg.Any<Action?>(), Arg.Any<Action?>());
    }
}
