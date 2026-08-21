using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.DataAccess.Migrations;

/// <summary>
/// The StoreTipAmountInCents migration moves recorded money between column types. Scaffolding it
/// would have dropped and re-added the column, losing every amount, so the conversion is
/// hand-written — and worth proving against a database that actually holds the old values.
/// </summary>
public class TipAmountConversionTests : IDisposable
{
    private const string BeforeConversion = "IndexFoldedSearchText";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-tips-{Guid.NewGuid():N}.db");
    private readonly IDbContextFactory<DefaultContext> _factory;

    public TipAmountConversionTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();
    }

    [Theory]
    // The awkward ones: values whose cents do not survive a naive float multiply-and-truncate.
    [InlineData("0.29", 29)]
    [InlineData("0.01", 1)]
    [InlineData("0.10", 10)]
    [InlineData("3.33", 333)]
    [InlineData("12.50", 1250)]
    [InlineData("99999.99", 9999999)]
    [InlineData("0.00", 0)]
    public void Migration_ConvertsADecimalAmountToExactCents(string stored, int expectedCents)
    {
        MigrateTo(BeforeConversion);
        SeedTip(stored);

        MigrateToLatest();

        Assert.Equal(expectedCents, SingleTipAmountInCents());
    }

    [Fact]
    public void Migration_ConvertsEveryRow_NotJustTheFirst()
    {
        MigrateTo(BeforeConversion);
        foreach (var amount in new[] { "0.29", "12.50", "99999.99" })
            SeedTip(amount);

        MigrateToLatest();

        using var context = _factory.CreateDbContext();
        Assert.Equal([29, 1250, 9999999], context.Tips.Select(t => t.AmountInCents).OrderBy(cents => cents));
    }

    [Fact]
    public void Migration_LeavesAnEmptyTableAlone()
    {
        MigrateTo(BeforeConversion);

        MigrateToLatest();

        using var context = _factory.CreateDbContext();
        Assert.Empty(context.Tips);
    }

    private void MigrateTo(string migration)
    {
        using var context = _factory.CreateDbContext();
        context.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(migration);
    }

    private void MigrateToLatest()
    {
        using var context = _factory.CreateDbContext();
        context.Database.Migrate();
    }

    // Raw SQL, because the model no longer has the column this is seeding.
    private void SeedTip(string amount)
    {
        using var context = _factory.CreateDbContext();
        var userId = Guid.NewGuid().ToString().ToUpperInvariant();
        context.Database.ExecuteSqlRaw(
            "INSERT INTO Users (Id, Name, NameFolded, Notes, CreatedDate) VALUES ({0}, {1}, {2}, '', {3})",
            userId, $"Singer {userId[..8]}", $"singer {userId[..8]}".ToLowerInvariant(), DateTime.UtcNow);
        context.Database.ExecuteSqlRaw(
            "INSERT INTO Tips (Id, CreatedDate, UserId, Amount, PaymentMethod, Notes, NotesFolded) " +
            "VALUES ({0}, {1}, {2}, {3}, 0, '', '')",
            Guid.NewGuid().ToString().ToUpperInvariant(), DateTime.UtcNow, userId, amount);
    }

    private int SingleTipAmountInCents()
    {
        using var context = _factory.CreateDbContext();
        return context.Tips.Single().AmountInCents;
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
}
