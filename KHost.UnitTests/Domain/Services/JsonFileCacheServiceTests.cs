using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KHost.UnitTests.Domain.Services;

public class JsonFileCacheServiceTests : IDisposable
{
    private const string _cacheDir = "./cache";
    private readonly JsonFileCacheService _service;

    public JsonFileCacheServiceTests()
    {
        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);

        var options = Substitute.For<IOptionsMonitor<JsonFileCacheService.ServiceOptions>>();
        _service = new JsonFileCacheService(NullLogger<JsonFileCacheService>.Instance, options);
    }

    public void Dispose()
    {
        CleanupCacheFile("strings");
        CleanupCacheFile("testobj");
        CleanupCacheFile("container");
        CleanupCacheFile("number");
        CleanupCacheFile("dict");
    }

    private void CleanupCacheFile(string key)
    {
        var path = Path.Combine(_cacheDir, key + ".json");
        if (File.Exists(path))
            File.Delete(path);
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

    [Fact]
    public async Task SaveAsync_CreatesDirectory_IfItDoesNotExist()
    {
        var cacheDir = Path.Combine(_cacheDir);
        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, recursive: true);

        try
        {
            await _service.SaveAsync("testkey", "test");

            var path = Path.Combine(cacheDir, "testkey.json");
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
            Directory.CreateDirectory(cacheDir);
        }
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
