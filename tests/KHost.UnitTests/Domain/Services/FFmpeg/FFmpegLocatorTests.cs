using KHost.Abstractions.Models;
using KHost.Domain.Services.FFmpeg;

namespace KHost.UnitTests.Domain.Services.FFmpeg;

public class FFmpegLocatorTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-locator-");

    private string Configured => Folder("configured");
    private string Bin => Folder("bin");
    private string OnPath => Folder("path");

    public void Dispose()
    {
        try { _root.Delete(recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Resolve_ConfiguredFolderHasIt_WinsOverBinAndPath()
    {
        Place(Configured, Bin, OnPath);

        Assert.Equal(Program(Configured), Resolve());
    }

    /// <summary>The setting names a place to look first, not the only place.</summary>
    [Fact]
    public void Resolve_ConfiguredFolderLacksIt_FallsToTheBinFolder()
    {
        Place(Bin, OnPath);

        Assert.Equal(Program(Bin), Resolve());
    }

    [Fact]
    public void Resolve_BinFolderHasIt_WinsOverPath()
    {
        Place(Bin, OnPath);

        Assert.Equal(Program(Bin), FFmpegLocator.Resolve(FFmpegTool.FFmpeg, null, Bin, OnPath, windows: false));
    }

    [Fact]
    public void Resolve_OnlyOnPath_FindsItThere()
    {
        Place(OnPath);

        Assert.Equal(Program(OnPath), Resolve());
    }

    [Fact]
    public void Resolve_QuotedPathEntry_IsReadWithoutItsQuotes()
    {
        Place(OnPath);

        var path = string.Join(Path.PathSeparator, Folder("empty"), $"\"{OnPath}\"");

        Assert.Equal(Program(OnPath), FFmpegLocator.Resolve(FFmpegTool.FFmpeg, null, null, path, windows: false));
    }

    [Fact]
    public void Resolve_Nowhere_ReturnsNull()
        => Assert.Null(Resolve());

    [Fact]
    public void Resolve_OnWindows_LooksForTheExe()
    {
        File.WriteAllText(Path.Combine(Bin, "ffprobe.exe"), "");

        Assert.Equal(Path.Combine(Bin, "ffprobe.exe"),
            FFmpegLocator.Resolve(FFmpegTool.FFprobe, null, Bin, null, windows: true));
        Assert.Null(FFmpegLocator.Resolve(FFmpegTool.FFprobe, null, Bin, null, windows: false));
    }

    [Theory]
    [InlineData("ffmpeg version 9.0 Copyright (c) 2000-2026 the FFmpeg developers\nbuilt with clang", "9.0")]
    [InlineData("ffprobe version 9.0.2-tessus  https://evermeet.cx/ffmpeg/  Copyright", "9.0.2-tessus")]
    [InlineData("ffmpeg version 2026-09-19-git-abc123-essentials_build-www.gyan.dev Copyright", "2026-09-19-git-abc123-essentials_build-www.gyan.dev")]
    [InlineData("ffmpeg version n7.1 Copyright", "n7.1")]
    public void ParseVersion_ReadsWhateverFollowsTheWordVersion(string output, string expected)
        => Assert.Equal(expected, FFmpegLocator.ParseVersion(output));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Usage: something else entirely")]
    [InlineData("built with ffmpeg version 9.0")]
    public void ParseVersion_NotAVersionLine_IsNull(string? output)
        => Assert.Null(FFmpegLocator.ParseVersion(output));

    private string? Resolve() => FFmpegLocator.Resolve(FFmpegTool.FFmpeg, Configured, Bin, OnPath, windows: false);

    private static string Program(string folder) => Path.Combine(folder, "ffmpeg");

    private static void Place(params string[] folders)
    {
        foreach (var folder in folders)
            File.WriteAllText(Program(folder), "");
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;
}
