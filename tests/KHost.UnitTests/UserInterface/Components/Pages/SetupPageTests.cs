using Bunit;
using KHost.Abstractions.Services;
using KHost.UnitTests.UserInterface.Components.Pages.Settings;
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
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();

    public SetupPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_usersService);
        Services.AddSingleton(_userGroupsService);
        Services.AddSingleton(_venuesService);
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_passwordHasher);
        Services.AddSingleton(_presets);
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddNewVenuesSection();
        _presets.ReadAll().Returns([]);

        _venuesService.HasAnyAsync().Returns(false);
        _usersService.HasAdminWithPasswordAsync().Returns(false);
    }

    [Fact]
    public void RequireLoginIsOn_StartsAtTheAdminStep_WithNoSecurityStepEver()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });

        var cut = Render<SetupPage>();

        // Six steps (Admin, Backgrounds, Placeholder, Venue, FFmpeg, Media): no Security step ever appears in the count.
        Assert.Contains("Step 1 of 6", cut.Markup);
        Assert.NotEmpty(cut.FindAll("#admin-username"));
    }

    [Fact]
    public void RequireLoginIsOff_SkipsTheAdminStep_AndStartsAtBackgrounds()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = false });

        var cut = Render<SetupPage>();

        // Five steps (Backgrounds, Placeholder, Venue, FFmpeg, Media): no Admin step either, since no sign-in needs one.
        Assert.Contains("Step 1 of 5", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".kh-wizard-backgrounds"));
    }

    /// <summary>Backgrounds and Placeholder come before Venue so the venue made next starts on both.</summary>
    [Fact]
    public async Task Backgrounds_ThenPlaceholder_ThenTheVenueStep()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = false });
        var cut = Render<SetupPage>();

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());
        Assert.Contains("Step 2 of 5", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".kh-placeholder-image"));

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());
        Assert.Contains("Step 3 of 5", cut.Markup);
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
    public void RequireLoginIsOn_WithAnAdminPasswordAlreadySet_ResumesAtBackgrounds()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });
        _usersService.HasAdminWithPasswordAsync().Returns(true);
        _venuesService.HasAnyAsync().Returns(false);

        var cut = Render<SetupPage>();

        Assert.NotEmpty(cut.FindAll(".kh-wizard-backgrounds"));
    }

    [Fact]
    public void RequireLoginIsOn_WithAVenueButNoAdminPassword_LeavesOutTheVenueStep()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = true });
        _usersService.HasAdminWithPasswordAsync().Returns(false);
        _venuesService.HasAnyAsync().Returns(true);

        var cut = Render<SetupPage>();

        // Three steps (Admin, FFmpeg, Media): the venue already made is not offered again, nor
        // the Backgrounds and Placeholder choices that exist to start it.
        Assert.Contains("Step 1 of 3", cut.Markup);
        Assert.NotEmpty(cut.FindAll("#admin-username"));
    }

    [Fact]
    public void RequireLoginIsOff_WithAVenue_ResumesAtFFmpeg()
    {
        _appSettings.Current.Returns(new AppSettings { RequireLogin = false });
        _venuesService.HasAnyAsync().Returns(true);
        // Only this test lands on the FFmpeg step, the one that needs these.
        var ffmpeg = Services.AddFFmpegSection();
        ffmpeg.CheckAsync().Returns(AppSettingsPageServices.Found());

        var cut = Render<SetupPage>();

        // Two steps (FFmpeg, Media), starting on the first.
        Assert.Contains("Step 1 of 2", cut.Markup);
        Assert.Empty(cut.FindAll("#venue-name"));
    }
}
