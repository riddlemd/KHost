using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>Each singer's lead level is part of how a song was sung, and comes back with it.</summary>
public class PerformancesRepositoryVoiceVolumesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-voices-{Guid.NewGuid():N}.db");
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly PerformancesRepository _repository;

    public PerformancesRepositoryVoiceVolumesTests()
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
    public async Task VoiceVolumes_RoundTripThroughTheDatabase()
    {
        var created = await _repository.CreateAsync(Sung(new() { ["♂"] = 25, ["♀"] = 0 }));

        var read = await _repository.ReadAsync(created.Id);

        Assert.Equal(new Dictionary<string, int> { ["♂"] = 25, ["♀"] = 0 }, read!.VoiceVolumes);
    }

    [Fact]
    public async Task VoiceVolumes_AnUpdateSavesTheNewLevels()
    {
        var created = await _repository.CreateAsync(Sung(new() { ["♂"] = 25 }));

        created.VoiceVolumes = new() { ["♂"] = 70 };
        await _repository.UpdateAsync(created);

        Assert.Equal(70, (await _repository.ReadAsync(created.Id))!.VoiceVolumes!["♂"]);
    }

    [Fact]
    public async Task VoiceVolumes_NeverSet_ReadBackAsNull()
    {
        var created = await _repository.CreateAsync(Sung(null));

        // Null, not empty: nobody mixed this one, so every singer sits at the default.
        Assert.Null((await _repository.ReadAsync(created.Id))!.VoiceVolumes);
    }

    [Fact]
    public void VoiceVolumes_ChangedInPlaceOnATrackedRow_IsSeenAsAChange()
    {
        using var context = _factory.CreateDbContext();
        var performance = Sung(new() { ["♂"] = 25 });
        context.Set<Performance>().Add(performance);
        context.SaveChanges();

        context.ChangeTracker.DetectChanges();
        var untouched = context.Entry(performance).Property(p => p.VoiceVolumes).IsModified;

        performance.VoiceVolumes!["♂"] = 80;
        context.ChangeTracker.DetectChanges();

        // Compared by content both ways: an equal copy is no change, an edit in place is one.
        Assert.False(untouched);
        Assert.True(context.Entry(performance).Property(p => p.VoiceVolumes).IsModified);
    }

    private static Performance Sung(Dictionary<string, int>? voices) => new()
    {
        SingerId = Guid.NewGuid(),
        MediaId = Guid.NewGuid(),
        CreatedDate = new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc),
        VoiceVolumes = voices,
    };
}
