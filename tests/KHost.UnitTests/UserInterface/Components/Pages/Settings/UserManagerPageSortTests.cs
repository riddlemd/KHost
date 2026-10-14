using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class UserManagerPageSortTests : BunitContext
{
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();

    public UserManagerPageSortTests()
    {
        this.AddKHostTestDefaults(users: _usersService);

        _usersService
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SortDescriptor?>())
            .Returns(new PaginatedResult<KHostUser> { Items = [new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }] });

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton(appSettings);
    }

    [Fact]
    public void SortColumnClicked_SearchesByThatColumn_AscendingFirst()
    {
        var cut = Render<UserManagerPage>();

        // The Name column is the only sortable header in UserManagerPage.razor.
        cut.Find("th.kh-table__col--sortable").Click();

        _usersService.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name" && !s.Descending));
    }

    [Fact]
    public void SortColumnClicked_Twice_ReversesTheDirection()
    {
        var cut = Render<UserManagerPage>();

        var header = cut.Find("th.kh-table__col--sortable");
        header.Click();
        header.Click();

        _usersService.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name" && s.Descending));
    }
}
