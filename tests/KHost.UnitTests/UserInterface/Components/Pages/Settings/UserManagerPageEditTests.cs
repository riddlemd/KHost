using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UnitTests.DataAccess;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>Edits a singer from the Users page through the real dialog onto a real store, where a
/// save from a stale row used to delete the keys tying a guest to their singer.</summary>
public class UserManagerPageEditTests : BunitContext
{
    private readonly SqliteTestDatabase _database = new();
    private readonly UserGroupsRepository _groups;
    private readonly UsersService _users;
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly KHostUserGroup _hosts = new() { Name = "Hosts" };
    private readonly KHostUserGroup _regulars = new() { Name = "Regulars" };
    private readonly KHostUserGroup _staff = new() { Name = "Staff" };
    private readonly KHostUser _ada = new() { Name = "Ada" };

    private KHostUser? _opened;
    private Func<KHostUser?, Task>? _onSave;

    public UserManagerPageEditTests()
    {
        var broker = new MessageBroker(NullLogger<MessageBroker>.Instance);
        _groups = new UserGroupsRepository(_database, NullLogger<BaseRepository<KHostUserGroup>>.Instance);
        _users = new UsersService(
            NullLogger<UsersService>.Instance,
            new UsersRepository(_database, NullLogger<BaseRepository<KHostUser>>.Instance),
            _groups,
            broker);

        _dialogs.RequestEditAsync(Arg.Any<KHostUser?>(), Arg.Any<Func<KHostUser?, Task>>(), Arg.Any<Action?>(), Arg.Any<Action?>())
            .Returns(call =>
            {
                _opened = call.ArgAt<KHostUser?>(0);
                _onSave = call.ArgAt<Func<KHostUser?, Task>>(1);
                return Task.CompletedTask;
            });

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        var userGroups = Substitute.For<IUserGroupsService>();
        userGroups.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(_ => new PaginatedResult<KHostUserGroup> { Items = [_hosts, _regulars, _staff] });

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadRecentVenueVisitsBySingerAsync(Arg.Any<Guid>(), Arg.Any<int>()).Returns([]);
        performances.ReadBySingerIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(new PaginatedResult<Performance>());

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IUsersService>(_users);
        Services.AddSingleton(userGroups);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPasswordHasher>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(broker);

        var auth = AddAuthorization();
        auth.SetAuthorized("tester");
        auth.SetPolicies("EditUser", "DeleteUser");
    }

    [Fact]
    public async Task EditingASinger_KeepsTheKeysAGuestHasAndChangesTheGroupsTicked()
    {
        await _database.SeedAsync(_hosts, _regulars, _staff, _ada);
        await _groups.AddUserToGroupAsync(_ada.Id, _hosts.Id);
        await _users.AddForeignKeyAsync(_ada.Id, "Example", "account-1");

        var page = Render<CascadingAuthenticationState>(ps => ps.AddChildContent<UserManagerPage>());

        // Written behind the page's back, as another console's group editor would: the row on
        // screen no longer knows, so only a fresh read keeps it.
        await _groups.AddUserToGroupAsync(_ada.Id, _staff.Id);

        await OpenAdaAsync(page);

        // A guest picks a song while the profile is open.
        await _users.AddForeignKeyAsync(_ada.Id, "Example", "guest-7", isEphemeral: true);

        var dialog = Render<EditUserDialog>(ps => ps
            .Add(d => d.IsOpen, true)
            .Add(d => d.User, _opened)
            .Add(d => d.OnSave, EventCallback.Factory.Create<KHostUser>(this, u => _onSave!(u))));

        Tick(dialog, _hosts, false);
        Tick(dialog, _regulars, true);
        await dialog.Find(".kh-user-edit-dialog__save-btn").ClickAsync(new());

        var stored = (await _users.ReadAsync(_ada.Id))!;
        Assert.Equal(new[] { _regulars.Id, _staff.Id }.Order(), stored.Groups.Select(g => g.Id).Order());
        Assert.Equal(["account-1", "guest-7"], stored.ForeignKeys.Select(k => k.Key).Order().ToArray());
    }

    private async Task OpenAdaAsync(IRenderedComponent<CascadingAuthenticationState> page)
    {
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("tr"), row => row.TextContent.Contains("Ada")));
        var row = page.FindAll("tr").First(r => r.TextContent.Contains("Ada"));
        await row.QuerySelector(".kh-singer-manager__edit-btn")!.ClickAsync(new());
        Assert.NotNull(_opened);
    }

    private static void Tick(IRenderedComponent<EditUserDialog> dialog, KHostUserGroup group, bool on)
        => dialog.FindAll(".kh-user-edit-dialog__group")
            .First(label => label.TextContent.Contains(group.Name))
            .QuerySelector("input")!
            .Change(on);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _database.Dispose();
        base.Dispose(disposing);
    }
}
