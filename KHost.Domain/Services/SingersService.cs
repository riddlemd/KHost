using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class SingersService : BaseRepositoryService<Singer, ISingersRepository>, ISingersService
{
    public IOptionsMonitor<ServiceOptions> Options { get; }

    public SingersService(ILogger<SingersService> logger, IOptionsMonitor<ServiceOptions> options, ISingersRepository repository)
        : base(logger, repository)
    {
        Options = options;
    }

    public async Task ToggleIsRegularAsync(Guid singerId)
    {
        var singer = await ReadAsync(singerId);
        if (singer is null) return;
        singer.IsRegular = !singer.IsRegular;
        await UpdateAsync(singer);
        Logger.LogInformation("Singer {SingerId} IsRegular toggled to {Value}", singerId, singer.IsRegular);
    }

    public async Task ToggleIsTipperAsync(Guid singerId)
    {
        var singer = await ReadAsync(singerId);
        if (singer is null) return;
        singer.IsTipper = !singer.IsTipper;
        await UpdateAsync(singer);
        Logger.LogInformation("Singer {SingerId} IsTipper toggled to {Value}", singerId, singer.IsTipper);
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(SingersService);
    }
}
