using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal class UsersRepository : BaseRepository<KHostUser>, IUsersRepository
{
    // Lowercased because SQLite orders with binary collation: without it every lowercase name
    // sorts below every uppercase one, and a singer called "mike" lands under the Vs.
    private static readonly IReadOnlyDictionary<string, Expression<Func<KHostUser, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<KHostUser, object>>>
        {
            ["name"] = u => u.Name.ToLower(),
            ["createdDate"] = u => u.CreatedDate,
        };

    public UsersRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<KHostUser>> logger)
        : base(contextFactory, logger)
    {
    }

    public async Task<KHostUser?> FindByNameAsync(string name)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        // In .NET, not SQL: the bundled SQLite folds ASCII only, so "SÖNG" and "söng" would
        // pass a NOCASE comparison as different names. The table is a roster, not a library.
        await foreach (var user in context.Set<KHostUser>().AsAsyncEnumerable())
        {
            if (string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase))
                return user;
        }

        return null;
    }

    public async Task<bool> HasAdminUserAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>()
            .AnyAsync(u => u.Groups.Any(g => g.IsAdmin));
    }

    public async Task<bool> HasAdminWithPasswordAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>()
            .AnyAsync(u => u.PasswordHash != null && u.PasswordHash != "" && u.Groups.Any(g => g.IsAdmin));
    }

    public override async Task<KHostUser?> ReadAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>().Include(u => u.Groups.OrderBy(g => g.Name)).FirstOrDefaultAsync(u => u.Id == id);
    }

    protected override IReadOnlyDictionary<string, Expression<Func<KHostUser, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<KHostUser, object>> DefaultSortExpression => u => u.Name.ToLower();

    protected override Func<KHostUser, IEnumerable<string?>>? SearchableTextFields => user => [user.Name];

    protected override IQueryable<KHostUser> ApplySearchFilters<TOptions>(IQueryable<KHostUser> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        return queryable.Include(u => u.Groups.OrderBy(g => g.Name));
    }
}
