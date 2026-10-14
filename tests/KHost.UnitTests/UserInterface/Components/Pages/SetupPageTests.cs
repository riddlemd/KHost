using Bunit;
using KHost.Abstractions.Services;
using KHost.UnitTests.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Components.Pages;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages;

public class SetupPageTests : BunitContext
{
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();

    public SetupPageTests()
    {
        this.AddKHostTestDefaults();

        Services.AddSingleton(_venuesService);
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_presets);
        Services.AddNewVenuesSection();
        _presets.ReadAll().Returns([]);

        _venuesService.HasAnyAsync().Returns(false);
        _appSettings.Current.Returns(new AppSettings());
    }

    [Fact]
    public void ANewMachine_StartsAtBackgrounds()
    {
        var cut = Render<SetupPage>();

        // Five steps: Backgrounds, Placeholder, Venue, FFmpeg, Media.
        Assert.Contains("Step 1 of 5", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".kh-wizard-backgrounds"));
    }

    /// <summary>Backgrounds and Placeholder come before Venue so the venue made next starts on both.</summary>
    [Fact]
    public async Task Backgrounds_ThenPlaceholder_ThenTheVenueStep()
    {
        var cut = Render<SetupPage>();

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());
        Assert.Contains("Step 2 of 5", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".kh-placeholder-image"));

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());
        Assert.Contains("Step 3 of 5", cut.Markup);
        Assert.NotEmpty(cut.FindAll("#venue-name"));
    }

    [Fact]
    public void WithAVenue_ResumesAtFFmpeg()
    {
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
