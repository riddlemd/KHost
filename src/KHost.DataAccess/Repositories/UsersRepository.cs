using System.Linq.Expressions;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.Data.Sqlite;
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

    /// <summary>SQLITE_CONSTRAINT — every constraint, so the column has to be checked as well.</summary>
    private const int SqliteConstraintViolation = 19;

    private const string FoldedNameIndex = "Users.NameFolded";

    private const string ForeignKeyIndex = "UserForeignKeys.Source, UserForeignKeys.Key";

    // Translated here rather than in the service: the constraint is a fact of this layer, and an
    // untranslated DbUpdateException reaches Blazor as an unhandled exception and kills the circuit.
    public override async Task<KHostUser> CreateAsync(KHostUser entity)
    {
        try
        {
            return await base.CreateAsync(entity);
        }
        catch (DbUpdateException ex) when (IsNameTaken(ex))
        {
            throw NameTaken(entity.Name, ex);
        }
    }

    public override async Task UpdateAsync(KHostUser entity)
    {
        try
        {
            await base.UpdateAsync(entity);
        }
        catch (DbUpdateException ex) when (IsNameTaken(ex))
        {
            throw NameTaken(entity.Name, ex);
        }
    }

    private static bool IsNameTaken(DbUpdateException ex)
        => ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintViolation } sqlite
           && sqlite.Message.Contains(FoldedNameIndex, StringComparison.Ordinal);

    // Names are matched folded, so "Mike" and "mike" collide and the message has to explain why a
    // name that looks free is not.
    private static KHostException NameTaken(string name, Exception inner)
        => new($"There is already a singer called “{name}”.",
               "Names ignore case and accents, so pick one that differs by more than those.",
               "KH-USER-NAME-TAKEN",
               inner);

    public async Task<KHostUser?> FindByNameAsync(string name)
    {
        var folded = EntityFolding.Fold(name);

        using var context = await ContextFactory.CreateDbContextAsync();

        // Exact spelling first: NameFolded is unique so the folded lookup can only ever return one
        // row, but a pre-upgrade database may hold two names the fold now considers equal (Andre
        // and Ándre), of which only one could be refolded. Typing a name exactly as stored must
        // always reach that account, or the losing side of the collision cannot sign in at all.
        return await context.Set<KHostUser>().FirstOrDefaultAsync(u => u.Name == name)
            ?? await context.Set<KHostUser>().FirstOrDefaultAsync(u => u.NameFolded == folded);
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
        return await context.Set<KHostUser>()
            .Include(u => u.Groups.OrderBy(g => g.Name))
            .Include(u => u.ForeignKeys)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<KHostUser?> ReadByForeignKeyAsync(string source, string key)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        // Matched exactly. An external id is not a name, and a provider is free to make its case
        // meaningful — folding one would merge two singers who are genuinely different people.
        var owner = await context.Set<KHostUserForeignKey>()
            .Where(k => k.Source == source && k.Key == key)
            .Select(k => k.UserId)
            .FirstOrDefaultAsync();

        return owner == Guid.Empty ? null : await ReadAsync(owner);
    }

    public async Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        context.Add(new KHostUserForeignKey
        {
            UserId = userId,
            Source = source,
            Key = key,
            IsEphemeral = isEphemeral,
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyTaken(ex))
        {
            throw ForeignKeyTaken(source, key, ex);
        }
    }

    public async Task RemoveForeignKeyAsync(Guid userId, string source, string key)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        await context.Set<KHostUserForeignKey>()
            .Where(k => k.UserId == userId && k.Source == source && k.Key == key)
            .ExecuteDeleteAsync();
    }

    public async Task<int> DeleteEphemeralForeignKeysAsync(string source)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.Set<KHostUserForeignKey>()
            .Where(k => k.IsEphemeral && k.Source == source)
            .ExecuteDeleteAsync();
    }

    private static bool IsForeignKeyTaken(DbUpdateException ex)
        => ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintViolation } sqlite
           && sqlite.Message.Contains(ForeignKeyIndex, StringComparison.Ordinal);

    // Translated here for the same reason the taken name is: a raw DbUpdateException reaching
    // Blazor kills the circuit, and this one is reachable from a plugin acting on a stale lookup.
    private static KHostException ForeignKeyTaken(string source, string key, Exception inner)
        => new($"Another singer is already registered as {source} “{key}”.",
               "Remove it from them before giving it to this singer.",
               "KH-USER-FOREIGN-KEY-TAKEN",
               inner);

    protected override IReadOnlyDictionary<string, Expression<Func<KHostUser, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<KHostUser, object>> DefaultSortExpression => u => u.Name.ToLower();

    protected override IQueryable<KHostUser> ApplySearchFilters<TOptions>(IQueryable<KHostUser> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        // Both collections, because an entity that comes back short of one is then saved back
        // short of it — UpdateAsync reconciles against what it is handed.
        queryable = queryable
            .Include(u => u.Groups.OrderBy(g => g.Name))
            .Include(u => u.ForeignKeys);

        if (options is UserSearchOptions { SingersOnly: true })
            queryable = queryable.Where(u => !u.Groups.Any(g => g.ExcludeFromSingerQueue));

        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(u => EF.Functions.Like(u.NameFolded, FoldedContainsPattern(query), "\\"));
    }
}
