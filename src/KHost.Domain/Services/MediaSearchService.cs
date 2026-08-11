using KHost.Abstractions.Models;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaSearchService : BaseService, IMediaSearchService
{
    private readonly IEnumerable<IMediaProvider> _providers;
    private readonly IAnalyticsService _analytics;

    public MediaSearchService(ILogger<MediaSearchService> logger, IEnumerable<IMediaProvider> providers, IAnalyticsService analytics)
        : base(logger)
    {
        _providers = providers;
        _analytics = analytics;
    }

    public async Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
    {
        using var activity = _analytics.StartActivity(AnalyticActivities.Search);
        activity.SetTag("query", query);

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
        var flatResults = results.SelectMany(r => r).ToList();

        activity.SetTag("result_count", flatResults.Count);

        return flatResults;
    }

    public string GetMediaProviderDisplayName(string source)
        => _providers.FirstOrDefault(x => x.SourceName == source)?.DisplayName ?? "Unknown Source";

    private static class AnalyticActivities
    {
        public const string Search = "media.search";
    }
}
