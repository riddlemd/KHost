using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

public class PluginCatalogService : BaseService, IPluginCatalogService
{
    public const string HttpClientName = "PluginCatalog";

    /// <summary>A catalog is a list of names and URLs. Anything this size is not one, and the
    /// response is read into memory, so it is refused rather than buffered.</summary>
    private const int MaxCatalogBytes = 2 * 1024 * 1024;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cache;
    private readonly IMessageBroker _broker;
    private readonly IOptionsMonitor<ServiceOptions> _options;
    private readonly TimeProvider _timeProvider;

    private PluginCatalogSnapshot? _current;
    private bool _cacheRead;

    public PluginCatalogService(
        ILogger<PluginCatalogService> logger,
        IHttpClientFactory httpClientFactory,
        ICacheService cache,
        IOptionsMonitor<ServiceOptions> options,
        TimeProvider timeProvider,
        IMessageBroker broker)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
        _broker = broker;
    }

    public PluginCatalogSnapshot? Current => _current;

    public string? LastError { get; private set; }

    public Task<PluginCatalogSnapshot?> GetAsync(CancellationToken cancellationToken = default)
        => LoadAsync(force: false, cancellationToken);

    public Task<PluginCatalogSnapshot?> RefreshAsync(CancellationToken cancellationToken = default)
        => LoadAsync(force: true, cancellationToken);

    private async Task<PluginCatalogSnapshot?> LoadAsync(bool force, CancellationToken cancellationToken)
    {
        var changed = false;

        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!_cacheRead)
            {
                _current = await _cache.LoadAsync<PluginCatalogSnapshot>(PluginCatalogSnapshot.CacheKey);
                _cacheRead = true;
                changed = _current is not null;
            }

            if (force || IsStale())
                changed |= await FetchAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }

        // Outside the lock: a handler that publishes back into this service would deadlock on it.
        if (changed)
            _broker.Announce(new PluginCatalogChanged());

        return _current;
    }

    private bool IsStale()
        => _current is null || _timeProvider.GetUtcNow().UtcDateTime - _current.FetchedUtc >= _options.CurrentValue.CacheLifetime;

    private async Task<bool> FetchAsync(CancellationToken cancellationToken)
    {
        var url = _options.CurrentValue.Url;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            LastError = "No plugin catalog URL is configured.";
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (_current?.ETag is { Length: > 0 } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
                request.Headers.IfNoneMatch.Add(tag);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified && _current is not null)
            {
                // Unchanged upstream still counts as checked, or every open re-requests it.
                _current = _current with { FetchedUtc = _timeProvider.GetUtcNow().UtcDateTime };
                LastError = null;

                await _cache.SaveAsync(PluginCatalogSnapshot.CacheKey, _current);

                return false;
            }

            response.EnsureSuccessStatusCode();

            var json = await ReadBoundedAsync(response, cancellationToken);
            var catalog = JsonSerializer.Deserialize<PluginCatalog>(json, JsonSerializerOptions.Web)
                ?? throw new JsonException("Catalog is empty.");

            if (catalog.SchemaVersion != PluginCatalog.SupportedSchemaVersion)
            {
                // The whole document, not the entries it could parse: a schema this host cannot
                // read might have moved the checksum or the API version.
                throw new InvalidOperationException(
                    $"Catalog is schema v{catalog.SchemaVersion}; this host reads v{PluginCatalog.SupportedSchemaVersion}.");
            }

            _current = new PluginCatalogSnapshot
            {
                Catalog = catalog,
                FetchedUtc = _timeProvider.GetUtcNow().UtcDateTime,
                ETag = response.Headers.ETag?.ToString(),
            };

            LastError = null;

            await _cache.SaveAsync(PluginCatalogSnapshot.CacheKey, _current);

            Logger.LogInformation("Plugin catalog fetched: {Count} plugins", catalog.Plugins.Count);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The cached catalog is kept on purpose — a bar's wifi should cost a stale list, not none.
            LastError = ex.Message;

            Logger.LogWarning(ex, "Could not fetch the plugin catalog from {Url}", uri);

            return false;
        }
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxCatalogBytes)
            throw new InvalidOperationException("Catalog is larger than this host will read.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        // Bounded by hand as well as by Content-Length: a chunked response declares no length.
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxCatalogBytes)
                throw new InvalidOperationException("Catalog is larger than this host will read.");

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public sealed class ServiceOptions
    {
        public const string SectionName = "PluginCatalog";

        /// <summary>Blank disables browsing entirely — the page then offers only manual installs.</summary>
        public string? Url { get; set; }

        public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromHours(6);
    }
}
