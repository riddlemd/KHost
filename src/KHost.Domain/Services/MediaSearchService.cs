using KHost.Abstractions.Models;
using KHost.Domain.Services.MediaProviders;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaSearchService : BaseService, IMediaSearchService
{
    /// <summary>The library on this machine, which is what a plain search means.</summary>
    internal static readonly string LocalSourceName = nameof(LocalMediaProvider);

    private readonly List<IMediaProvider> _providers;
    private readonly IAnalyticsService _analytics;
    private readonly IFlashService _flash;

    public MediaSearchService(
        ILogger<MediaSearchService> logger, IEnumerable<IMediaProvider> providers, IAnalyticsService analytics, IFlashService flash)
        : base(logger)
    {
        _providers = providers.ToList();
        _analytics = analytics;
        _flash = flash;
    }

    public IReadOnlyList<IMediaProvider> Providers => _providers;

    public Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
    {
        if (_providers.FirstOrDefault(p => p.SourceName == LocalSourceName) is not { } local)
        {
            Logger.LogWarning("No local media provider is registered, so the default search has nothing to read");
            return Task.FromResult(new List<MediaSearchEntity>());
        }

        return SearchProvidersAsync([local], query, local.SourceName, pageNumber, pageSize);
    }

    public Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0)
    {
        var matching = _providers.Where(p => p.SourceName == source).ToList();

        if (matching.Count == 0)
            Logger.LogWarning("No provider registered for source '{Source}'", source);

        return SearchProvidersAsync(matching, query, source, pageNumber, pageSize);
    }

    /// <summary>Still plural: two providers may share a SourceName and both answer.</summary>
    private async Task<List<MediaSearchEntity>> SearchProvidersAsync(
        List<IMediaProvider> providers, string query, string source, int pageNumber, int pageSize)
    {
        using var activity = _analytics.StartActivity(AnalyticActivities.Search);
        activity.SetTag("query", query);
        activity.SetTag("source", source);

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

                // Without this an offline provider reads as "no songs match". A provider cancelling its
                // own superseded search is no failure, but HttpClient reports a timeout as a cancel too.
                if (ex is not OperationCanceledException { InnerException: not TimeoutException })
                    _flash.Show(NetworkFailureText.Describe(p.DisplayName, ex), FlashType.Warning);

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
