using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Visualisations;
using KHost.UnitTests.DataAccess;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>Driven through the page's own controls, onto the real service and a real database, so
/// what a click saves is what is read back.</summary>
public class VisualisationsManagerPageTests : BunitContext
{
    private readonly SqliteTestDatabase _database = new();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly VisualisationPlaylistService _playlists;
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private Func<Task>? _confirm;

    public VisualisationsManagerPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _playlists = new VisualisationPlaylistService(NullLogger<VisualisationPlaylistService>.Instance,
            new VisualisationPlaylistRepository(_database, NullLogger<BaseRepository<VisualisationPlaylist>>.Instance), _broker);

        _presets.ReadAll().Returns(
        [
            new VisualiserPreset { Name = "Rovastar - Oozing Resistance", Source = VisualiserPresetSource.Bundled },
            new VisualiserPreset { Name = "_Mig_049", Source = VisualiserPresetSource.Bundled },
            new VisualiserPreset { Name = "My Swirl", Source = VisualiserPresetSource.Imported, ImportedUtc = DateTime.UtcNow },
            // Last here, so a select that lists them first does so by grouping, not by input order.
            new VisualiserPreset { Name = "spectrum-bars", Title = "Spectrum bars", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "retro-vhs", Title = "VHS tracking", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-embers", Title = "Rising embers", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "oscilloscope", Title = "Oscilloscope", Source = VisualiserPresetSource.BuiltIn },
        ]);
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Room" });
        _dialogs.ShowConfirmationAsync(Arg.Any<string>(), Arg.Do<Func<Task>>(confirm => _confirm = confirm), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        Services.AddSingleton<IVisualisationPlaylistService>(_playlists);
        Services.AddSingleton(_presets);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _database.Dispose();
        base.Dispose(disposing);
    }

    private async Task<VisualisationPlaylist> StoredAsync() => (await _playlists.ReadAllWithEntriesAsync()).Single();

    [Fact]
    public async Task AddPlaylist_SavesOneAndOpensIt()
    {
        var cut = Render<VisualisationsManagerPage>();

        cut.Find("#visualisation-add-playlist").Click();

        var stored = await StoredAsync();
        Assert.Equal("New playlist", stored.Name);
        cut.WaitForAssertion(() => Assert.Equal("New playlist", cut.Find("#visualisation-playlist-name").GetAttribute("value")));
    }

    /// <summary>The preset is chosen in the editor the new entry opens in, not beside the button.</summary>
    [Fact]
    public async Task AddEntry_OffersNoPickerOfItsOwn_AndStartsOnTheFirstPreset()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();

        Assert.Empty(cut.FindAll("#visualisation-add-preset"));
        cut.Find("#visualisation-add-entry").Click();

        var entry = Assert.Single((await StoredAsync()).Entries);
        var first = _presets.ReadAll()[0];
        Assert.Equal((first.Name, first.Source), (entry.PresetName, entry.PresetSource));
        Assert.Equal(VisualisationsManagerPage.PresetKey(first.Source, first.Name), cut.Find("#visualisation-preset").GetAttribute("value"));
    }

    [Fact]
    public async Task AddEntry_ThenTheEditor_SavesThePresetPicked()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();

        cut.Find("#visualisation-add-entry").Click();
        cut.Find("#visualisation-preset").Change("0:_Mig_049");

        var entry = Assert.Single((await StoredAsync()).Entries);
        Assert.Equal(("_Mig_049", VisualiserPresetSource.Bundled), (entry.PresetName, entry.PresetSource));
        cut.WaitForAssertion(() => Assert.Contains("_Mig_049", cut.Find(".kh-visualisations__entries").TextContent));
    }

    [Fact]
    public async Task AnImportedPreset_CanBeAdded()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();

        cut.Find("#visualisation-add-entry").Click();
        cut.Find("#visualisation-preset").Change("1:My Swirl");

        var entry = Assert.Single((await StoredAsync()).Entries);
        Assert.Equal(("My Swirl", VisualiserPresetSource.Imported), (entry.PresetName, entry.PresetSource));
    }

    /// <summary>One setting per test: any later save writes the whole list, which would carry a
    /// slider's value to the database even if letting go of it saved nothing.</summary>
    [Theory]
    [InlineData("#visualisation-brightness", "150")]
    [InlineData("#visualisation-saturation", "40")]
    [InlineData("#visualisation-sensitivity", "220")]
    public async Task LettingGoOfASlider_SavesItOnTheEntry(string slider, string value)
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();
        cut.Find("#visualisation-add-entry").Click();

        cut.Find(slider).Change(value);

        var entry = Assert.Single((await StoredAsync()).Entries);
        var saved = slider switch
        {
            "#visualisation-brightness" => entry.Brightness,
            "#visualisation-saturation" => entry.Saturation,
            _ => entry.Sensitivity,
        };
        Assert.Equal(int.Parse(value), saved);
    }

    [Fact]
    public void ThePresetSelect_ListsTheBuiltInsFirstByTitle()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();
        cut.Find("#visualisation-add-entry").Click();

        var groups = cut.FindAll("#visualisation-preset optgroup");

        Assert.Equal(["Built-in", "Ambient", "Retro", "MilkDrop presets", "Imported"], groups.Select(g => g.GetAttribute("label")));
        Assert.Equal(["Spectrum bars", "Oscilloscope"], groups[0].QuerySelectorAll("option").Select(o => o.TextContent));
        Assert.Equal(["2:spectrum-bars", "2:oscilloscope"], groups[0].QuerySelectorAll("option").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Rising embers"], groups[1].QuerySelectorAll("option").Select(o => o.TextContent));
        Assert.Equal(["2:ambient-embers"], groups[1].QuerySelectorAll("option").Select(o => o.GetAttribute("value")));
        Assert.Equal(["VHS tracking"], groups[2].QuerySelectorAll("option").Select(o => o.TextContent));
        Assert.Equal(["2:retro-vhs"], groups[2].QuerySelectorAll("option").Select(o => o.GetAttribute("value")));
    }

    /// <summary>A retro effect has no bars either, and its classic palette is its own look.</summary>
    [Fact]
    public async Task ARetroEffect_OffersAPaletteButNoBarCount()
    {
        var cut = await WithBuiltInAsync("retro-vhs");

        Assert.Empty(cut.FindAll("#visualisation-bars"));
        Assert.Contains("effect's own colours", cut.Find($"#visualisation-palette option[value={VisualiserColourScheme.Classic}]").TextContent);

        cut.Find("#visualisation-palette").Change("Single");
        Assert.Equal(VisualiserColourScheme.Single, Assert.Single((await StoredAsync()).Entries).ColourScheme);
    }

    [Fact]
    public async Task ThePreview_IsToldToDrawARetroEffect()
    {
        await WithBuiltInAsync("retro-vhs");

        Assert.Contains(JSInterop.Invocations, call =>
            call.Identifier == "khVisualiserPreview.show" && call.Arguments[1]!.ToString()!.Contains("builtIn = retro-vhs"));
    }

    /// <summary>A scene has no bars, and its classic palette is a mix of colours, not a meter's.</summary>
    [Fact]
    public async Task AnAmbientScene_OffersAPaletteButNoBarCount()
    {
        var cut = await WithBuiltInAsync("ambient-embers");

        Assert.Empty(cut.FindAll("#visualisation-bars"));
        var classic = cut.Find($"#visualisation-palette option[value={VisualiserColourScheme.Classic}]").TextContent;
        Assert.Contains("mix of colours", classic);

        cut.Find("#visualisation-palette").Change("Theme");
        Assert.Equal(VisualiserColourScheme.Theme, Assert.Single((await StoredAsync()).Entries).ColourScheme);
    }

    [Fact]
    public async Task AnAnalyser_KeepsItsGreenToRedWording()
    {
        var cut = await WithBuiltInAsync("spectrum-bars");

        Assert.Contains("green to red", cut.Find($"#visualisation-palette option[value={VisualiserColourScheme.Classic}]").TextContent);
    }

    [Fact]
    public async Task ThePreview_IsToldToDrawAnAmbientScene()
    {
        await WithBuiltInAsync("ambient-embers");

        Assert.Contains(JSInterop.Invocations, call =>
            call.Identifier == "khVisualiserPreview.show" && call.Arguments[1]!.ToString()!.Contains("builtIn = ambient-embers"));
    }

    private async Task<IRenderedComponent<VisualisationsManagerPage>> WithBuiltInAsync(string name)
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();
        cut.Find("#visualisation-add-entry").Click();
        cut.Find("#visualisation-preset").Change($"2:{name}");

        var entry = Assert.Single((await StoredAsync()).Entries);
        Assert.Equal((VisualiserPresetSource.BuiltIn, name), (entry.PresetSource, entry.PresetName));
        return cut;
    }

    [Fact]
    public async Task ABuiltIn_IsListedByItsTitle()
    {
        var cut = await WithBuiltInAsync("spectrum-bars");

        cut.WaitForAssertion(() => Assert.Contains("Spectrum bars", cut.Find(".kh-visualisations__entries").TextContent));
    }

    [Fact]
    public async Task ABarCount_IsSavedOnTheEntry()
    {
        var cut = await WithBuiltInAsync("spectrum-bars");

        cut.Find("#visualisation-bars").Change("64");

        Assert.Equal(64, Assert.Single((await StoredAsync()).Entries).BarCount);
    }

    [Fact]
    public async Task APalette_IsSavedOnTheEntry()
    {
        var cut = await WithBuiltInAsync("spectrum-bars");

        cut.Find("#visualisation-palette").Change("Theme");

        Assert.Equal(VisualiserColourScheme.Theme, Assert.Single((await StoredAsync()).Entries).ColourScheme);
    }

    [Fact]
    public async Task OneColour_OffersAPickerAndSavesWhatIsPicked()
    {
        var cut = await WithBuiltInAsync("spectrum-bars");
        Assert.Empty(cut.FindAll("#visualisation-colour"));

        cut.Find("#visualisation-palette").Change("Single");
        cut.Find("#visualisation-colour").Change("#ff0000");

        var entry = Assert.Single((await StoredAsync()).Entries);
        Assert.Equal((VisualiserColourScheme.Single, "#ff0000"), (entry.ColourScheme, entry.Colour));
    }

    /// <summary>A bar count means nothing to a line or to a preset, so neither offers one.</summary>
    [Fact]
    public async Task OnlyBarStylesOfferABarCount_AndOnlyBuiltInsAPalette()
    {
        var cut = await WithBuiltInAsync("oscilloscope");
        Assert.Empty(cut.FindAll("#visualisation-bars"));
        Assert.Single(cut.FindAll("#visualisation-palette"));

        cut.Find("#visualisation-preset").Change("0:_Mig_049");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#visualisation-palette")));
    }

    [Fact]
    public async Task ThePreview_IsToldToDrawTheBuiltInWithItsStyle()
    {
        var cut = await WithBuiltInAsync("spectrum-bars");

        cut.Find("#visualisation-bars").Change("16");

        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, call =>
            call.Identifier == "khVisualiserPreview.show"
            && call.Arguments[1]!.ToString()!.Contains("builtIn = spectrum-bars")
            && call.Arguments[1]!.ToString()!.Contains("barCount = 16")
            && call.Arguments[1]!.ToString()!.Contains("presetName = ,")));
    }

    /// <summary>A slider being dragged moves the preview, not the database.</summary>
    [Fact]
    public async Task DraggingASlider_SavesNothingUntilItIsLetGo()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();
        cut.Find("#visualisation-add-entry").Click();

        cut.Find("#visualisation-brightness").Input("60");

        Assert.Equal(100, Assert.Single((await StoredAsync()).Entries).Brightness);
        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, call =>
            call.Identifier == "khVisualiserPreview.show" && call.Arguments[1]!.ToString()!.Contains("brightness = 60")));
    }

    [Fact]
    public async Task Shuffle_IsSavedOnThePlaylist()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();

        cut.Find("#visualisation-shuffle").Change(true);

        Assert.True((await StoredAsync()).Shuffle);
    }

    [Fact]
    public async Task Renaming_IsSavedOnThePlaylist()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();

        cut.Find("#visualisation-playlist-name").Change("Late show");

        Assert.Equal("Late show", (await StoredAsync()).Name);
    }

    [Fact]
    public async Task RemovingAnEntry_SavesTheShorterList()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();
        cut.Find("#visualisation-add-entry").Click();
        cut.Find("#visualisation-add-entry").Click();
        cut.Find("#visualisation-preset").Change("0:_Mig_049");

        cut.FindAll(".kh-visualisations__remove-entry")[0].Click();

        Assert.Equal(["_Mig_049"], (await StoredAsync()).Entries.Select(e => e.PresetName));
    }

    [Fact]
    public async Task DeletingAPlaylist_OnceConfirmed_RemovesIt()
    {
        var cut = Render<VisualisationsManagerPage>();
        cut.Find("#visualisation-add-playlist").Click();

        cut.Find(".kh-visualisations__delete-playlist").Click();
        Assert.Single(await _playlists.ReadAllWithEntriesAsync());

        await cut.InvokeAsync(_confirm!);

        Assert.Empty(await _playlists.ReadAllWithEntriesAsync());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".kh-visualisations__playlist")));
    }

    /// <summary>The app ships this row; the page offers no way to remove it, matching how a
    /// built-in theme's Delete is disabled rather than hidden.</summary>
    [Fact]
    public async Task TheDefaultPlaylist_HasItsDeleteButtonDisabledWithATooltip()
    {
        await _playlists.CreateAsync(new VisualisationPlaylist { Id = VisualisationPlaylist.DefaultId, Name = "Default Visualizations" });
        var cut = Render<VisualisationsManagerPage>();

        var row = Assert.Single(cut.FindAll(".kh-visualisations__playlist"), r => r.TextContent.Contains("Default Visualizations"));
        var deleteButton = row.QuerySelector(".kh-visualisations__delete-playlist")!;

        Assert.True(deleteButton.HasAttribute("disabled"));
        Assert.Contains("cannot be deleted", deleteButton.GetAttribute("title"));

        deleteButton.Click();
        Assert.Single(await _playlists.ReadAllWithEntriesAsync());
    }

    [Fact]
    public async Task ThePlaylistTheVenueUses_IsMarkedInUse()
    {
        var used = await _playlists.CreateAsync(new VisualisationPlaylist { Name = "Night" });
        await _playlists.CreateAsync(new VisualisationPlaylist { Name = "Party" });
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Room", Settings = new() { VisualisationPlaylistId = used.Id } });

        var cut = Render<VisualisationsManagerPage>();

        var row = Assert.Single(cut.FindAll(".kh-visualisations__playlist"), r => r.QuerySelector(".kh-badge") is not null);
        Assert.Contains("Night", row.TextContent);
    }

    [Fact]
    public async Task ImportingAFile_HandsItToThePresetStore()
    {
        _presets.ImportAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new VisualiserPresetImport { Preset = new VisualiserPreset { Name = "Fresh", Source = VisualiserPresetSource.Imported } });
        var cut = Render<VisualisationsManagerPage>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("""{"baseVals":{}}""", "Fresh.json"));

        await _presets.Received(1).ImportAsync("Fresh.json", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        _flash.Received(1).Show("Imported the preset Fresh.", FlashType.Success);
    }

    [Fact]
    public void ARefusedImport_SaysWhy()
    {
        _presets.ImportAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new VisualiserPresetImport { Error = "That is not a butterchurn preset." });
        var cut = Render<VisualisationsManagerPage>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("nope", "Bad.milk"));

        _flash.Received(1).Show("Bad.milk: That is not a butterchurn preset.", FlashType.Warning);
    }
}
