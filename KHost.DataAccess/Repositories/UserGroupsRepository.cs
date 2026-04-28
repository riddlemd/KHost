using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class UserGroupsRepository : BaseRepository<KHostUserGroup>, IUserGroupsRepository
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<KHostUserGroup, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<KHostUserGroup, object>>>
        {
            ["name"] = g => g.Name,
            ["isAdmin"] = g => g.IsAdmin,
        };

    public UserGroupsRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {
    }

    public async Task AddUserToGroupAsync(Guid userId, Guid groupId)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        var exists = await context.Set<UserGroupMembership>()
            .AnyAsync(m => m.UserId == userId && m.GroupId == groupId);
        if (exists) return;

        context.Set<UserGroupMembership>().Add(new UserGroupMembership { UserId = userId, GroupId = groupId });
        await context.SaveChangesAsync();
    }

    public async Task RemoveUserFromGroupAsync(Guid userId, Guid groupId)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        var membership = new UserGroupMembership { UserId = userId, GroupId = groupId };
        context.Set<UserGroupMembership>().Remove(membership);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsUserInGroupAsync(Guid userId, Guid groupId)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>()
            .AnyAsync(u => u.Id == userId && u.Groups.Any(g => g.Id == groupId));
    }

    public async Task<IReadOnlyList<KHostUser>> GetAllUsersInGroupAsync(Guid groupId)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<KHostUser>()
            .Where(u => u.Groups.Any(g => g.Id == groupId))
            .Include(u => u.Groups.OrderBy(g => g.Name))
            .ToListAsync();
    }

    protected override IReadOnlyDictionary<string, Expression<Func<KHostUserGroup, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<KHostUserGroup, object>> DefaultSortExpression => g => g.Name;

    protected override IQueryable<KHostUserGroup> ApplySearchFilters<TOptions>(IQueryable<KHostUserGroup> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(g => g.Name.ToLower().Contains(query.ToLower()));
    }
}
