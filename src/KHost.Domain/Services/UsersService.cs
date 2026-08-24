using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        entity.Groups = [];
        var saved = await Repository.CreateAsync(entity);

        foreach (var group in groups)
            await _userGroupsRepository.AddUserToGroupAsync(saved.Id, group.Id);

        saved.Groups = groups;

        _broker.Announce(new UsersChanged());
        return saved;
    }

    public override async Task UpdateAsync(KHostUser entity)
    {
        var groups = entity.Groups.ToArray();
        var desiredGroupIds = groups.Select(g => g.Id).ToHashSet();
        var currentGroupIds = (await Repository.ReadAsync(entity.Id))?.Groups.Select(g => g.Id).ToHashSet() ?? [];

        entity.Groups = [];
        await Repository.UpdateAsync(entity);
        entity.Groups = groups;

        foreach (var groupId in desiredGroupIds.Except(currentGroupIds))
            await _userGroupsRepository.AddUserToGroupAsync(entity.Id, groupId);

        foreach (var groupId in currentGroupIds.Except(desiredGroupIds))
            await _userGroupsRepository.RemoveUserFromGroupAsync(entity.Id, groupId);

        _broker.Announce(new UsersChanged());
    }

    public override async Task<bool> DeleteAsync(Guid id)
    {
        if (KHostUser.IsBuiltIn(id))
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

}
