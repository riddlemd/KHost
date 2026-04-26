using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class UsersService : BaseRepositoryService<KHostUser, IUsersRepository>, IUsersService
{
    public IOptionsMonitor<ServiceOptions> Options { get; }

    public UsersService(ILogger<UsersService> logger, IOptionsMonitor<ServiceOptions> options, IUsersRepository repository)
        : base(logger, repository)
    {
        Options = options;
    }

    public async Task ToggleIsRegularAsync(Guid userId)
    {
        var user = await ReadAsync(userId);
        if (user is null) return;
        user.IsRegular = !user.IsRegular;
        await UpdateAsync(user);
        Logger.LogInformation("User {UserId} IsRegular toggled to {Value}", userId, user.IsRegular);
    }

    public async Task ToggleIsTipperAsync(Guid userId)
    {
        var user = await ReadAsync(userId);
        if (user is null) return;
        user.IsTipper = !user.IsTipper;
        await UpdateAsync(user);
        Logger.LogInformation("User {UserId} IsTipper toggled to {Value}", userId, user.IsTipper);
    }

    public async Task<bool> HasAdminUserAsync()
    {
        return await Repository.HasAdminUserAsync();
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(UsersService);
    }
}
