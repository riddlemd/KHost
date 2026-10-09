using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Setup;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Setup;

public class WizardStepBackgroundsTests : BunitContext
{
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();

    public WizardStepBackgroundsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _appSettings.Current.Returns(new AppSettings());
        _presets.ReadAll().Returns(
        [
            new VisualiserPreset { Name = "spectrum-bars", Title = "Spectrum bars", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-bokeh", Title = "Floating lights", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-embers", Title = "Rising embers", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-nebula", Title = "Emission nebula", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-storm", Title = "Thunderhead", Source = VisualiserPresetSource.BuiltIn },
        ]);

        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_presets);
    }

    [Theory]
    [InlineData(VenueBackgrounds.Basic, "wizard-backgrounds-Basic")]
    [InlineData(VenueBackgrounds.Advanced, "wizard-backgrounds-Advanced")]
    public void TheStoredChoice_IsTheOneChecked(VenueBackgrounds stored, string checkedId)
    {
        _appSettings.Current.Returns(new AppSettings { NewVenueBackgrounds = stored });

        var cut = Render<WizardStepBackgrounds>();

        Assert.Equal([checkedId], cut.FindAll("input[type=radio]").Where(r => r.HasAttribute("checked")).Select(r => r.Id));
    }

    /// <summary>Each option names its own scenes, so a host sees what they are choosing between.</summary>
    [Fact]
    public void EachOption_ListsOnlyItsOwnAmbientScenes()
    {
        var cut = Render<WizardStepBackgrounds>();

        Assert.Equal(
            ["Floating lights · Rising embers", "Emission nebula · Thunderhead"],
            cut.FindAll(".kh-wizard-backgrounds__scenes").Select(s => s.TextContent));
    }

    [Fact]
    public void EachPreview_PlaysASceneFromItsOwnPlaylist()
    {
        Render<WizardStepBackgrounds>();

        var shown = JSInterop.Invocations["khVisualiserPreview.show"]
            .Select(call => call.Arguments[1]!.GetType().GetProperty("builtIn")!.GetValue(call.Arguments[1]))
            .ToList();
        Assert.Equal(["ambient-bokeh", "ambient-nebula"], shown);
    }

    [Fact]
    public async Task Next_SavesTheChosenPlaylist_ThenMovesOn()
    {
        var completed = false;
        var cut = Render<WizardStepBackgrounds>(ps => ps.Add(p => p.OnComplete, () => completed = true));

        cut.Find("#wizard-backgrounds-Advanced").Change(true);
        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        await _appSettings.Received(1).SaveNewVenueBackgroundsAsync(VenueBackgrounds.Advanced);
        Assert.True(completed);
    }

    [Fact]
    public async Task Next_ASaveThatFails_SaysSoAndStays()
    {
        _appSettings.SaveNewVenueBackgroundsAsync(Arg.Any<VenueBackgrounds>()).Returns(Task.FromException(new IOException("disk full")));
        var completed = false;
        var cut = Render<WizardStepBackgrounds>(ps => ps.Add(p => p.OnComplete, () => completed = true));

        await cut.Find(".kh-setup-wizard__actions button").ClickAsync(new());

        Assert.Contains("disk full", cut.Find(".kh-alert--danger").TextContent);
        Assert.False(completed);
    }
}
