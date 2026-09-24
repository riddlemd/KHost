using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class JsonFileCacheServiceTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"khost-cache-{Guid.NewGuid():N}");
    private readonly JsonFileCacheService _service;

    public JsonFileCacheServiceTests()
    {
        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);

        var analytics = Substitute.For<IAnalyticsService>();
        _service = new JsonFileCacheService(NullLogger<JsonFileCacheService>.Instance, analytics, _cacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var result = await _service.LoadAsync<List<string>>("strings");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_DeserializesJson_WhenFileExists()
    {
        var data = new List<string> { "a", "b", "c" };
        var path = Path.Combine(_cacheDir, "strings.json");
        var json = JsonSerializer.Serialize(data, JsonSerializerOptions.Web);
        await File.WriteAllTextAsync(path, json);

        var result = await _service.LoadAsync<List<string>>("strings");

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0]);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefault_WhenJsonIsInvalid()
    {
        var path = Path.Combine(_cacheDir, "strings.json");
        await File.WriteAllTextAsync(path, "{ not valid json }");

        var result = await _service.LoadAsync<List<string>>("strings");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_CreatesFile()
    {
        var data = new List<int> { 1, 2, 3 };

        await _service.SaveAsync("number", data);

        var path = Path.Combine(_cacheDir, "number.json");
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        var loaded = JsonSerializer.Deserialize<List<int>>(content, JsonSerializerOptions.Web);
        Assert.Equal(3, loaded!.Count);
    }

    [Fact]
    public async Task SaveAsync_OverwritesFile_WhenItExists()
    {
        var path = Path.Combine(_cacheDir, "number.json");
        await File.WriteAllTextAsync(path, "old content");

        await _service.SaveAsync("number", 42);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("42", content);
        Assert.DoesNotContain("old content", content);
    }

    // GetCacheLocation resolves off AppContext.BaseDirectory, which two concurrent test runs share
    // as the same physical bin/ — deleting and recreating that real "cache" folder here used to
    // race a second run doing the same thing ("Directory not empty"). Redirecting BaseDirectory to
    // a unique temp root for the one SaveAsync call gives this test a directory nothing else on the
    // machine ever created, so there is nothing left to race.
    [Fact]
    public async Task SaveAsync_CreatesDirectory_IfItDoesNotExist()
    {
        var missing = Path.Combine(_cacheDir, "not-yet");
        var service = new JsonFileCacheService(
            NullLogger<JsonFileCacheService>.Instance, Substitute.For<IAnalyticsService>(), missing);

        await service.SaveAsync("testkey", "test");

        Assert.True(File.Exists(Path.Combine(missing, "testkey.json")));
    }

    [Fact]
    public async Task SaveAsync_IsThreadSafe_WithConcurrentWrites()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            var data = new Dictionary<string, int> { { $"key{i}", i } };
            tasks.Add(_service.SaveAsync("dict", data));
        }

        await Task.WhenAll(tasks);

        var path = Path.Combine(_cacheDir, "dict.json");
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        var result = JsonSerializer.Deserialize<Dictionary<string, int>>(content, JsonSerializerOptions.Web);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RoundTrip_SerializeAndDeserialize()
    {
        var original = new TestObject { Name = "Test", Age = 25 };

        await _service.SaveAsync("testobj", original);
        var loaded = await _service.LoadAsync<TestObject>("testobj");

        Assert.NotNull(loaded);
        Assert.Equal("Test", loaded.Name);
        Assert.Equal(25, loaded.Age);
    }

    [Fact]
    public async Task LoadAsync_WithComplexType()
    {
        var data = new TestContainer
        {
            Items = new List<TestObject>
            {
                new TestObject { Name = "A", Age = 10 },
                new TestObject { Name = "B", Age = 20 }
            }
        };

        await _service.SaveAsync("container", data);
        var loaded = await _service.LoadAsync<TestContainer>("container");

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal("A", loaded.Items[0].Name);
    }

    private class TestObject
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    private class TestContainer
    {
        public List<TestObject> Items { get; set; } = [];
    }
}
