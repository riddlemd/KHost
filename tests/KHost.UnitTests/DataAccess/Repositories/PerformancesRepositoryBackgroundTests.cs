using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>What a turn draws under its words is saved with it, through the migrated schema.</summary>
public class PerformancesRepositoryBackgroundTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-background-{Guid.NewGuid():N}.db");
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly PerformancesRepository _repository;

    public PerformancesRepositoryBackgroundTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new PerformancesRepository(_factory, NullLogger<BaseRepository<Performance>>.Instance);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Background_ALook_RoundTripsThroughTheDatabase()
    {
        var video = Guid.NewGuid();
        var created = await _repository.CreateAsync(Queued(new PerformanceBackground
        {
            Type = PerformanceBackgroundType.Look,
            PresetSource = VisualiserPresetSource.Video,
            VideoMediaId = video,
            Brightness = 140,
            Saturation = 60,
            Sensitivity = 250,
            BarCount = 64,
            ColourScheme = VisualiserColourScheme.Single,
            Colour = "#ff0000",
        }));

        var read = (await _repository.ReadAsync(created.Id))!.Background!;

        Assert.Equal((PerformanceBackgroundType.Look, VisualiserPresetSource.Video, (Guid?)video, 140, 60, 250, 64, VisualiserColourScheme.Single, "#ff0000"),
            (read.Type, read.PresetSource, read.VideoMediaId, read.Brightness, read.Saturation, read.Sensitivity, read.BarCount, read.ColourScheme, read.Colour));
    }

    [Fact]
    public async Task Background_Black_RoundTripsThroughTheDatabase()
    {
        var created = await _repository.CreateAsync(Queued(new PerformanceBackground { Type = PerformanceBackgroundType.Black }));

        Assert.Equal(PerformanceBackgroundType.Black, (await _repository.ReadAsync(created.Id))!.Background!.Type);
    }

    [Fact]
    public async Task Background_NeverSet_ReadsBackAsNull()
    {
        var created = await _repository.CreateAsync(Queued(null));

        Assert.Null((await _repository.ReadAsync(created.Id))!.Background);
    }

    [Fact]
    public async Task Background_AnUpdateSavesTheNewOne_AndNullClearsIt()
    {
        var created = await _repository.CreateAsync(Queued(new PerformanceBackground { Type = PerformanceBackgroundType.Black }));

        created.Background = new PerformanceBackground { Type = PerformanceBackgroundType.Look, PresetName = "_Mig_049" };
        await _repository.UpdateAsync(created);
        var looked = (await _repository.ReadAsync(created.Id))!.Background;

        created.Background = null;
        await _repository.UpdateAsync(created);

        Assert.Equal((PerformanceBackgroundType.Look, "_Mig_049"), (looked!.Type, looked.PresetName));
        Assert.Null((await _repository.ReadAsync(created.Id))!.Background);
    }

    [Fact]
    public void Background_ChangedInPlaceOnATrackedRow_IsSeenAsAChange()
    {
        using var context = _factory.CreateDbContext();
        var performance = Queued(new PerformanceBackground { Type = PerformanceBackgroundType.Look, Brightness = 100 });
        context.Set<Performance>().Add(performance);
        context.SaveChanges();

        context.ChangeTracker.DetectChanges();
        var untouched = context.Entry(performance).Property(p => p.Background).IsModified;

        performance.Background!.Brightness = 150;
        context.ChangeTracker.DetectChanges();

        Assert.False(untouched);
        Assert.True(context.Entry(performance).Property(p => p.Background).IsModified);
    }

    private static Performance Queued(PerformanceBackground? background) => new()
    {
        SingerId = Guid.NewGuid(),
        MediaId = Guid.NewGuid(),
        QueuePosition = 1,
        CreatedDate = new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc),
        Background = background,
    };
}
