using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaSearchService : BaseService, IMediaSearchService
{
    private readonly IEnumerable<IMediaProvider> _providers;

    public MediaSearchService(ILogger<MediaSearchService> logger, IEnumerable<IMediaProvider> providers)
        : base(logger)
    {
        _providers = providers;
    }

    public async Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
    {
        var providerList = _providers.ToList();

        Logger.LogDebug("Searching {ProviderCount} providers for '{Query}'", providerList.Count, query);

        var tasks = providerList.Select(async p =>
        {
            try
            {
                return await p.SearchAsync(query, pageNumber, pageSize);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Provider '{Provider}' failed for query '{Query}'", p.DisplayName, query);
                return [];
            }
        });

        var results = await Task.WhenAll(tasks);

        return [.. results.SelectMany(r => r)];
    }
}
