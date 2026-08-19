using KHost.DataAccess.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KHost.UnitTests.DataAccess;

/// <summary>
/// A real SQLite database held in memory. Repositories are mostly LINQ that only becomes SQL at the
/// provider, so a substitute would test the expression tree rather than the query — the case
/// sensitivity of a search, or whether a total count is taken before paging, only shows up here.
///
/// The connection stays open because an in-memory database lives exactly as long as its connection.
/// The schema comes from the model rather than the migrations, which carry raw FTS5 SQL these
/// tests do not need.
/// </summary>
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
