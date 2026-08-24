using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.MediaProviders;

public class LocalMediaProvider : BaseService, IMediaProvider
{
    // Broken songs stay in the library so the host can fix or re-import them, but must not be
    // offered for queueing. Derived from the enum so a new status shows up unless it opts out.
    private static readonly MediaSearchOptions _searchOptions = new()
    {
        Type = MediaType.Karaoke,
        Statuses = [.. Enum.GetValues<MediaStatus>().Where(status => status != MediaStatus.Broken)],
    };

    private readonly IPerformanceService _performanceService;
    private readonly IMediaRepository _repository;
    private readonly ISingerQueueService _singerQueueService;
    private readonly IInteractionDispatcher _interactions;
    private readonly IMediaService _mediaService;

    public LocalMediaProvider(
        ILogger<LocalMediaProvider> logger,
        IPerformanceService performanceService,
        ISingerQueueService singerQueueService,
        IMediaRepository repository,
        IInteractionDispatcher interactions,
        IMediaService mediaService)
        : base(logger)
    {
        _performanceService = performanceService;
        _singerQueueService = singerQueueService;
        _repository = repository;
        _interactions = interactions;
        _mediaService = mediaService;

        Actions = [
            new() {
                DisplayName = "Enqueue",
                Description = "Enqueue Song",
                Icon = "plus-lg",
                PerformAsync = EnqueueAsync,
                SubActions = [
                    new() {
                        DisplayName = "Edit",
                        Description = "Edit Song",
                        Icon = "pencil-fill",
                        PerformAsync = EditAsync
                    },
                    new() {
                        DisplayName = "Lyrics",
                        Description = "View Song's Lyrics",
                        Icon = "file-text",
                        PerformAsync = GetLyricsAsync
                    }
                ]
            }
        ];
    }

    public string DisplayName => "Local";

    public string SourceName => nameof(LocalMediaProvider);

    public IEnumerable<MediaProviderAction> Actions { get; }

    public async Task EnqueueAsync(MediaSearchEntity mediaSearchEntity)
    {
        if (_performanceService is null)
        {
            Logger.LogWarning("Cannot enqueue: PerformanceService unavailable");
            return;
        }

        if (_singerQueueService?.SelectedUserId is not { } singerId)
        {
            Logger.LogWarning("Cannot enqueue: no singer selected");
            return;
        }

        await _performanceService.CreateAndEnqueueAsync(new()
        {
            MediaId = new Guid(mediaSearchEntity.ForeignKey),
            SingerId = singerId,
            CreatedDate = DateTime.UtcNow
        });

        Logger.LogInformation("Enqueued media {MediaId} for singer {SingerId}", mediaSearchEntity.ForeignKey, singerId);
    }

    public async Task EditAsync(MediaSearchEntity mediaSearchEntity)
    {
        var media = await _repository.ReadAsync(new Guid(mediaSearchEntity.ForeignKey));

        if (media is null) return;

        var updated = await _interactions.RequestAsync(new EditMediaRequest(media));

        if (updated is null) return;

        await _mediaService.UpdateAsync(updated);
    }

    public async Task GetLyricsAsync(MediaSearchEntity mediaSearchEntity)
    {
        var media = await _repository.ReadAsync(new Guid(mediaSearchEntity.ForeignKey));

        if (media is null) return;

        await _interactions.RequestAsync(new ShowLyricsRequest($"{media.Artist} {media.Title}"));
    }

    public async Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
    {
        var result = await _repository.SearchAsync(query, pageNumber, pageSize, _searchOptions);

        return [.. result.Items
            .Select(media => new MediaSearchEntity
            {
                Title = media.Title,
                Artist = media.Artist,
                SourceDisplayName = DisplayName,
                Source = SourceName,
                Duration = media.Duration,
                ForeignKey = media.Id.ToString(),
                SupportedActions = Actions
            })];
    }
}
