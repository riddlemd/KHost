using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>A password nobody can sign in with is nothing to collect, so the field follows
/// Auth:RequireLogin rather than always showing.</summary>
public class EditUserDialogPasswordVisibilityTests : BunitContext
{
    private readonly IUserGroupsService _userGroupsService = Substitute.For<IUserGroupsService>();
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly ITipsService _tipsService = Substitute.For<ITipsService>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();

    public EditUserDialogPasswordVisibilityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_userGroupsService);
        Services.AddSingleton(_usersService);
        Services.AddSingleton(_performanceService);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(_venuesService);
        Services.AddSingleton(_tipsService);
        Services.AddSingleton(_passwordHasher);
        Services.AddSingleton(_appSettings);

        _userGroupsService.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<KHostUserGroup>());
        // Null: the add flow's unsaved user, and a dialog with no stats to aggregate either way.
        _usersService.ReadAsync(Arg.Any<Guid>()).Returns((KHostUser?)null);
    }

    [Fact]
    public void RequireLoginIsOn_ShowsThePasswordField()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });

        var cut = Render<EditUserDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.User, new KHostUser { Name = "Singer" }));

        Assert.Contains(cut.FindAll("label"), l => l.TextContent.Contains("Password"));
    }

    [Fact]
    public void RequireLoginIsOff_HidesThePasswordField()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = false });

        var cut = Render<EditUserDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.User, new KHostUser { Name = "Singer" }));

        Assert.DoesNotContain(cut.FindAll("label"), l => l.TextContent.Contains("Password"));
    }

    /// <summary>The field being gone means nothing ever types into it, so a save with sign-in off
    /// must not clear a password an earlier, sign-in-on save already set.</summary>
    [Fact]
    public async Task RequireLoginIsOff_SavingAUser_LeavesTheExistingPasswordHashUntouched()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = false });
        var user = new KHostUser { Name = "Singer", PasswordHash = "existing-hash" };

        KHostUser? saved = null;
        var cut = Render<EditUserDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.User, user)
            .Add(p => p.OnSave, (KHostUser u) => saved = u));

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        Assert.NotNull(saved);
        Assert.Equal("existing-hash", saved!.PasswordHash);
        await _passwordHasher.DidNotReceive().HashAsync(Arg.Any<string>());
    }
}
