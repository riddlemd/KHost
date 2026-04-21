using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class SingerQueueServiceTests : IDisposable
{
    private const string _cacheDir = "./cache";
    private readonly IOptionsMonitor<SingerQueueService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<SingerQueueService.ServiceOptions>>();
    private readonly ICacheService _cacheService;
    private readonly IPerformanceService _performanceService;
    private readonly ISingersService _singersService;
    private readonly Dictionary<Guid, Singer> _singerDb = [];
    private readonly SingerQueueService _service;

    public SingerQueueServiceTests()
    {
        var cacheOptions = Substitute.For<IOptionsMonitor<JsonFileCacheService.ServiceOptions>>();
        _cacheService = new JsonFileCacheService(NullLogger<JsonFileCacheService>.Instance, cacheOptions);
        _performanceService = Substitute.For<IPerformanceService>();
        _singersService = Substitute.For<ISingersService>();

        _performanceService.CreateAndEnqueueAsync(Arg.Any<Performance>())
            .Returns(args => Task.FromResult((Performance)args[0]));

        _singersService.ReadAsync(Arg.Any<Guid>())
            .Returns(args => { _singerDb.TryGetValue((Guid)args[0], out var s); return Task.FromResult(s); });

        _singersService.UpdateAsync(Arg.Any<Singer>())
            .Returns(args => { var s = (Singer)args[0]; _singerDb[s.Id] = s; return Task.CompletedTask; });

        _service = new SingerQueueService(NullLogger<SingerQueueService>.Instance, _options, _cacheService, _performanceService, _singersService);
    }

    public void Dispose()
    {
        var cacheFile = Path.Combine(_cacheDir, "singer-queue.json");
        if (File.Exists(cacheFile))
            File.Delete(cacheFile);
    }

    [Fact]
    public void NewService_StartsEmpty()
    {
        Assert.Empty(_service.Singers);
        Assert.Null(_service.SelectedSingerId);
        Assert.Null(_service.SelectedSinger);
    }

    [Fact]
    public async Task AddSingerAsync_AddsSingerById()
    {
        var singer = await EnqueueAsync("Alice");

        Assert.Single(_service.Singers);
        Assert.Equal("Alice", singer.Name);
        Assert.NotEqual(Guid.Empty, singer.Id);
    }

    [Fact]
    public async Task AddSingerAsync_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += (_, _) => raised = true;

        await EnqueueAsync("Alice");

        Assert.True(raised);
    }

    [Fact]
    public async Task RemoveSingerAsync_RemovesSinger()
    {
        var alice = await EnqueueAsync("Alice");

        await _service.RemoveSingerAsync(alice.Id);

        Assert.Empty(_service.Singers);
    }

    [Fact]
    public async Task RemoveSingerAsync_ClearsSelection_IfRemovingSelected()
    {
        var alice = await EnqueueAsync("Alice");
        await _service.SelectSingerAsync(alice.Id);

        await _service.RemoveSingerAsync(alice.Id);

        Assert.Null(_service.SelectedSingerId);
    }

    [Fact]
    public async Task SelectSingerAsync_SetsSelectedId()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");

        await _service.SelectSingerAsync(bob.Id);

        Assert.Equal(bob.Id, _service.SelectedSingerId);
        Assert.Equal("Bob", _service.SelectedSinger!.Name);
    }

    [Fact]
    public async Task AddMediaAsync_DelegatesWithUnknownSinger()
    {
        var mediaId = Guid.NewGuid();
        var entity = new MediaSearchEntity
        {
            Source = "FileSystem",
            ForeignKey = Guid.NewGuid().ToString(),
            DisplayName = "My Media",
        };

        await _service.AddMediaAsync(Guid.NewGuid(), entity);

        await _performanceService.DidNotReceive().CreateAndEnqueueAsync(Arg.Any<Performance>());
    }

    [Fact]
    public async Task AddMediaAsync_DelegatesWithKnownSinger()
    {
        var alice = await EnqueueAsync("Alice");
        var entity = new MediaSearchEntity
        {
            Source = "FileSystem",
            ForeignKey = "/music/media.mp4",
            DisplayName = "My Media"
        };

        await _service.AddMediaAsync(alice.Id, entity);

        await _performanceService.Received(1).CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.SingerId == alice.Id));
    }

    [Fact]
    public async Task MoveSingerUpAsync_SwapsWithPrevious()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveSingerUpAsync(b.Id);

        Assert.Equal(b.Id, _service.Singers[0].Id);
        Assert.Equal(a.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerUpAsync_DoesNothing_ForFirst()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveSingerUpAsync(a.Id);

        Assert.Equal(a.Id, _service.Singers[0].Id);
        Assert.Equal(b.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerDownAsync_SwapsWithNext()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveSingerDownAsync(a.Id);

        Assert.Equal(b.Id, _service.Singers[0].Id);
        Assert.Equal(a.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerDownAsync_DoesNothing_ForLast()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveSingerDownAsync(b.Id);

        Assert.Equal(a.Id, _service.Singers[0].Id);
        Assert.Equal(b.Id, _service.Singers[1].Id);
    }

    [Fact]
    public async Task MoveSingerToStartAsync_MovesToFirst()
    {
        await EnqueueAsync("A");
        await EnqueueAsync("B");
        var c = await EnqueueAsync("C");

        await _service.MoveSingerToStartAsync(c.Id);

        Assert.Equal(c.Id, _service.Singers[0].Id);
        Assert.Equal(3, _service.Singers.Count);
    }

    [Fact]
    public async Task MoveSingerToEndAsync_MovesToLast()
    {
        var a = await EnqueueAsync("A");
        await EnqueueAsync("B");
        await EnqueueAsync("C");

        await _service.MoveSingerToEndAsync(a.Id);

        Assert.Equal(a.Id, _service.Singers[^1].Id);
        Assert.Equal(3, _service.Singers.Count);
    }

    [Fact]
    public async Task SelectFirstSingerInQueueAsync_DoesNothing_WhenEmpty()
    {
        await _service.SelectFirstSingerInQueueAsync();

        Assert.Null(_service.SelectedSingerId);
    }

    [Fact]
    public async Task SelectFirstSingerInQueueAsync_SelectsFirst()
    {
        var a = await EnqueueAsync("A");
        await EnqueueAsync("B");

        await _service.SelectFirstSingerInQueueAsync();

        Assert.Equal(a.Id, _service.SelectedSingerId);
    }


    [Fact]
    public async Task SelectFirstSingerInQueueAsync_SelectsFirst_WhenMultipleSingersExist()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.SelectFirstSingerInQueueAsync();

        Assert.Equal(a.Id, _service.SelectedSingerId);
    }

    private async Task<Singer> EnqueueAsync(string name)
    {
        var singer = new Singer { Name = name };
        _singerDb[singer.Id] = singer;
        await _service.AddSingerAsync(singer.Id);
        return singer;
    }
}
