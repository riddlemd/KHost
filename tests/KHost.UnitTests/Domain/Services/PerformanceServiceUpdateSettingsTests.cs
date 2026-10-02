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

/// <summary>A waiting turn's key, tempo and levels, saved to the database and read back the way
/// playback reads them at load.</summary>
public class PerformanceServiceUpdateSettingsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-perf-settings-{Guid.NewGuid():N}.db");
    private readonly PerformancesRepository _repository;
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly PerformanceService _service;

    public PerformanceServiceUpdateSettingsTests()
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
    public async Task UpdateSettingsAsync_SavesEveryValue_AndReadsBack()
    {
        var queued = await QueuedAsync();

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(-3, 20, 35, 60)
        {
            VoiceVolumes = new Dictionary<string, int> { ["♂"] = 25 },
        });

        var read = (await _repository.ReadAsync(queued.Id))!;
        Assert.Equal(-3, read.Pitch);
        Assert.Equal(20, read.Tempo);
        Assert.Equal(35, read.LeadVolume);
        Assert.Equal(60, read.BackingVolume);
        Assert.Equal(new Dictionary<string, int> { ["♂"] = 25 }, read.VoiceVolumes);
    }

    [Fact]
    public async Task UpdateSettingsAsync_HoldsEachValueToItsRange()
    {
        var queued = await QueuedAsync();

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(20, -90, 150, -5)
        {
            VoiceVolumes = new Dictionary<string, int> { ["♀"] = 300 },
        });

        var read = (await _repository.ReadAsync(queued.Id))!;
        Assert.Equal(IPlaybackService.MaxPitch, read.Pitch);
        Assert.Equal(IPlaybackService.MinTempo, read.Tempo);
        Assert.Equal(AudioMix.MaxVolume, read.LeadVolume);
        Assert.Equal(AudioMix.MinVolume, read.BackingVolume);
        Assert.Equal(AudioMix.MaxVolume, read.VoiceVolumes!["♀"]);
    }

    [Fact]
    public async Task UpdateSettingsAsync_TheOtherEnds_AreHeldToo()
    {
        var queued = await QueuedAsync();

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(-20, 90, -1, 101));

        var read = (await _repository.ReadAsync(queued.Id))!;
        Assert.Equal(IPlaybackService.MinPitch, read.Pitch);
        Assert.Equal(IPlaybackService.MaxTempo, read.Tempo);
        Assert.Equal(AudioMix.MinVolume, read.LeadVolume);
        Assert.Equal(AudioMix.MaxVolume, read.BackingVolume);
    }

    /// <summary>Null is "nobody mixed this", which keeps the turn on the machine setting.</summary>
    [Fact]
    public async Task UpdateSettingsAsync_NullBacking_ClearsItBackToTheMachineSetting()
    {
        var queued = await QueuedAsync(backing: 40);

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(0, 0, 0, null));

        Assert.Null((await _repository.ReadAsync(queued.Id))!.BackingVolume);
    }

    [Fact]
    public async Task UpdateSettingsAsync_MergesVoicesOverThoseSaved()
    {
        var queued = await QueuedAsync(voices: new() { ["♂"] = 10, ["♀"] = 20 });

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(0, 0, 0, null)
        {
            VoiceVolumes = new Dictionary<string, int> { ["♂"] = 50 },
        });

        Assert.Equal(new Dictionary<string, int> { ["♂"] = 50, ["♀"] = 20 },
            (await _repository.ReadAsync(queued.Id))!.VoiceVolumes);
    }

    [Fact]
    public async Task UpdateSettingsAsync_NoVoices_LeavesTheSavedOnesAlone()
    {
        var queued = await QueuedAsync(voices: new() { ["♂"] = 10 });

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(2, 0, 0, null));

        Assert.Equal(new Dictionary<string, int> { ["♂"] = 10 }, (await _repository.ReadAsync(queued.Id))!.VoiceVolumes);
    }

    /// <summary>Only how it is sung changes: the turn keeps its place, its singer and its name.</summary>
    [Fact]
    public async Task UpdateSettingsAsync_LeavesTheTurnsPlaceAndNameAlone()
    {
        var queued = await QueuedAsync();

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(1, 5, 0, null));

        var read = (await _repository.ReadAsync(queued.Id))!;
        Assert.Equal(3, read.QueuePosition);
        Assert.Equal("flo", read.SungAs);
        Assert.Equal(queued.SingerId, read.SingerId);
    }

    [Fact]
    public async Task UpdateSettingsAsync_AnnouncesOnce_SoTheQueueRedraws()
    {
        var queued = await QueuedAsync();
        var raised = 0;
        using var subscription = _broker.Subscribe<PerformancesChanged>(_ => raised++);

        await _service.UpdateSettingsAsync(queued.Id, new PerformanceSettings(1, 0, 0, null));

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task UpdateSettingsAsync_NoSuchTurn_ReturnsNullAndAnnouncesNothing()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<PerformancesChanged>(_ => raised++);

        var result = await _service.UpdateSettingsAsync(Guid.NewGuid(), new PerformanceSettings(1, 0, 0, null));

        Assert.Null(result);
        Assert.Equal(0, raised);
    }

    private Task<Performance> QueuedAsync(int? backing = null, Dictionary<string, int>? voices = null)
        => _repository.CreateAsync(new Performance
        {
            SingerId = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            QueuePosition = 3,
            SungAs = "flo",
            BackingVolume = backing,
            VoiceVolumes = voices,
            CreatedDate = new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc),
        });
}
