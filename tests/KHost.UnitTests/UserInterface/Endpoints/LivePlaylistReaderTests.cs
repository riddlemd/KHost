using System.Net;
using KHost.Abstractions.Services;
using KHost.UserInterface.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.UserInterface.Endpoints;

public sealed class LivePlaylistReaderTests : IDisposable
{
    private const string Playlist = "#EXTM3U\n#EXT-X-VERSION:6\n#EXTINF:2.0,\nstream0.ts\n";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "khost-playlist-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public LivePlaylistReaderTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "stream.m3u8");
        File.WriteAllText(_path, Playlist);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ReadAsync_FileHeldOpenByAWriter_ReadsIt()
    {
        // ffmpeg's own handle: writing, sharing only read.
        await using var writer = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);

        var text = await LivePlaylistReader.ReadAsync(_path, attempts: 1);

        Assert.Equal(Playlist, text);
    }

    [Fact]
    public async Task ReadAsync_FileLockedThroughEveryAttempt_ReturnsNull()
    {
        await using var holder = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var text = await LivePlaylistReader.ReadAsync(_path, attempts: 3, delay: TimeSpan.FromMilliseconds(5));

        Assert.Null(text);
    }

    [Fact]
    public async Task ReadAsync_FileMissing_ReturnsNull()
    {
        File.Delete(_path);

        var text = await LivePlaylistReader.ReadAsync(_path, attempts: 2, delay: TimeSpan.FromMilliseconds(5));

        Assert.Null(text);
    }

    [Fact]
    public async Task ReadAsync_LockReleasedBetweenAttempts_ReadsIt()
    {
        var holder = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(60);
            await holder.DisposeAsync();
        });

        var text = await LivePlaylistReader.ReadAsync(_path, attempts: 40, delay: TimeSpan.FromMilliseconds(25));
        await release;

        Assert.Equal(Playlist, text);
    }

    [Fact]
    public async Task MediaStream_PlaylistLockedThroughEveryAttempt_Answers503()
    {
        await using var holder = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var response = await GetPlaylistAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("1", response.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task MediaStream_PlaylistHeldOpenByAWriter_ServesItPinnedToTheTop()
    {
        await using var writer = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);

        using var response = await GetPlaylistAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("#EXTM3U\n#EXT-X-START:TIME-OFFSET=0", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> GetPlaylistAsync()
    {
        var streams = Substitute.For<IMediaStreamService>();
        streams.ResolveArtifact("session", "stream.m3u8").Returns(_path);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(streams);

        await using var app = builder.Build();
        app.MapMediaStream();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient();
        var response = await client.GetAsync($"{address}/media/session/stream.m3u8");
        await response.Content.LoadIntoBufferAsync();
        await app.StopAsync();
        return response;
    }
}
