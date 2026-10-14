using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class UserGroupsManagerPageSortTests : BunitContext
{
    private readonly IUserGroupsService _userGroupsService = Substitute.For<IUserGroupsService>();

    public UserGroupsManagerPageSortTests()
    {
        this.AddKHostTestDefaults();

        _userGroupsService
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SortDescriptor?>())
            .Returns(new PaginatedResult<KHostUserGroup> { Items = [new KHostUserGroup { Id = Guid.NewGuid(), Name = "Regulars" }] });

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_userGroupsService);
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton(appSettings);
    }

    [Fact]
    public void SortColumnClicked_SearchesByThatColumn_AscendingFirst()
    {
        var cut = Render<UserGroupsManagerPage>();

        // The Name column is the only sortable header in UserGroupsManagerPage.razor.
        cut.Find("th.kh-table__col--sortable").Click();

        _userGroupsService.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name" && !s.Descending));
    }

    [Fact]
    public void SortColumnClicked_Twice_ReversesTheDirection()
    {
        var cut = Render<UserGroupsManagerPage>();

        var header = cut.Find("th.kh-table__col--sortable");
        header.Click();
        header.Click();

        _userGroupsService.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name" && s.Descending));
    }
}
