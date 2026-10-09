using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KHost.Abstractions.Services;
using KHost.DataAccess.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Services;

public class DatabaseInitializerTests
{
    private readonly IVisualisationPlaylistService _visualisationPlaylistService = Substitute.For<IVisualisationPlaylistService>();
    private readonly IVisualiserPresetService _visualiserPresetService = Substitute.For<IVisualiserPresetService>();

    private DatabaseInitializer CreateSut(IDbContextFactory<DefaultContext>? factory = null)
        => new(
            factory!,
            NullLogger<DatabaseInitializer>.Instance,
            _visualisationPlaylistService,
            _visualiserPresetService);


    [Fact]
    public async Task EnsureShippedVisualisationPlaylistsAsync_DoesNothing_WhenBothRowsAreThere()
    {
        StubPlaylist(ShippedVisualisationPlaylists.BasicId);
        StubPlaylist(ShippedVisualisationPlaylists.AdvancedId);
        var sut = CreateSut();

        await sut.EnsureShippedVisualisationPlaylistsAsync();

        await _visualisationPlaylistService.DidNotReceive().CreateAsync(Arg.Any<VisualisationPlaylist>());
    }

    [Fact]
    public async Task EnsureShippedVisualisationPlaylistsAsync_RecreatesBasicFromTheLiveShapeScenes_WhenItIsGone()
    {
        StubPlaylist(ShippedVisualisationPlaylists.AdvancedId);
        StubLiveAmbientSet();
        var sut = CreateSut();

        await sut.EnsureShippedVisualisationPlaylistsAsync();

        await _visualisationPlaylistService.Received(1).CreateAsync(Arg.Any<VisualisationPlaylist>());
        await _visualisationPlaylistService.Received(1).CreateAsync(
            Arg.Is<VisualisationPlaylist>(p => p.Id == ShippedVisualisationPlaylists.BasicId && p.Name == "Basic Backgrounds"));
        await _visualisationPlaylistService.Received(1).ReplaceEntriesAsync(ShippedVisualisationPlaylists.BasicId,
            Arg.Is<IReadOnlyList<VisualisationEntry>>(entries => entries.Select(e => e.PresetName).SequenceEqual(new[] { "ambient-gradient", "ambient-bokeh" })));
    }

    [Fact]
    public async Task EnsureShippedVisualisationPlaylistsAsync_RecreatesAdvancedFromTheLiveShaderScenes_WhenItIsGone()
    {
        StubPlaylist(ShippedVisualisationPlaylists.BasicId);
        StubLiveAmbientSet();
        var sut = CreateSut();

        await sut.EnsureShippedVisualisationPlaylistsAsync();

        await _visualisationPlaylistService.Received(1).CreateAsync(Arg.Any<VisualisationPlaylist>());
        await _visualisationPlaylistService.Received(1).CreateAsync(
            Arg.Is<VisualisationPlaylist>(p => p.Id == ShippedVisualisationPlaylists.AdvancedId && p.Name == "Advanced Backgrounds"));
        await _visualisationPlaylistService.Received(1).ReplaceEntriesAsync(ShippedVisualisationPlaylists.AdvancedId,
            Arg.Is<IReadOnlyList<VisualisationEntry>>(entries => entries.Select(e => e.PresetName).SequenceEqual(new[] { "ambient-clouds", "ambient-nebula" })
                && entries.All(e => e.PresetSource == VisualiserPresetSource.BuiltIn)));
    }

    /// <summary>The screen draws a field scene by the name the host sends, so the advanced set must
    /// name exactly the scenes the screen draws with a shader.</summary>
    [Fact]
    public void AdvancedScenes_AreTheFieldScenesTheScreenShips()
    {
        var script = File.ReadAllText(Path.Combine(KHost.UnitTests.Domain.Services.Visualisations.VisualiserPresetServiceTests.RepositoryRoot(),
            "src", "KHost.LocalScreen", "screen-ui", "eq-visualisers.js"));
        var fields = script[script.IndexOf("const AMBIENT_FIELDS = {", StringComparison.Ordinal)..];
        fields = fields[..fields.IndexOf("\n};", StringComparison.Ordinal)];
        var names = System.Text.RegularExpressions.Regex.Matches(fields, @"^    '([^']+)':", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value);

        Assert.Equal(names, ShippedVisualisationPlaylists.AdvancedScenes);
    }

    private void StubPlaylist(Guid id)
        => _visualisationPlaylistService.ReadWithEntriesAsync(id).Returns(new VisualisationPlaylist { Id = id, Name = "Kept" });

    private void StubLiveAmbientSet()
    {
        _visualiserPresetService.ReadAll().Returns(
        [
            new VisualiserPreset { Name = "spectrum-bars", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-gradient", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-clouds", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-bokeh", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "ambient-nebula", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "retro-static", Source = VisualiserPresetSource.BuiltIn },
            new VisualiserPreset { Name = "Rovastar - Oozing Resistance", Source = VisualiserPresetSource.Bundled },
        ]);
        _visualisationPlaylistService.CreateAsync(Arg.Any<VisualisationPlaylist>()).Returns(c => c.Arg<VisualisationPlaylist>());
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_FlipsDownloadingRowsToBroken()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("stalled.mp4", MediaStatus.Downloading));
            var sut = CreateSut(factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(MediaStatus.Broken, context.Media.Single(m => m.FilePath == "stalled.mp4").Status);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_FlipsProcessingRowsToBroken()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            // A row left mid-render is as stalled as one left mid-download: nothing survives the
            // process to finish it, and nothing else in the app can move it out of Processing.
            SeedMedia(factory, ("rendering.khv", MediaStatus.Processing));
            var sut = CreateSut(factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(MediaStatus.Broken, context.Media.Single(m => m.FilePath == "rendering.khv").Status);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_NeverDeletesTheRow()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("stalled.mp4", MediaStatus.Downloading));
            var sut = CreateSut(factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(1, context.Media.Count(m => m.FilePath == "stalled.mp4"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_LeavesReadyAndBrokenRowsAlone()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("ready.mp4", MediaStatus.Ready), ("broken.mp4", MediaStatus.Broken));
            var sut = CreateSut(factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(MediaStatus.Ready, context.Media.Single(m => m.FilePath == "ready.mp4").Status);
            Assert.Equal(MediaStatus.Broken, context.Media.Single(m => m.FilePath == "broken.mp4").Status);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_CountsRowsItSwept()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("a.mp4", MediaStatus.Downloading), ("b.mp4", MediaStatus.Downloading), ("c.mp4", MediaStatus.Ready));
            var sut = CreateSut(factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(2, context.Media.Count(m => m.Status == MediaStatus.Broken));
        }
        finally { Delete(dbPath); }
    }

    private static void SeedMedia(IDbContextFactory<DefaultContext> factory, params (string FilePath, MediaStatus Status)[] rows)
    {
        using var context = factory.CreateDbContext();
        foreach (var (filePath, status) in rows)
            context.Media.Add(new Media { FilePath = filePath, Title = filePath, Status = status });
        context.SaveChanges();
    }

    /// <summary>A provider's remote room can outlive the host, so a restart must not cut every guest
    /// loose from their singer; the source that wrote a key is the one that clears it.</summary>
    [Fact]
    public async Task InitializeAsync_KeepsEphemeralForeignKeys()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            var ada = new KHostUser { Name = "Ada" };
            using (var seed = factory.CreateDbContext())
            {
                seed.Users.Add(ada);
                seed.UserForeignKeys.AddRange(
                    new KHostUserForeignKey { UserId = ada.Id, Source = "Example", Key = "remote-1", IsEphemeral = true },
                    new KHostUserForeignKey { UserId = ada.Id, Source = "Example", Key = "account-1", IsEphemeral = false });
                await seed.SaveChangesAsync();
            }

            StubPlaylist(ShippedVisualisationPlaylists.BasicId);
            StubPlaylist(ShippedVisualisationPlaylists.AdvancedId);
            var sut = CreateSut(factory);

            await sut.InitializeAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(["account-1", "remote-1"], context.UserForeignKeys.Select(k => k.Key).OrderBy(k => k).ToArray());
        }
        finally { Delete(dbPath); }
    }

    private static (IDbContextFactory<DefaultContext> Factory, string Path) NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"khost-dbinit-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(o =>
            o.UseSqlite($"Data Source={path}").UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        var factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();
        using (var context = factory.CreateDbContext()) context.Database.Migrate();
        return (factory, path);
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}