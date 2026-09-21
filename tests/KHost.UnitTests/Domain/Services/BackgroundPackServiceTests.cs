using KHost.Abstractions.Models.Backgrounds;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>What a song may be rendered against: the set shipped with the app, plus a folder the
/// host points at. Both are flat folders of files, so either may be absent or empty at any moment.
/// </summary>
public class BackgroundPackServiceTests : IDisposable
{
    private readonly string _shipped = Path.Combine(Path.GetTempPath(), $"khost-shipped-{Guid.NewGuid():N}");
    private readonly string _host = Path.Combine(Path.GetTempPath(), $"khost-host-{Guid.NewGuid():N}");
    private readonly TestOptionsMonitor<BackgroundPackService.ServiceOptions> _options;
    private readonly BackgroundPackService _service;

    public BackgroundPackServiceTests()
    {
        Directory.CreateDirectory(_shipped);
        Directory.CreateDirectory(_host);
        _options = new TestOptionsMonitor<BackgroundPackService.ServiceOptions>(
            new BackgroundPackService.ServiceOptions { BuiltInFolder = _shipped, Folder = _host });
        _service = new BackgroundPackService(NullLogger<BackgroundPackService>.Instance, _options);
    }

    public void Dispose()
    {
        foreach (var folder in (string[])[_shipped, _host])
            try { Directory.Delete(folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static void Write(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
    }

    private void HostFolder(string? folder)
        => _options.Set(new BackgroundPackService.ServiceOptions { BuiltInFolder = _shipped, Folder = folder });

    /// <summary>A host who has named no folder still has everything KHost ships.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Read_NoHostFolder_StillOffersWhatShipped(string? folder)
    {
        Write(_shipped, "amber.mp4");
        HostFolder(folder);

        var pack = await _service.ReadAsync();

        Assert.Equal(BackgroundPackProblem.NoFolderSet, pack.Problem);
        Assert.Equal("amber.mp4", Assert.Single(pack.Entries).File);
    }

    /// <summary>A host folder on a machine that gets rebuilt must not take the shipped ones with
    /// it.</summary>
    [Fact]
    public async Task Read_HostFolderIsGone_SaysSoAndKeepsWhatShipped()
    {
        Write(_shipped, "amber.mp4");
        HostFolder(Path.Combine(_host, "not-here"));

        var pack = await _service.ReadAsync();

        Assert.Equal(BackgroundPackProblem.FolderMissing, pack.Problem);
        Assert.Single(pack.Entries);
    }

    [Fact]
    public async Task Read_BothFolders_OffersEverythingBetweenThem()
    {
        Write(_shipped, "amber.mp4");
        Write(_host, "violet.mp4");

        var pack = await _service.ReadAsync();

        Assert.Equal(BackgroundPackProblem.None, pack.Problem);
        Assert.Equal(["amber.mp4", "violet.mp4"], pack.Entries.Select(entry => entry.File));
    }

    /// <summary>Replacing one KHost ships is dropping a file in, not an argument about precedence.
    /// </summary>
    [Fact]
    public async Task Read_TheHostHasOneOfTheSameName_ShadowsTheShippedOne()
    {
        Write(_shipped, "amber.mp4");
        Write(_host, "amber.mp4");

        var entry = Assert.Single((await _service.ReadAsync()).Entries);

        Assert.Equal(Path.Combine(_host, "amber.mp4"), entry.FilePath);
    }

    /// <summary>The venue ticks boxes in this grid, so tiles must not move between reads — and the
    /// order a filesystem hands files back in is neither stable nor the same on two machines.
    /// </summary>
    [Fact]
    public async Task Read_SeveralBackgrounds_ComeBackSortedByName()
    {
        Write(_shipped, "violet.mp4");
        Write(_shipped, "Slow Tide.mp4");
        Write(_host, "amber.mp4");
        Write(_host, "aurora.mp4");

        var names = (await _service.ReadAsync()).Entries.Select(entry => entry.Name);

        Assert.Equal(["amber", "aurora", "Slow Tide", "violet"], names);
    }

    [Fact]
    public async Task Read_AClip_IsNamedByItsFileNameWithoutTheExtension()
    {
        Write(_host, "amber bokeh.mp4");

        var entry = Assert.Single((await _service.ReadAsync()).Entries);

        Assert.Equal("amber bokeh", entry.Name);
        Assert.Equal("amber bokeh.mp4", entry.File);
    }

    [Fact]
    public async Task Read_AStillBesideTheClip_IsPairedByName()
    {
        Write(_host, "amber.mp4");
        Write(_host, "amber.jpg");

        Assert.Equal(Path.Combine(_host, "amber.jpg"),
            Assert.Single((await _service.ReadAsync()).Entries).StillPath);
    }

    [Fact]
    public async Task Read_NoStillBesideTheClip_LeavesItWithoutOne()
    {
        Write(_host, "amber.mp4");

        Assert.Null(Assert.Single((await _service.ReadAsync()).Entries).StillPath);
    }

    [Fact]
    public async Task Read_FilesThatAreNotClips_AreIgnored()
    {
        Write(_host, "amber.mp4");
        Write(_host, "notes.txt");
        Write(_host, "cover.jpg");

        Assert.Equal("amber.mp4", Assert.Single((await _service.ReadAsync()).Entries).File);
    }

    [Fact]
    public async Task Read_AnUppercaseExtension_IsStillAClip()
    {
        Write(_host, "amber.MP4");

        Assert.Single((await _service.ReadAsync()).Entries);
    }

    /// <summary>A pack is one flat folder, so a clip filed away below it is not quietly part of it.
    /// </summary>
    [Fact]
    public async Task Read_AClipInASubfolder_IsNotOffered()
    {
        Write(_host, "amber.mp4");
        Write(_host, Path.Combine("archive", "retired.mp4"));

        Assert.Equal("amber.mp4", Assert.Single((await _service.ReadAsync()).Entries).File);
    }

    /// <summary>Which is why a venue stores the extension too.</summary>
    [Fact]
    public async Task Read_TwoClipsSharingAName_AreTwoBackgrounds()
    {
        Write(_host, "amber.mp4");
        Write(_host, "amber.mov");

        var entries = (await _service.ReadAsync()).Entries;

        Assert.Equal(["amber.mov", "amber.mp4"], entries.Select(entry => entry.File));
    }

    [Fact]
    public async Task Read_NothingAnywhere_IsNotAProblem()
    {
        var pack = await _service.ReadAsync();

        Assert.Equal(BackgroundPackProblem.None, pack.Problem);
        Assert.False(pack.HasAny);
    }

    /// <summary>Read live, so a host changing the folder does not have to restart.</summary>
    [Fact]
    public async Task Read_TheFolderChanges_IsSeenWithoutARestart()
    {
        var moved = Path.Combine(Path.GetTempPath(), $"khost-moved-{Guid.NewGuid():N}");
        Directory.CreateDirectory(moved);
        Write(moved, "elsewhere.mp4");

        try
        {
            Assert.Empty((await _service.ReadAsync()).Entries);

            HostFolder(moved);

            Assert.Equal("elsewhere.mp4", Assert.Single((await _service.ReadAsync()).Entries).File);
        }
        finally { Directory.Delete(moved, recursive: true); }
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.mkv")]
    [InlineData("clip.avi")]
    [InlineData("clip.flv")]
    [InlineData("clip.mov")]
    [InlineData("clip.webm")]
    [InlineData("clip.m4v")]
    public async Task Read_EveryFormatTheHostCallsVideo_CountsAsABackground(string name)
    {
        Write(_host, name);

        Assert.Single((await _service.ReadAsync()).Entries);
    }
}
