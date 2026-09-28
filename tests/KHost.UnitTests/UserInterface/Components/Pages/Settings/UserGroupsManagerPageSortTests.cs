using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class UserGroupsManagerPageSortTests : BunitContext
{
    private readonly IUserGroupsService _userGroupsService = Substitute.For<IUserGroupsService>();

    public UserGroupsManagerPageSortTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _userGroupsService
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SortDescriptor?>())
            .Returns(new PaginatedResult<KHostUserGroup> { Items = [new KHostUserGroup { Id = Guid.NewGuid(), Name = "Regulars" }] });

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_userGroupsService);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));

        var auth = AddAuthorization();
        auth.SetAuthorized("tester");
        auth.SetPolicies("EditGroup", "DeleteGroup");
    }

    [Fact]
    public void SortColumnClicked_SearchesByThatColumn_AscendingFirst()
    {
        var cut = Render<CascadingAuthenticationState>(ps => ps.AddChildContent<UserGroupsManagerPage>());

        // The Name column is the only sortable header in UserGroupsManagerPage.razor.
        cut.Find("th.kh-table__col--sortable").Click();

        _userGroupsService.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name" && !s.Descending));
    }

    [Fact]
    public void SortColumnClicked_Twice_ReversesTheDirection()
    {
        var cut = Render<CascadingAuthenticationState>(ps => ps.AddChildContent<UserGroupsManagerPage>());

        var header = cut.Find("th.kh-table__col--sortable");
        header.Click();
        header.Click();

        _userGroupsService.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name" && s.Descending));
    }
}
