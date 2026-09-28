using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public sealed class HlsSessionSweeperTests : IDisposable
{
    private const int DeadOwner = 111;
    private const int LiveOwner = 222;

    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"khost-sweep-{Guid.NewGuid():N}");
    private readonly string _root;

    public HlsSessionSweeperTests()
    {
        _root = Path.Combine(_scratch, "khost-streams");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Sweep_AFolderWhoseOwnerIsGone_IsDeleted()
    {
        var session = Session(owner: DeadOwner);

        var swept = Sweep();

        Assert.Equal([Path.GetFileName(session)], swept);
        Assert.False(Directory.Exists(session));
    }

    [Fact]
    public void Sweep_AFolderWhoseOwnerIsRunning_IsKept_HoweverOld()
    {
        var session = Session(owner: LiveOwner, age: TimeSpan.FromDays(3));

        Assert.Empty(Sweep());
        Assert.True(Directory.Exists(session));
    }

    [Fact]
    public void Sweep_AnUnownedFolderPastTheAge_IsDeleted()
    {
        var session = Session(owner: null, age: HlsSessionSweeper.UnownedMaxAge + TimeSpan.FromMinutes(1));

        Sweep();

        Assert.False(Directory.Exists(session));
    }

    [Fact]
    public void Sweep_AnUnownedFolderWrittenRecently_IsKept()
    {
        var session = Session(owner: null, age: HlsSessionSweeper.UnownedMaxAge - TimeSpan.FromMinutes(1));

        Sweep();

        Assert.True(Directory.Exists(session));
    }

    [Fact]
    public void Sweep_AFolderNotNamedLikeASession_IsKept()
    {
        var other = Path.Combine(_root, "not-a-session");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, HlsSessionSweeper.OwnerFileName), DeadOwner.ToString());
        Directory.SetLastWriteTimeUtc(other, Now - TimeSpan.FromDays(3));

        Sweep();

        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void Sweep_ALinkInsideASession_IsRemovedWithoutTouchingItsTarget()
    {
        var outside = Path.Combine(_scratch, "outside");
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "precious.txt");
        File.WriteAllText(precious, "keep");

        var session = Session(owner: DeadOwner);
        Directory.CreateSymbolicLink(Path.Combine(session, "link"), outside);

        Sweep();

        Assert.False(Directory.Exists(session));
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void Sweep_ASessionNamedLinkToAFolderOutside_IsNotFollowed()
    {
        var outside = Path.Combine(_scratch, "outside");
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "precious.txt");
        File.WriteAllText(precious, "keep");
        File.WriteAllText(Path.Combine(outside, HlsSessionSweeper.OwnerFileName), DeadOwner.ToString());

        var link = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(link, outside);

        Assert.Empty(Sweep());
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void WriteOwner_NamesThisProcess()
    {
        var session = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);

        HlsSessionSweeper.WriteOwner(session);

        Assert.Equal(Environment.ProcessId.ToString(), File.ReadAllText(Path.Combine(session, HlsSessionSweeper.OwnerFileName)));
    }

    [Fact]
    public void IsRunning_ThisProcess_IsTrue()
        => Assert.True(HlsSessionSweeper.IsRunning(Environment.ProcessId));

    [Fact]
    public void Service_OnConstruction_SweepsWhatADeadHostLeft()
    {
        var stale = Session(owner: null);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromDays(2));

        using var service = NewService();

        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public async Task Service_OpeningASession_MarksItWithThisProcess()
    {
        var anchor = Path.Combine(_scratch, "song.kfa");
        await File.WriteAllTextAsync(anchor, "");
        using var service = NewService();

        var session = await service.OpenWithoutEncodeAsync(anchor);

        var owner = Path.Combine(session.WorkingDirectory!, HlsSessionSweeper.OwnerFileName);
        Assert.Equal(Environment.ProcessId.ToString(), await File.ReadAllTextAsync(owner));
    }

    private HlsMediaStreamService NewService() => new(
        NullLogger<HlsMediaStreamService>.Instance,
        new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
        {
            WorkingDirectory = _root,
        }),
        new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []));

    private IReadOnlyList<string> Sweep() => HlsSessionSweeper.Sweep(_root, Now, pid => pid == LiveOwner);

    private string Session(int? owner, TimeSpan? age = null)
    {
        var session = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "segment0.ts"), "");
        if (owner is { } pid)
            File.WriteAllText(Path.Combine(session, HlsSessionSweeper.OwnerFileName), pid.ToString());

        // Set last: writing into the folder moves its time.
        Directory.SetLastWriteTimeUtc(session, Now - (age ?? TimeSpan.Zero));
        return session;
    }
}
