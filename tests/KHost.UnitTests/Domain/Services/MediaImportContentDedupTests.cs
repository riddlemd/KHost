using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;

namespace KHost.UnitTests.Domain.Services;

/// <summary>
/// The dedup path end to end over real bytes and a real SQLite file — only the metadata parser is
/// stubbed. Mocked hashes cannot show that a moved file is recognised or that two same-size songs
/// stay apart, which is the whole point of the feature.
/// </summary>
public class MediaImportContentDedupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-dedup-e2e-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;
    private readonly MediaImportService _service;

    public MediaImportContentDedupTests()
    {
        Directory.CreateDirectory(_directory);
        _dbPath = Path.Combine(_directory, "khost.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();
        using (var context = _factory.CreateDbContext())
            context.Database.Migrate();

        _repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);

        var parser = Substitute.For<IMediaFileParsingService>();
        parser.LoadAndParseAsync(Arg.Any<string>()).Returns(call =>
            new Media { FilePath = call.Arg<string>(), Title = Path.GetFileName(call.Arg<string>()) });

        var mediaService = Substitute.For<IMediaService>();
        mediaService.CreateAsync(Arg.Any<Media>()).Returns(call => _repository.CreateAsync(call.Arg<Media>()));

        var analytics = Substitute.For<IAnalyticsService>();
        analytics.StartActivity(Arg.Any<string>()).Returns(Substitute.For<IAnalyticsActivity>());

        _service = new MediaImportService(
            NullLogger<MediaImportService>.Instance,
            parser,
            _repository,
            mediaService,
            new MediaFingerprintService(NullLogger<MediaFingerprintService>.Instance),
            analytics,
            EmptyRegistry(),
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task Import_StoresTheSizeAndSampledHash()
    {
        var song = WriteFile("song.mp4", Song());

        await ImportAsync(song);

        var stored = Assert.Single(await AllMediaAsync());
        Assert.Equal(new FileInfo(song).Length, stored.FileSize);
        Assert.NotNull(stored.SampledHash);
    }

    [Fact]
    public async Task Import_SkipsTheSameFileUnderANewName()
    {
        var bytes = Song();
        await ImportAsync(WriteFile("song.mp4", bytes));

        await ImportAsync(WriteFile("song (copy).mp4", bytes));

        Assert.Single(await AllMediaAsync());
    }

    [Fact]
    public async Task Import_SkipsTheSameFileMovedToAnotherFolder()
    {
        var bytes = Song();
        await ImportAsync(WriteFile("song.mp4", bytes));

        Directory.CreateDirectory(Path.Combine(_directory, "sorted"));
        await ImportAsync(WriteFile(Path.Combine("sorted", "song.mp4"), bytes));

        Assert.Single(await AllMediaAsync());
    }

    [Fact]
    public async Task Import_KeepsTwoDifferentSongsThatHappenToBeTheSameSize()
    {
        // CD+G sizes land on a 96-byte grid, so equal sizes are ordinary in a karaoke library.
        var first = Song();
        var second = Song();
        second[first.Length / 2] ^= 0xFF;

        await ImportAsync(WriteFile("a.cdg", first));
        await ImportAsync(WriteFile("b.cdg", second));

        Assert.Equal(2, (await AllMediaAsync()).Count);
    }

    [Fact]
    public async Task Import_SkipsADuplicateInsideASingleSelection()
    {
        var bytes = Song();

        await ImportAsync(WriteFile("a.mp4", bytes), WriteFile("b.mp4", bytes));

        Assert.Single(await AllMediaAsync());
    }

    [Fact]
    public async Task Import_RecognisesAFileImportedBeforeFingerprintsExisted()
    {
        var bytes = Song();
        var original = WriteFile("song.mp4", bytes);
        await _repository.CreateAsync(new Media { FilePath = original, Title = "Legacy" });

        await ImportAsync(WriteFile("song (copy).mp4", bytes));

        var stored = Assert.Single(await AllMediaAsync());
        Assert.Equal("Legacy", stored.Title);
        Assert.Equal(new FileInfo(original).Length, stored.FileSize);
    }

    private static byte[] Song()
    {
        // Shared head and tail, so only the middle or the length can tell two of these apart —
        // exactly the case the sampled hash cannot decide on its own.
        var bytes = new byte[300_000];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 7);

        return bytes;
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private async Task<IReadOnlyList<Media>> AllMediaAsync()
    {
        await using var context = await _factory.CreateDbContextAsync();
        return await context.Media.ToListAsync();
    }

    private async Task ImportAsync(params string[] paths)
    {
        await _service.StartAsync(paths);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_service.State != ImportState.Idle && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        Assert.Equal(ImportState.Idle, _service.State);
        Assert.Equal(0, _service.FailedCount);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }

    private static IPluginRegistry EmptyRegistry()
    {
        var registry = Substitute.For<IPluginRegistry>();
        registry.Plugins.Returns((IReadOnlyList<KHost.Abstractions.Models.Plugins.DiscoveredPlugin>)[]);
        return registry;
    }
}
