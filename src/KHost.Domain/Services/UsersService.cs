using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KHost.Common.Repositories;

namespace KHost.Domain.Services;

public class UsersService : BaseRepositoryService<KHostUser, IUsersRepository>, IUsersService
{
    private readonly IUserGroupsRepository _userGroupsRepository;
    private readonly IMessageBroker _broker;

    public UsersService(
        ILogger<UsersService> logger,
        IUsersRepository repository,
        IUserGroupsRepository userGroupsRepository,
        IMessageBroker broker)
        : base(logger, repository, broker, new UsersChanged())
    {
        _broker = broker;
        _userGroupsRepository = userGroupsRepository;
    }

    // Groups is detached first: BaseRepository's Add/Update cascade the graph and would rewrite
    // the group rows. Repository directly, not base — base notifies before membership lands.
    public override async Task<KHostUser> CreateAsync(KHostUser entity)
    {
        var groups = entity.Groups.ToArray();
        var foreignKeys = entity.ForeignKeys.ToArray();

        entity.Groups = [];
        entity.ForeignKeys = [];
        var saved = await Repository.CreateAsync(entity);

        foreach (var group in groups)
            await _userGroupsRepository.AddUserToGroupAsync(saved.Id, group.Id);

        foreach (var key in foreignKeys)
            await Repository.AddForeignKeyAsync(saved.Id, key.Source, key.Key, key.IsEphemeral);

        saved.Groups = groups;
        saved.ForeignKeys = foreignKeys;

        _broker.Announce(new UsersChanged());
        return saved;
    }

    public override async Task UpdateAsync(KHostUser entity)
    {
        var groups = entity.Groups.ToArray();
        var foreignKeys = entity.ForeignKeys.ToArray();

        var stored = await Repository.ReadAsync(entity.Id);
        var desiredGroupIds = groups.Select(g => g.Id).ToHashSet();
        var currentGroupIds = stored?.Groups.Select(g => g.Id).ToHashSet() ?? [];
        var desiredKeys = foreignKeys.Select(Pair).ToHashSet();
        var currentKeys = stored?.ForeignKeys.Select(Pair).ToHashSet() ?? [];

        entity.Groups = [];
        entity.ForeignKeys = [];
        await Repository.UpdateAsync(entity);
        entity.Groups = groups;
        entity.ForeignKeys = foreignKeys;

        foreach (var groupId in desiredGroupIds.Except(currentGroupIds))
            await _userGroupsRepository.AddUserToGroupAsync(entity.Id, groupId);

        foreach (var groupId in currentGroupIds.Except(desiredGroupIds))
            await _userGroupsRepository.RemoveUserFromGroupAsync(entity.Id, groupId);

        // Diffed on the pair rather than the row id: a caller building a key by hand has no id to
        // give it, so comparing ids would delete and re-add every key on every save.
        foreach (var (source, key) in desiredKeys.Except(currentKeys))
            await Repository.AddForeignKeyAsync(entity.Id, source, key,
                foreignKeys.First(k => Pair(k) == (source, key)).IsEphemeral);

        foreach (var (source, key) in currentKeys.Except(desiredKeys))
            await Repository.RemoveForeignKeyAsync(entity.Id, source, key);

        _broker.Announce(new UsersChanged());
    }

    private static (string Source, string Key) Pair(KHostUserForeignKey foreignKey)
        => (foreignKey.Source, foreignKey.Key);

    public override async Task<bool> DeleteAsync(Guid id)
    {
        if (RepositoryModels.IsBuiltIn(id))
        {
            Logger.LogWarning("Refused to delete built-in user {UserId}", id);
            return false;
        }

        return await base.DeleteAsync(id);
    }

    public async Task<bool> HasAdminWithPasswordAsync()
        => await Repository.HasAdminWithPasswordAsync();

    public async Task<bool> HasAdminUserAsync()
    {
        return await Repository.HasAdminUserAsync();
    }

    public async Task<KHostUser?> FindByNameAsync(string name)
        => await Repository.FindByNameAsync(name);

    public async Task<KHostUser?> ReadByForeignKeyAsync(string source, string key)
        => await Repository.ReadByForeignKeyAsync(source, key);

    public async Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral = false)
    {
        await Repository.AddForeignKeyAsync(userId, source, key, isEphemeral);

        _broker.Announce(new UsersChanged());
    }

    public async Task RemoveForeignKeyAsync(Guid userId, string source, string key)
    {
        await Repository.RemoveForeignKeyAsync(userId, source, key);

        _broker.Announce(new UsersChanged());
    }

    public async Task<int> DeleteEphemeralForeignKeysAsync(string source)
    {
        var dropped = await Repository.DeleteEphemeralForeignKeysAsync(source);

        // Only when something moved: this runs on every reconnect, and a quiet night would
        // otherwise redraw every singer list for nothing.
        if (dropped > 0)
            _broker.Announce(new UsersChanged());

        return dropped;
    }

}
