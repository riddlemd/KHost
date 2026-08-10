using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class TipsRepositoryTests : IDisposable
{
    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly TipsRepository _repository;

    public TipsRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-tips-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new TipsRepository(_factory, NullLogger<BaseRepository<Tip>>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_ReturnsZero_WhenUserHasNoTips()
    {
        var total = await _repository.GetTotalByUserIdAsync(UserA);

        Assert.Equal(0m, total);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_SumsOnlyTheGivenUser()
    {
        await SeedAsync(
            MakeTip(UserA, 10.00m, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 5.50m, new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserB, 99.00m, new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalByUserIdAsync(UserA);

        Assert.Equal(15.50m, total);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_IncludesTipsExactlyOnTheFromBoundary()
    {
        var boundary = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(MakeTip(UserA, 20m, boundary));

        var total = await _repository.GetTotalByUserIdAsync(UserA, from: boundary);

        Assert.Equal(20m, total);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_IncludesTipsExactlyOnTheToBoundary()
    {
        var boundary = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(MakeTip(UserA, 20m, boundary));

        var total = await _repository.GetTotalByUserIdAsync(UserA, to: boundary);

        Assert.Equal(20m, total);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_ExcludesTipsOutsideTheRange()
    {
        await SeedAsync(
            MakeTip(UserA, 1m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 10m, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 100m, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalByUserIdAsync(
            UserA,
            from: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(10m, total);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_ReturnsZero_WhenRangeExcludesEverything()
    {
        await SeedAsync(MakeTip(UserA, 10m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalByUserIdAsync(
            UserA,
            from: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0m, total);
    }

    [Fact]
    public async Task GetTotalByUserIdAsync_PreservesDecimalPrecision()
    {
        await SeedAsync(
            MakeTip(UserA, 0.10m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 0.20m, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalByUserIdAsync(UserA);

        Assert.Equal(0.30m, total);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOnlyThatUsersTips_NewestFirst()
    {
        await SeedAsync(
            MakeTip(UserA, 1m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 2m, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 3m, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserB, 99m, new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)));

        var tips = await _repository.GetByUserIdAsync(UserA);

        Assert.Equal([2m, 3m, 1m], tips.Select(t => t.Amount));
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsEmpty_WhenUserHasNoTips()
    {
        var tips = await _repository.GetByUserIdAsync(UserA);

        Assert.Empty(tips);
    }

    [Fact]
    public async Task SearchAsync_MatchesNotesSubstring()
    {
        await SeedAsync(
            MakeTip(UserA, 1m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "great set tonight"),
            MakeTip(UserA, 2m, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "encore please"));

        var result = await _repository.SearchAsync("encore");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(2m, result.Items[0].Amount);
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitiveForAsciiNotes()
    {
        await SeedAsync(MakeTip(UserA, 1m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Encore Please"));

        var result = await _repository.SearchAsync("ENCORE");

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_BlankQuery_ReturnsAllTips()
    {
        await SeedAsync(
            MakeTip(UserA, 1m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserB, 2m, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        var result = await _repository.SearchAsync("");

        Assert.Equal(2, result.TotalCount);
    }

    // Tip.UserId is a real foreign key, so the owning users must exist before any tip is saved.
    private async Task SeedAsync(params Tip[] tips)
    {
        using var context = await _factory.CreateDbContextAsync();

        var existingUserIds = context.Users.Select(u => u.Id).ToHashSet();
        var missingUsers = tips
            .Select(t => t.UserId)
            .Distinct()
            .Where(id => !existingUserIds.Contains(id))
            .Select(id => new KHostUser { Id = id, Name = $"Singer {id:N}" });

        context.Users.AddRange(missingUsers);
        context.Tips.AddRange(tips);
        await context.SaveChangesAsync();
    }

    private static Tip MakeTip(Guid userId, decimal amount, DateTime createdDate, string notes = "") => new()
    {
        UserId = userId,
        Amount = amount,
        CreatedDate = createdDate,
        PaymentMethod = TipPaymentMethod.Cash,
        Notes = notes,
    };
}
