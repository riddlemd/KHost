using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages;

/// <summary>Sign-in is a config flag now, not a wizard step: the wizard's step list follows
/// Auth:RequireLogin, and there is no Security step to run through either way.</summary>
public class SetupPageTests : BunitContext
{
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();
    private readonly IUserGroupsService _userGroupsService = Substitute.For<IUserGroupsService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();

    public SetupPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_usersService);
        Services.AddSingleton(_userGroupsService);
        Services.AddSingleton(_venuesService);
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_passwordHasher);

        _venuesService.HasAnyAsync().Returns(false);
        _usersService.HasAdminWithPasswordAsync().Returns(false);
    }

    [Fact]
    public void RequireLoginIsOn_StartsAtTheAdminStep_WithNoSecurityStepEver()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });

        var cut = Render<SetupPage>();

        // Four steps (Admin, Venue, FFmpeg, Media): no Security step ever appears in the count.
        Assert.Contains("Step 1 of 4", cut.Markup);
        Assert.NotEmpty(cut.FindAll("#admin-username"));
    }

    [Fact]
    public void RequireLoginIsOff_SkipsTheAdminStep_AndStartsAtVenue()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = false });

        var cut = Render<SetupPage>();

        // Three steps (Venue, FFmpeg, Media): no Admin step either, since no sign-in needs one.
        Assert.Contains("Step 1 of 3", cut.Markup);
        Assert.NotEmpty(cut.FindAll("#venue-name"));
    }

    /// <summary>The lock-out guard: an admin row with no password has not cleared this step,
    /// so a host who flips the flag on after setup lands back on Admin, not past it. An admin row
    /// existing is not enough on its own — HasAdminUserAsync would say yes here, which is exactly
    /// the question this page must not ask.</summary>
    [Fact]
    public void RequireLoginIsOn_WithAnAdminButNoPassword_StaysOnTheAdminStep()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });
        _usersService.HasAdminUserAsync().Returns(true);
        _usersService.HasAdminWithPasswordAsync().Returns(false);
        _venuesService.HasAnyAsync().Returns(true);

        var cut = Render<SetupPage>();

        Assert.NotEmpty(cut.FindAll("#admin-username"));
    }

    [Fact]
    public void RequireLoginIsOn_WithAnAdminPasswordAlreadySet_ResumesAtVenue()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });
        _usersService.HasAdminWithPasswordAsync().Returns(true);
        _venuesService.HasAnyAsync().Returns(false);

        var cut = Render<SetupPage>();

        Assert.NotEmpty(cut.FindAll("#venue-name"));
    }
}
