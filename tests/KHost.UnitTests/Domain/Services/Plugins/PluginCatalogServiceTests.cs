using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Common.Plugins;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Net.Http.Headers;
using System.Net;
using System.Text;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginCatalogServiceTests
{
    private const string Url = "https://example.test/catalog.json";

    private const string CatalogJson = """
        {
          "schemaVersion": 1,
          "plugins": [
            { "id": "0a000000-0000-4000-8000-0000000000c1", "name": "YouTube", "releases": [] }
          ]
        }
        """;

    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly MemoryCache _cache = new();
    private readonly StubTimeProvider _time = new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetAsync_NothingCached_FetchesAndKeepsIt()
    {
        var handler = new StubHandler(CatalogJson);
        var service = BuildService(handler);

        var snapshot = await service.GetAsync();

        Assert.Equal("YouTube", Assert.Single(snapshot!.Catalog.Plugins).Name);
        Assert.Equal(1, handler.Calls);
        Assert.Null(service.LastError);
        Assert.NotNull(await _cache.LoadAsync<PluginCatalogSnapshot>(PluginCatalogSnapshot.CacheKey));
    }

    [Fact]
    public async Task GetAsync_CachedAndStillFresh_DoesNotHitTheNetwork()
    {
        var handler = new StubHandler(CatalogJson);
        var service = BuildService(handler);

        await service.GetAsync();
        await service.GetAsync();

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_CachedCopyHasAgedOut_FetchesAgain()
    {
        var handler = new StubHandler(CatalogJson);
        var service = BuildService(handler, lifetime: TimeSpan.FromHours(6));

        await service.GetAsync();

        _time.Now = _time.Now.AddHours(7);

        await service.GetAsync();

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RefreshAsync_CachedCopyStillFresh_FetchesAnyway()
    {
        var handler = new StubHandler(CatalogJson);
        var service = BuildService(handler);

        await service.GetAsync();
        await service.RefreshAsync();

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_CachedFromAPreviousRun_IsReadWithoutTheNetwork()
    {
        await _cache.SaveAsync(PluginCatalogSnapshot.CacheKey, new PluginCatalogSnapshot
        {
            Catalog = new PluginCatalog { SchemaVersion = 1, Plugins = [new PluginCatalogEntry { Name = "Cached" }] },
            FetchedUtc = _time.Now.UtcDateTime,
        });

        var handler = new StubHandler(CatalogJson);
        var snapshot = await BuildService(handler).GetAsync();

        Assert.Equal("Cached", Assert.Single(snapshot!.Catalog.Plugins).Name);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RefreshAsync_FetchFails_KeepsTheCachedCatalogAndReportsWhy()
    {
        var handler = new StubHandler(CatalogJson);
        var service = BuildService(handler);

        await service.GetAsync();

        handler.Status = HttpStatusCode.InternalServerError;

        var snapshot = await service.RefreshAsync();

        Assert.Equal("YouTube", Assert.Single(snapshot!.Catalog.Plugins).Name);
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public async Task GetAsync_UnsupportedSchemaVersion_RefusesTheDocument()
    {
        var handler = new StubHandler("""{ "schemaVersion": 99, "plugins": [{ "name": "Future" }] }""");
        var service = BuildService(handler);

        Assert.Null(await service.GetAsync());
        Assert.Contains("schema v99", service.LastError);
    }

    [Fact]
    public async Task GetAsync_ResponseIsNotJson_RefusesIt()
    {
        var service = BuildService(new StubHandler("<html>nope</html>"));

        Assert.Null(await service.GetAsync());
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public async Task RefreshAsync_ServerSaysNotModified_KeepsTheCatalogAndMovesTheCheckedTime()
    {
        var handler = new StubHandler(CatalogJson) { ETag = "\"v1\"" };
        var service = BuildService(handler);

        await service.GetAsync();

        _time.Now = _time.Now.AddHours(1);
        handler.Status = HttpStatusCode.NotModified;

        var snapshot = await service.RefreshAsync();

        Assert.Equal("YouTube", Assert.Single(snapshot!.Catalog.Plugins).Name);
        Assert.Equal(_time.Now.UtcDateTime, snapshot.FetchedUtc);
        Assert.Equal("\"v1\"", handler.LastIfNoneMatch);
    }

    [Fact]
    public async Task GetAsync_FetchSucceeds_AnnouncesTheChange()
    {
        var raised = 0;

        using var subscription = _broker.Subscribe<PluginCatalogChanged>(_ => raised++);

        await BuildService(new StubHandler(CatalogJson)).GetAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task GetAsync_NoUrlConfigured_ReportsItWithoutThrowing()
    {
        var service = BuildService(new StubHandler(CatalogJson), url: null);

        Assert.Null(await service.GetAsync());
        Assert.Contains("catalog URL", service.LastError);
    }

    [Fact]
    public async Task GetAsync_ResponseDeclaresMoreThanTheCap_IsRefusedWithoutReadingIt()
    {
        // A small body behind a huge Content-Length: only the declared-size check can refuse this,
        // and refusing on the header is the point — the body is never pulled down.
        var service = BuildService(new StubHandler(CatalogJson) { DeclaredLength = 3 * 1024 * 1024 });

        Assert.Null(await service.GetAsync());
        Assert.Contains("larger", service.LastError);
    }

    [Fact]
    public async Task GetAsync_ResponseWithNoDeclaredLengthOverflowsTheCap_IsRefused()
    {
        var oversized = "{\"schemaVersion\":1,\"plugins\":[]," + new string(' ', 3 * 1024 * 1024) + "\"x\":1}";
        var service = BuildService(new StubHandler(oversized) { Chunked = true });

        Assert.Null(await service.GetAsync());
        Assert.Contains("larger", service.LastError);
    }

    private PluginCatalogService BuildService(StubHandler handler, string? url = Url, TimeSpan? lifetime = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();

        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var options = Substitute.For<IOptionsMonitor<PluginCatalogService.ServiceOptions>>();

        options.CurrentValue.Returns(new PluginCatalogService.ServiceOptions
        {
            Url = url,
            CacheLifetime = lifetime ?? TimeSpan.FromHours(6),
        });

        return new PluginCatalogService(
            NullLogger<PluginCatalogService>.Instance,
            factory,
            _cache,
            options,
            _time,
            _broker);
    }

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryCache : ICacheService
    {
        private readonly Dictionary<string, string> _entries = [];

        public Task<T?> LoadAsync<T>(string key)
        {
            if (!_entries.TryGetValue(key, out var json))
                return Task.FromResult<T?>(default);

            // Round-trips through JSON like the real cache, so a shape that cannot be rehydrated
            // fails here rather than only in the running app.
            return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<T>(json, System.Text.Json.JsonSerializerOptions.Web));
        }

        public Task SaveAsync<T>(string key, T state)
        {
            _entries[key] = System.Text.Json.JsonSerializer.Serialize(state, System.Text.Json.JsonSerializerOptions.Web);

            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? ETag { get; set; }

        /// <summary>Content-Length to advertise regardless of the body's real size.</summary>
        public long? DeclaredLength { get; set; }

        /// <summary>Send the body with no Content-Length at all, as a chunked response does.</summary>
        public bool Chunked { get; set; }

        public int Calls { get; private set; }

        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastIfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();

            var response = new HttpResponseMessage(Status)
            {
                Content = Status == HttpStatusCode.NotModified
                    ? new StringContent(string.Empty)
                    : Body(),
            };

            if (DeclaredLength is { } declared)
                response.Content.Headers.ContentLength = declared;

            if (ETag is not null)
                response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);

            return Task.FromResult(response);
        }

        // Non-seekable on purpose: StreamContent computes a Content-Length from any stream that
        // can report one, which would send this down the declared-size path instead.
        private HttpContent Body() => Chunked
            ? new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes(body)))
            : new StringContent(body, Encoding.UTF8, "application/json");
    }

    private sealed class ForwardOnlyStream(byte[] payload) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, payload.Length - _position);

            Array.Copy(payload, _position, buffer, offset, read);
            _position += read;

            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
