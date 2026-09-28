using System.Text;
using System.Text.RegularExpressions;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Visualisations;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Visualisations;

public class VisualiserPresetServiceTests : IDisposable
{
    private const string Preset = """{"baseVals":{"decay":0.98},"shapes":[],"waves":[],"frame_eqs_str":"a.zoom=1;"}""";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-presets-{Guid.NewGuid():N}");
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly VisualiserPresetService _service;

    public VisualiserPresetServiceTests()
        => _service = new VisualiserPresetService(NullLogger<VisualiserPresetService>.Instance, _broker, _directory);

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private Task<VisualiserPresetImport> ImportAsync(string fileName, string content)
        => _service.ImportAsync(fileName, new MemoryStream(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public async Task ImportAsync_AButterchurnPreset_IsStoredUnderItsFileName()
    {
        var result = await ImportAsync("My Swirl.json", Preset);

        Assert.Null(result.Error);
        Assert.Equal("My Swirl", result.Preset!.Name);
        Assert.Equal(VisualiserPresetSource.Imported, result.Preset.Source);
        Assert.Equal(Preset, await _service.ReadImportedAsync("My Swirl"));
        Assert.True(File.Exists(Path.Combine(_directory, "My Swirl.json")));
    }

    [Theory]
    [InlineData("not json at all", "MilkDrop .milk")]
    [InlineData("[1,2,3]", "not an object")]
    [InlineData("""{"shapes":[]}""", "no baseVals")]
    [InlineData("""{"baseVals":7}""", "no baseVals")]
    [InlineData("""{"baseVals":{},"shapes":{}}""", "shapes is not a list")]
    [InlineData("""{"baseVals":{},"waves":"x"}""", "waves is not a list")]
    [InlineData("""{"baseVals":{},"frame_eqs_str":42}""", "frame_eqs_str is not text")]
    public async Task ImportAsync_NotAButterchurnPreset_IsRefusedAndNothingIsStored(string content, string says)
    {
        var result = await ImportAsync("Bad.json", content);

        Assert.Null(result.Preset);
        Assert.Contains(says, result.Error);
        Assert.False(File.Exists(Path.Combine(_directory, "Bad.json")));
    }

    [Fact]
    public async Task ImportAsync_ALargerFileThanAnyPreset_IsRefused()
    {
        var padding = new string(' ', VisualiserPresetService.MaxBytes);
        var result = await ImportAsync("Huge.json", Preset + padding);

        Assert.Null(result.Preset);
        Assert.Contains("KB", result.Error);
        Assert.False(File.Exists(Path.Combine(_directory, "Huge.json")));
    }

    [Fact]
    public async Task ImportAsync_JustInsideTheLimit_IsTaken()
    {
        var content = Preset + new string(' ', VisualiserPresetService.MaxBytes - Preset.Length);

        Assert.Null((await ImportAsync("Big.json", content)).Error);
    }

    [Fact]
    public async Task ImportAsync_TheSameNameAgain_ReplacesIt()
    {
        await ImportAsync("Swirl.json", Preset);

        var again = await ImportAsync("Swirl.json", """{"baseVals":{"decay":0.5}}""");

        Assert.True(again.Replaced);
        Assert.Contains("0.5", await _service.ReadImportedAsync("Swirl"));
    }

    [Theory]
    [InlineData("../../escape.json", "escape")]
    [InlineData("C:\\presets\\Mine.json", "Mine")]
    [InlineData("a:b*c?.json", "a_b_c_")]
    public async Task ImportAsync_ANameThatWouldLeaveTheFolder_IsKeptInsideIt(string fileName, string stored)
    {
        var result = await ImportAsync(fileName, Preset);

        Assert.Equal(stored, result.Preset!.Name);
        Assert.Equal([Path.Combine(_directory, stored + ".json")], Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".json")]
    [InlineData("...json")]
    public async Task ImportAsync_NoUsableName_IsRefused(string fileName)
        => Assert.NotNull((await ImportAsync(fileName, Preset)).Error);

    [Fact]
    public async Task ImportAsync_Announces()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<VisualiserPresetsChanged>(_ => raised++);

        await ImportAsync("Swirl.json", Preset);
        await ImportAsync("Bad.json", "nope");

        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("..")]
    [InlineData("sub/dir")]
    public async Task ReadImportedAsync_ANameFromOutsideTheFolder_ReadsNothing(string name)
    {
        // A real file where the escape would land, beside the store's own folder.
        Directory.CreateDirectory(_directory);
        var outside = Path.Combine(Path.GetDirectoryName(_directory)!, "secret.json");
        await File.WriteAllTextAsync(outside, "{}");

        try
        {
            Assert.Null(await _service.ReadImportedAsync(name));
            Assert.False(_service.DeleteImported(name));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ReadAll_ListsTheBuiltInsThenTheShippedPresetsThenTheImportedOnesByName()
    {
        await ImportAsync("zebra.json", Preset);
        await ImportAsync("Aurora.json", Preset);

        var all = _service.ReadAll();
        var builtIns = VisualiserPresetService.BuiltIns.Count;
        var bundled = VisualiserPresetService.BundledNames.Count;

        Assert.Equal(VisualiserPresetService.BuiltIns, all.Take(builtIns).Select(p => (p.Name, p.Title!)));
        Assert.All(all.Take(builtIns), p => Assert.Equal(VisualiserPresetSource.BuiltIn, p.Source));
        Assert.Equal(VisualiserPresetService.BundledNames, all.Skip(builtIns).Take(bundled).Select(p => p.Name));
        Assert.All(all.Skip(builtIns).Take(bundled), p => Assert.Equal(VisualiserPresetSource.Bundled, p.Source));
        Assert.Equal(["Aurora", "zebra"], all.Skip(builtIns + bundled).Select(p => p.Name));
    }

    [Fact]
    public void ReadAll_NoFolderYet_ListsOnlyWhatTheHostShips()
        => Assert.Equal(VisualiserPresetService.BuiltIns.Count + VisualiserPresetService.BundledNames.Count, _service.ReadAll().Count);

    /// <summary>The host sends a built-in by name and the screen draws it from its own list, so the
    /// two must name the same styles.</summary>
    [Fact]
    public void BuiltIns_AreTheDrawingsTheScreenShips()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "KHost.LocalScreen", "screen-ui", "eq-visualisers.js"));
        var styles = Regex.Matches(script, @"\{ name: '([^']+)', title: '([^']+)' \}")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value));

        Assert.Equal(styles, VisualiserPresetService.BuiltIns);
    }

    [Fact]
    public async Task DeleteImported_RemovesItAndAnnounces()
    {
        await ImportAsync("Swirl.json", Preset);
        var raised = 0;
        using var subscription = _broker.Subscribe<VisualiserPresetsChanged>(_ => raised++);

        Assert.True(_service.DeleteImported("Swirl"));
        Assert.False(_service.DeleteImported("Swirl"));

        Assert.Null(await _service.ReadImportedAsync("Swirl"));
        Assert.Equal(1, raised);
    }

    /// <summary>The host sends a shipped preset by name, and the screen looks it up in its own file,
    /// so the two lists must name the same presets.</summary>
    [Fact]
    public void BundledNames_AreThePresetsTheScreenShips()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "KHost.LocalScreen", "screen-ui", "visualiser-presets.js"));
        var names = Regex.Matches(script, @"\{ name: (""(?:[^""\\]|\\.)*""), preset:")
            .Select(m => System.Text.Json.JsonSerializer.Deserialize<string>(m.Groups[1].Value)!);

        Assert.Equal(names, VisualiserPresetService.BundledNames);
    }

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.GetFiles("KHost.slnx").Length > 0)
                return directory.FullName;
        }

        throw new InvalidOperationException($"No KHost.slnx above {AppContext.BaseDirectory}, so the repository root could not be found.");
    }
}
