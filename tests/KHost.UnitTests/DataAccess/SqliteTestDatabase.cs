using KHost.DataAccess.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KHost.UnitTests.DataAccess;

/// <summary>A substitute tests the expression tree, not the query; case and paging show here only.</summary>
/// <remarks>The connection stays open; an in-memory database lives only as long as its connection.</remarks>
internal sealed class SqliteTestDatabase : IDbContextFactory<DefaultContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DefaultContext> _options;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DefaultContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public DefaultContext CreateDbContext() => new(_options);

    public async Task SeedAsync(params object[] entities)
    {
        using var context = CreateDbContext();
        context.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public void Dispose() => _connection.Dispose();
}
