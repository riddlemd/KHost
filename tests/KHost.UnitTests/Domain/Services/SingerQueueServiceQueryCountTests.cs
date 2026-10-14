using System.Data.Common;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.QueueRotation;
using KHost.UnitTests.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Counts the SQL each queue action sends, against real repositories over SQLite.</summary>
public class SingerQueueServiceQueryCountTests : IDisposable
{
    private static readonly DateTime Evening = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"khost-queue-count-{Guid.NewGuid():N}");
    private QueueRotationContext? _lastRotation;

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    [Fact]
    public async Task QueueActions_SendTheSameNumberOfQueries_HoweverManySingersWait()
    {
        var few = await MeasureAsync(singers: 3);
        var many = await MeasureAsync(singers: 30);

        Assert.Equal(few, many);
    }

    [Fact]
    public async Task Users_ComeBackInQueueOrder_WithoutSingersWhoNoLongerExist()
    {
        using var database = new SqliteTestDatabase();
        var users = Enumerable.Range(0, 12).Select(i => new KHostUser { Name = $"Singer {i}" }).ToList();
        await database.SeedAsync([.. users]);
        var vip = new KHostUserGroup { Name = "VIP" };
        await database.SeedAsync(vip);
        using var service = Build(database, Path.Combine(_cacheDir, "order"), vip.Id);

        var queued = users.Select(u => u.Id).Reverse().ToList();
        var gone = Guid.NewGuid();
        queued.Insert(5, gone);
        foreach (var id in queued)
            await service.AddUserAsync(id);

        queued.Remove(gone);
        Assert.Equal(queued, service.Users.Select(u => u.Id));
    }

    [Fact]
    public async Task Rotation_SeesEachSingersLastSungSongAndGroups()
    {
        using var database = new SqliteTestDatabase();
        var vip = new KHostUserGroup { Name = "VIP" };
        var regular = new KHostUserGroup { Name = "Thursday crowd" };
        var alice = new KHostUser { Name = "Alice", Groups = [vip, regular] };
        var bob = new KHostUser { Name = "Bob" };
        var carol = new KHostUser { Name = "Carol", Groups = [regular] };
        await database.SeedAsync(vip, regular, alice, bob, carol,
            Sung(alice, Evening), Sung(alice, Evening.AddHours(1)),
            Sung(carol, Evening.AddMinutes(30)),
            new Performance { SingerId = carol.Id, MediaId = Guid.NewGuid(), CreatedDate = Evening.AddHours(2), QueuePosition = 1 });
        using var service = Build(database, Path.Combine(_cacheDir, "rotation"), vip.Id);

        await service.AddUserAsync(alice.Id);
        await service.AddUserAsync(bob.Id);
        await service.AddUserAsync(carol.Id);

        var queue = Assert.IsType<QueueRotationContext>(_lastRotation).Queue.ToDictionary(s => s.Id);
        Assert.Equal(Evening.AddHours(1), queue[alice.Id].LastSangOn);
        Assert.Null(queue[bob.Id].LastSangOn);
        Assert.Equal(Evening.AddMinutes(30), queue[carol.Id].LastSangOn);
        Assert.Equal([regular.Id, vip.Id], queue[alice.Id].GroupIds.Order());
        Assert.Empty(queue[bob.Id].GroupIds);
        Assert.Equal([regular.Id], queue[carol.Id].GroupIds);
    }

    private static Performance Sung(KHostUser singer, DateTime on)
        => new() { SingerId = singer.Id, MediaId = Guid.NewGuid(), CreatedDate = on };

    private async Task<QueryCounts> MeasureAsync(int singers)
    {
        var counter = new CommandCounter();
        using var database = new SqliteTestDatabase(builder => builder.AddInterceptors(counter));
        var vip = new KHostUserGroup { Name = "VIP" };
        var users = Enumerable.Range(0, singers + 1)
            .Select(i => new KHostUser { Name = $"Singer {i}", Groups = [vip] })
            .ToList();
        var sung = users.Select(u => new Performance
        {
            SingerId = u.Id,
            MediaId = Guid.NewGuid(),
            CreatedDate = Evening,
        });

        await database.SeedAsync([vip, .. users, .. sung]);

        using var service = Build(database, Path.Combine(_cacheDir, $"count-{singers}"), vip.Id);

        foreach (var user in users.Take(singers))
            await service.AddUserAsync(user.Id);

        counter.Reset();
        await service.AddUserAsync(users[^1].Id);
        var add = counter.Count;

        counter.Reset();
        await service.RotateQueueAsync(users[0].Id);
        var rotate = counter.Count;

        counter.Reset();
        await service.RefreshAsync();
        var refresh = counter.Count;

        counter.Reset();
        await service.MoveUserUpAsync(users[^1].Id);
        var move = counter.Count;

        Assert.Equal(singers + 1, service.Users.Count);

        return new QueryCounts(add, rotate, refresh, move);
    }

    private SingerQueueService Build(SqliteTestDatabase database, string cacheDir, Guid vipGroupId)
    {
        var usersRepository = new UsersRepository(database, NullLogger<BaseRepository<KHostUser>>.Instance);
        var performancesRepository = new PerformancesRepository(database, NullLogger<BaseRepository<Performance>>.Instance);

        var performanceService = Substitute.For<IPerformanceService>();
        performanceService.ReadBySingerIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(ci => performancesRepository.ReadBySingerIdAsync((Guid)ci[0], (int)ci[1], (int)ci[2], (PerformanceFilter)ci[3], (DateTime?)ci[4]));
        performanceService.CountSungSinceAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>())
            .Returns(ci => performancesRepository.CountSungSinceAsync((IEnumerable<Guid>)ci[0], (DateTime)ci[1]));

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "Room",
            Settings = new Venue.VenueSettings { QueueRotation = new QueueRotationConfig { VipGroupId = vipGroupId } },
        });

        var strategy = Substitute.For<IQueueRotationStrategy>();
        strategy.ApplyAsync(Arg.Any<QueueRotationContext>())
            .Returns(ci =>
            {
                _lastRotation = (QueueRotationContext)ci[0];
                return Task.FromResult<IReadOnlyList<Guid>>(_lastRotation.Queue.Select(s => s.Id).ToList());
            });
        var rotation = Substitute.For<IQueueRotationStrategyFactory>();
        rotation.Resolve(Arg.Any<QueueRotationConfig>()).Returns(strategy);

        var cache = new JsonFileCacheService(NullLogger<JsonFileCacheService>.Instance, Substitute.For<IAnalyticsService>(), cacheDir);

        return new SingerQueueService(NullLogger<SingerQueueService>.Instance, cache, performanceService,
            usersRepository, performancesRepository, venues, Substitute.For<IAnalyticsService>(), rotation,
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    private sealed record QueryCounts(int Add, int Rotate, int Refresh, int Move);

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
