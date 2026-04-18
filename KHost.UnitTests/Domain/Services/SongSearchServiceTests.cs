using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class SongSearchServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SongSearchService _service = new();

    public SongSearchServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"song-search-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateFile(string relativePath)
    {
        var full = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, "");
        return full;
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenDirectoryDoesNotExist()
    {
        var results = await _service.SearchAsync(Path.Combine(_tempDir, "nonexistent"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_FindsSupportedExtensions()
    {
        CreateFile("song1.mp4");
        CreateFile("song2.mkv");
        CreateFile("song3.cdg");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SearchAsync_IgnoresUnsupportedExtensions()
    {
        CreateFile("song.mp4");
        CreateFile("notes.txt");
        CreateFile("image.jpg");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Single(results);
        Assert.EndsWith(".mp4", results[0].FilePath);
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitiveForExtensions()
    {
        CreateFile("song.MP4");
        CreateFile("song.Mkv");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_RecursesByDefault()
    {
        CreateFile("song.mp4");
        CreateFile("sub/nested.mp4");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_RespectsNonRecursiveFlag()
    {
        CreateFile("song.mp4");
        CreateFile("sub/nested.mp4");

        var results = await _service.SearchAsync(_tempDir, recursive: false);

        Assert.Single(results);
        Assert.Equal("song", results[0].DisplayName);
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameCaseInsensitive()
    {
        CreateFile("Karaoke_Wonderful.mp4");
        CreateFile("Rock_Anthem.mp4");

        var results = await _service.SearchAsync(_tempDir, filter: "wonder");

        Assert.Single(results);
        Assert.Equal("Karaoke_Wonderful", results[0].DisplayName);
    }

    [Fact]
    public async Task SearchAsync_SortsResultsAlphabetically()
    {
        CreateFile("zebra.mp4");
        CreateFile("apple.mp4");
        CreateFile("mango.mp4");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Equal(new[] { "apple", "mango", "zebra" }, results.Select(r => r.DisplayName));
    }

    [Fact]
    public async Task SearchAsync_MapsCdgExtensionToCombinedFormat()
    {
        CreateFile("song.cdg");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Single(results);
        Assert.Equal("CDG+MP3", results[0].Format);
    }

    [Fact]
    public async Task SearchAsync_MapsStandardExtensionsToUppercase()
    {
        CreateFile("clip.mp4");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Single(results);
        Assert.Equal("MP4", results[0].Format);
    }

    [Fact]
    public async Task SearchAsync_ReturnsDisplayNameWithoutExtension()
    {
        var path = CreateFile("Amazing Song.mp4");

        var results = await _service.SearchAsync(_tempDir);

        Assert.Single(results);
        Assert.Equal("Amazing Song", results[0].DisplayName);
        Assert.Equal(path, results[0].FilePath);
    }

    [Fact]
    public async Task SearchAsync_EmptyFilter_ReturnsAllMatches()
    {
        CreateFile("a.mp4");
        CreateFile("b.mp4");

        var results = await _service.SearchAsync(_tempDir, filter: "");

        Assert.Equal(2, results.Count);
    }
}
