using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KHost.LrcLib.Models;
using Microsoft.Extensions.Logging;

namespace KHost.LrcLib;

public class LrcLibClient : ILrcLibClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LrcLibClient> _logger;

    public LrcLibClient(HttpClient httpClient, ILogger<LrcLibClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<LyricsRecord?> GetAsync(GetLyricsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.TrackName) || string.IsNullOrWhiteSpace(request.ArtistName))
            throw new ArgumentException("track_name and artist_name are required.", nameof(request));

        var url = BuildGetUrl("api/get", request);

        return GetRecordAsync(url, cancellationToken);
    }

    public Task<LyricsRecord?> GetCachedAsync(GetLyricsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.TrackName) || string.IsNullOrWhiteSpace(request.ArtistName))
            throw new ArgumentException("track_name and artist_name are required.", nameof(request));

        var url = BuildGetUrl("api/get-cached", request);

        return GetRecordAsync(url, cancellationToken);
    }

    public Task<LyricsRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var url = $"api/get/{id.ToString(CultureInfo.InvariantCulture)}";

        return GetRecordAsync(url, cancellationToken);
    }

    public async Task<IReadOnlyList<LyricsRecord>> SearchAsync(SearchLyricsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query) && string.IsNullOrWhiteSpace(request.TrackName))
            throw new ArgumentException("Either Query (q) or TrackName is required.", nameof(request));

        var builder = new QueryStringBuilder("api/search");
        builder.Add("q", request.Query);
        builder.Add("track_name", request.TrackName);
        builder.Add("artist_name", request.ArtistName);
        builder.Add("album_name", request.AlbumName);

        using var response = await _httpClient.GetAsync(builder.Build(), cancellationToken);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<LyricsRecord>>(JsonSerializerOptions.Web, cancellationToken);

        return results ?? [];
    }

    private async Task<LyricsRecord?> GetRecordAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("LRCLIB returned 404 for {Url}", url);
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LyricsRecord>(JsonSerializerOptions.Web, cancellationToken);
    }

    private static string BuildGetUrl(string path, GetLyricsRequest request)
    {
        var builder = new QueryStringBuilder(path);
        builder.Add("track_name", request.TrackName);
        builder.Add("artist_name", request.ArtistName);
        builder.Add("album_name", request.AlbumName);

        if (request.Duration is { } duration)
            builder.Add("duration", duration.ToString("0.###", CultureInfo.InvariantCulture));

        return builder.Build();
    }

    private sealed class QueryStringBuilder
    {
        private readonly StringBuilder _buffer;
        private bool _hasQuery;

        public QueryStringBuilder(string path)
        {
            _buffer = new StringBuilder(path);
        }

        public void Add(string name, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            _buffer.Append(_hasQuery ? '&' : '?');
            _buffer.Append(Uri.EscapeDataString(name));
            _buffer.Append('=');
            _buffer.Append(Uri.EscapeDataString(value));
            _hasQuery = true;
        }

        public string Build() => _buffer.ToString();
    }
}
