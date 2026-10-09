using KHost.Abstractions.Interactions;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A waiting turn's own background, saved to the database held to the playlist ranges.</summary>
public class PerformanceServiceUpdateBackgroundTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-perf-background-{Guid.NewGuid():N}.db");
    private readonly PerformancesRepository _repository;
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly PerformanceService _service;

    public PerformanceServiceUpdateBackgroundTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        var factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();
        using (var context = factory.CreateDbContext())
            context.Database.Migrate();

        _repository = new PerformancesRepository(factory, NullLogger<BaseRepository<Performance>>.Instance);

        _service = new PerformanceService(
            NullLogger<PerformanceService>.Instance,
            _repository,
            Substitute.For<IMediaService>(),
            Substitute.For<IUsersService>(),
            Substitute.For<IVenuesService>(),
            Substitute.For<IInteractionDispatcher>(),
            Substitute.For<IDownloadsService>(),
            Substitute.For<IServiceProvider>(),
            _broker);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UpdateBackgroundAsync_ALook_IsSavedHeldToTheRanges()
    {
        var queued = await QueuedAsync();

        await _service.UpdateBackgroundAsync(queued.Id, new PerformanceBackground
        {
            Type = PerformanceBackgroundType.Look,
            PresetSource = VisualiserPresetSource.BuiltIn,
            PresetName = "spectrum-bars",
            Brightness = 999,
            Colour = "nonsense",
        });

        var read = (await _repository.ReadAsync(queued.Id))!.Background!;
        Assert.Equal((PerformanceBackgroundType.Look, "spectrum-bars", VisualisationEntry.MaxBrightness, VisualisationEntry.DefaultColour),
            (read.Type, read.PresetName, read.Brightness, read.Colour));
    }

    [Fact]
    public async Task UpdateBackgroundAsync_Null_HandsTheTurnBackToTheVenuesPlaylist()
    {
        var queued = await QueuedAsync(new PerformanceBackground { Type = PerformanceBackgroundType.Black });

        var saved = await _service.UpdateBackgroundAsync(queued.Id, null);

        Assert.NotNull(saved);
        Assert.Null((await _repository.ReadAsync(queued.Id))!.Background);
    }

    [Fact]
    public async Task UpdateBackgroundAsync_AnnouncesOnce_SoTheQueueRedraws()
    {
        var queued = await QueuedAsync();
        var raised = 0;
        using var subscription = _broker.Subscribe<PerformancesChanged>(_ => raised++);

        await _service.UpdateBackgroundAsync(queued.Id, new PerformanceBackground { Type = PerformanceBackgroundType.Black });

        Assert.Equal(1, raised);
    }

    /// <summary>A sung turn is history: nothing draws it again.</summary>
    [Fact]
    public async Task UpdateBackgroundAsync_ATurnNoLongerQueued_SavesNothingAndAnnouncesNothing()
    {
        var sung = await QueuedAsync(position: null);
        var raised = 0;
        using var subscription = _broker.Subscribe<PerformancesChanged>(_ => raised++);

        var result = await _service.UpdateBackgroundAsync(sung.Id, new PerformanceBackground { Type = PerformanceBackgroundType.Black });

        Assert.Null(result);
        Assert.Null((await _repository.ReadAsync(sung.Id))!.Background);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task UpdateBackgroundAsync_NoSuchTurn_ReturnsNullAndAnnouncesNothing()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<PerformancesChanged>(_ => raised++);

        var result = await _service.UpdateBackgroundAsync(Guid.NewGuid(), null);

        Assert.Null(result);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_ABackground_IsSavedHeldToTheRanges()
    {
        var created = await _service.CreateAndEnqueueAsync(new Performance
        {
            SingerId = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Background = new PerformanceBackground { Type = PerformanceBackgroundType.Look, PresetName = "_Mig_049", Saturation = -10 },
        });

        var read = (await _repository.ReadAsync(created!.Id))!.Background!;
        Assert.Equal(("_Mig_049", VisualisationEntry.MinSaturation), (read.PresetName, read.Saturation));
    }

    private Task<Performance> QueuedAsync(PerformanceBackground? background = null, int? position = 3)
        => _repository.CreateAsync(new Performance
        {
            SingerId = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            QueuePosition = position,
            Background = background,
            CreatedDate = new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc),
        });
}
