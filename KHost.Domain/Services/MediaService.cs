using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaService : BaseRepositoryService<Media, IMediaRepository>, IMediaService
{
    public MediaService(ILogger<MediaService> logger, IMediaRepository repository)
        : base(logger, repository)
    {
    }
}
