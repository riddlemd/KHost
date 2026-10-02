using KHost.Abstractions.Interactions;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Models;

/// <summary>An Edit Performance save writes only the parts that moved, each through the service
/// that owns it, against the stored row rather than the copy the dialog was opened on.</summary>
public class PerformanceEditTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-perf-edit-{Guid.NewGuid():N}.db");
    private readonly PerformancesRepository _repository;
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly PerformanceService _service;

    public PerformanceEditTests()
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
    public async Task SaveAsync_AliasAlone_RenamesAndLeavesTheLevelsAsStored()
    {
        var row = await StoredAsync();
        var raised = Count();

        await new PerformanceEdit { SungAsChanged = true, SungAs = "DJ P" }.SaveAsync(_service, row.Id);

        var read = (await _repository.ReadAsync(row.Id))!;
        Assert.Equal("DJ P", read.SungAs);
        Assert.Equal(-2, read.Pitch);
        Assert.Equal(40, read.BackingVolume);
        Assert.Equal(1, raised());
    }

    /// <summary>The queue moved after the dialog opened: the rename must not put the old place back.</summary>
    [Fact]
    public async Task SaveAsync_AliasAlone_WritesOverTheStoredRowNotAStaleCopy()
    {
        var row = await StoredAsync();
        var stored = (await _repository.ReadAsync(row.Id))!;
        stored.QueuePosition = 1;
        stored.Pitch = 4;
        await _repository.UpdateAsync(stored);

        await new PerformanceEdit { SungAsChanged = true, SungAs = "DJ P" }.SaveAsync(_service, row.Id);

        var read = (await _repository.ReadAsync(row.Id))!;
        Assert.Equal(1, read.QueuePosition);
        Assert.Equal(4, read.Pitch);
    }

    [Fact]
    public async Task SaveAsync_ABlankedAlias_StoresNoName()
    {
        var row = await StoredAsync();

        await new PerformanceEdit { SungAsChanged = true, SungAs = null }.SaveAsync(_service, row.Id);

        Assert.Null((await _repository.ReadAsync(row.Id))!.SungAs);
    }

    [Fact]
    public async Task SaveAsync_ControlsAlone_KeepTheNameAndAnnounceOnce()
    {
        var row = await StoredAsync();
        var raised = Count();

        await new PerformanceEdit { Settings = new PerformanceSettings(3, 10, 50, null) }.SaveAsync(_service, row.Id);

        var read = (await _repository.ReadAsync(row.Id))!;
        Assert.Equal(3, read.Pitch);
        Assert.Equal(10, read.Tempo);
        Assert.Equal("flo", read.SungAs);
        Assert.Equal(1, raised());
    }

    [Fact]
    public async Task SaveAsync_Both_WritesBothAndAnnouncesEach()
    {
        var row = await StoredAsync();
        var raised = Count();

        await new PerformanceEdit
        {
            SungAsChanged = true,
            SungAs = "DJ P",
            Settings = new PerformanceSettings(3, 10, 50, null),
        }.SaveAsync(_service, row.Id);

        var read = (await _repository.ReadAsync(row.Id))!;
        Assert.Equal("DJ P", read.SungAs);
        Assert.Equal(3, read.Pitch);
        Assert.Equal(2, raised());
    }

    [Fact]
    public async Task SaveAsync_NothingChanged_WritesAndAnnouncesNothing()
    {
        var row = await StoredAsync();
        var raised = Count();

        await new PerformanceEdit().SaveAsync(_service, row.Id);

        Assert.Equal(0, raised());
        Assert.Equal("flo", (await _repository.ReadAsync(row.Id))!.SungAs);
    }

    /// <summary>A sung turn is edited the same way: the history row is the record re-queueing reads.</summary>
    [Fact]
    public async Task SaveAsync_APastPerformance_RewritesTheRecordAndStaysInHistory()
    {
        var row = await StoredAsync(queuePosition: null);

        await new PerformanceEdit
        {
            SungAsChanged = true,
            SungAs = "DJ P",
            Settings = new PerformanceSettings(1, -5, 50, 70),
        }.SaveAsync(_service, row.Id);

        var read = (await _repository.ReadAsync(row.Id))!;
        Assert.Null(read.QueuePosition);
        Assert.Equal("DJ P", read.SungAs);
        Assert.Equal(1, read.Pitch);
        Assert.Equal(70, read.BackingVolume);
    }

    private Func<int> Count()
    {
        var raised = 0;
        _ = _broker.Subscribe<PerformancesChanged>(_ => raised++);
        return () => raised;
    }

    private Task<Performance> StoredAsync(int? queuePosition = 3)
        => _repository.CreateAsync(new Performance
        {
            SingerId = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            QueuePosition = queuePosition,
            SungAs = "flo",
            Pitch = -2,
            BackingVolume = 40,
            CreatedDate = new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc),
        });
}
