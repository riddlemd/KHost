using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
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
    public async Task GetTotalInCentsByUserIdAsync_ReturnsZero_WhenUserHasNoTips()
    {
        var total = await _repository.GetTotalInCentsByUserIdAsync(UserA);

        Assert.Equal(0, total);
    }

    [Fact]
    public async Task GetTotalInCentsByUserIdAsync_SumsOnlyTheGivenUser()
    {
        await SeedAsync(
            MakeTip(UserA, 1000, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 550, new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserB, 9900, new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalInCentsByUserIdAsync(UserA);

        Assert.Equal(1550, total);
    }

    [Fact]
    public async Task GetTotalInCentsByUserIdAsync_IncludesTipsExactlyOnTheFromBoundary()
    {
        var boundary = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(MakeTip(UserA, 2000, boundary));

        var total = await _repository.GetTotalInCentsByUserIdAsync(UserA, from: boundary);

        Assert.Equal(2000, total);
    }

    [Fact]
    public async Task GetTotalInCentsByUserIdAsync_IncludesTipsExactlyOnTheToBoundary()
    {
        var boundary = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(MakeTip(UserA, 2000, boundary));

        var total = await _repository.GetTotalInCentsByUserIdAsync(UserA, to: boundary);

        Assert.Equal(2000, total);
    }

    [Fact]
    public async Task GetTotalInCentsByUserIdAsync_ExcludesTipsOutsideTheRange()
    {
        await SeedAsync(
            MakeTip(UserA, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 1000, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 10000, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalInCentsByUserIdAsync(
            UserA,
            from: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1000, total);
    }

    [Fact]
    public async Task GetTotalInCentsByUserIdAsync_ReturnsZero_WhenRangeExcludesEverything()
    {
        await SeedAsync(MakeTip(UserA, 1000, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var total = await _repository.GetTotalInCentsByUserIdAsync(
            UserA,
            from: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, total);
    }

    [Fact]
    public async Task GetTotalInCentsByUserIdAsync_AddsUpExactly()
    {
        await SeedAsync(
            MakeTip(UserA, 10, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 20, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        // Whole cents, so the total is integer arithmetic end to end - no float in the middle.
        var total = await _repository.GetTotalInCentsByUserIdAsync(UserA);

        Assert.Equal(30, total);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOnlyThatUsersTips_NewestFirst()
    {
        await SeedAsync(
            MakeTip(UserA, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 200, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserA, 300, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserB, 9900, new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)));

        var tips = await _repository.GetByUserIdAsync(UserA);

        Assert.Equal([200, 300, 100], tips.Select(t => t.AmountInCents));
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
            MakeTip(UserA, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "great set tonight"),
            MakeTip(UserA, 200, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "encore please"));

        var result = await _repository.SearchAsync("encore");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(200, result.Items[0].AmountInCents);
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitiveForAsciiNotes()
    {
        await SeedAsync(MakeTip(UserA, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Encore Please"));

        var result = await _repository.SearchAsync("ENCORE");

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_BlankQuery_ReturnsAllTips()
    {
        await SeedAsync(
            MakeTip(UserA, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeTip(UserB, 200, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

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

    private static Tip MakeTip(Guid userId, int amountInCents, DateTime createdDate, string notes = "") => new()
    {
        UserId = userId,
        AmountInCents = amountInCents,
        CreatedDate = createdDate,
        PaymentMethod = TipPaymentMethod.Cash,
        Notes = notes,
    };
}
