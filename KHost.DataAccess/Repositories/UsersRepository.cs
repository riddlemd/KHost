using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class UsersRepository : BaseRepository<KHostUser>, IUsersRepository
{
    public UsersRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {
    }

    public async Task<KHostUser?> FindByNameAsync(string name)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>().FirstOrDefaultAsync(u => u.Name == name);
    }

    public async Task<bool> HasAdminUserAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>()
            .AnyAsync(u => u.Groups.Any(g => g.IsAdmin));
    }

    public override async Task<KHostUser?> ReadAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>().Include(u => u.Groups.OrderBy(g => g.Name)).FirstOrDefaultAsync(u => u.Id == id);
    }

    protected override IQueryable<KHostUser> ApplySearchFilters<TOptions>(IQueryable<KHostUser> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        queryable = queryable.Include(u => u.Groups.OrderBy(g => g.Name));

        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        queryable = queryable.Where(u => u.Name.ToLower().Contains(query.ToLower()));

        return queryable.OrderBy(u => u.Name);
    }
}
