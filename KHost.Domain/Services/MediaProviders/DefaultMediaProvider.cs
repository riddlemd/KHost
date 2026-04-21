using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.MediaProviders;

public class DefaultMediaProvider : BaseService, IMediaProvider
{
    private readonly IPerformanceService _performanceService;
    private readonly IMediaRepository _repository;
    private readonly ISingerQueueService _singerQueueService;

    public DefaultMediaProvider(
        ILogger<DefaultMediaProvider> logger,
        IPerformanceService performanceService,
        ISingerQueueService singerQueueService,
        IMediaRepository repository)
        : base(logger)
    {
        _performanceService = performanceService;
        _singerQueueService = singerQueueService;
        _repository = repository;

        Actions = [
            new() {
                DisplayName = "Enqueue",
                PerformAsync = EnqueueAsync
            }
        ];
    }

    public string DisplayName => "Default Media Provider";

    public string SourceName => nameof(DefaultMediaProvider);

    public IEnumerable<MediaProviderAction> Actions { get; }

    public async Task EnqueueAsync(string foreignKey)
    {
        if (_performanceService is null)
        {
            Logger.LogWarning("Cannot enqueue: PerformanceService unavailable");
            return;
        }

        if (_singerQueueService?.SelectedSingerId is not { } singerId)
        {
            Logger.LogWarning("Cannot enqueue: no singer selected");
            return;
        }

        await _performanceService.CreateAndEnqueueAsync(new()
        {
            MediaId = new Guid(foreignKey),
            SingerId = singerId,
            CreatedDate = DateTime.Now
        });

        Logger.LogInformation("Enqueued media {MediaId} for singer {SingerId}", foreignKey, singerId);
    }

    public async Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
    {
        var result = await _repository.SearchAsync(query, pageNumber, pageSize);

        return [.. result.Items
            .Select(media => new MediaSearchEntity
            {
                DisplayName = $"{media.Artist} - {media.Title}",
                Source = SourceName,
                ForeignKey = media.Id.ToString(),
                SupportedActions = Actions
            })];
    }
}
