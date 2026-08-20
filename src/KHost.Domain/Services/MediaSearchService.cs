using KHost.Abstractions.Models;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaSearchService : BaseService, IMediaSearchService
{
    private readonly List<IMediaProvider> _providers;
    private readonly IAnalyticsService _analytics;

    public MediaSearchService(ILogger<MediaSearchService> logger, IEnumerable<IMediaProvider> providers, IAnalyticsService analytics)
        : base(logger)
    {
        _providers = providers.ToList();
        _analytics = analytics;
    }

    public IReadOnlyList<IMediaProvider> Providers => _providers;

    public Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
        => SearchProvidersAsync(_providers, query, source: null, pageNumber, pageSize);

    public Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0)
    {
        var matching = _providers.Where(p => p.SourceName == source).ToList();

        if (matching.Count == 0)
            Logger.LogWarning("No provider registered for source '{Source}'", source);

        return SearchProvidersAsync(matching, query, source, pageNumber, pageSize);
    }

    private async Task<List<MediaSearchEntity>> SearchProvidersAsync(
        List<IMediaProvider> providers, string query, string? source, int pageNumber, int pageSize)
    {
        using var activity = _analytics.StartActivity(AnalyticActivities.Search);
        activity.SetTag("query", query);
        activity.SetTag("source", source ?? "all");

        Logger.LogDebug("Searching {ProviderCount} providers for '{Query}'", providers.Count, query);

        var tasks = providers.Select(async p =>
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
