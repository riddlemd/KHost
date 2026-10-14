using KHost.UserInterface.Services;
using Microsoft.Extensions.Logging.Abstractions;
using FileManager = KHost.UserInterface.Services.FileRevealService.FileManager;

namespace KHost.UnitTests.UserInterface.Services;

public class FileRevealServiceTests : IDisposable
{
    // Built with this OS's separator: the folder comes back from Path, which writes its own.
    private static readonly string Folder = Path.Combine(Path.DirectorySeparatorChar + "karaoke", "The Band");
    private static readonly string Song = Path.Combine(Folder, "song one.mp4");

    private readonly string _root = Directory.CreateTempSubdirectory("khost-reveal-").FullName;
    private readonly FileRevealService _service = new(NullLogger<FileRevealService>.Instance);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void BuildStartInfo_FinderWithTheFile_SelectsItInItsFolder()
    {
        var info = FileRevealService.BuildStartInfo(FileManager.Finder, Song, fileExists: true, folderExists: true)!;

        Assert.Equal("open", info.FileName);
        Assert.Equal(["-R", Song], info.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_FinderWithOnlyTheFolder_OpensTheFolder()
    {
        var info = FileRevealService.BuildStartInfo(FileManager.Finder, Song, fileExists: false, folderExists: true)!;

        Assert.Equal([Folder], info.ArgumentList);
    }

    /// <summary>Explorer reads its own command line; /select and the quoted path must arrive as one argument.</summary>
    [Fact]
    public void BuildStartInfo_ExplorerWithTheFile_PassesSelectAndTheQuotedPathTogether()
    {
        var info = FileRevealService.BuildStartInfo(FileManager.Explorer, Song, fileExists: true, folderExists: true)!;

        Assert.Equal("explorer.exe", info.FileName);
        Assert.Equal($"/select,\"{Song}\"", info.Arguments);
    }

    [Fact]
    public void BuildStartInfo_ExplorerWithOnlyTheFolder_OpensTheQuotedFolder()
    {
        var info = FileRevealService.BuildStartInfo(FileManager.Explorer, Song, fileExists: false, folderExists: true)!;

        Assert.Equal($"\"{Folder}\"", info.Arguments);
    }

    [Fact]
    public void BuildStartInfo_FreeDesktopWithTheFile_OpensItsFolder()
    {
        var info = FileRevealService.BuildStartInfo(FileManager.FreeDesktop, Song, fileExists: true, folderExists: true)!;

        Assert.Equal("xdg-open", info.FileName);
        Assert.Equal([Folder], info.ArgumentList);
    }

    [Theory]
    [InlineData(FileManager.Finder)]
    [InlineData(FileManager.Explorer)]
    [InlineData(FileManager.FreeDesktop)]
    public void BuildStartInfo_NeitherFileNorFolder_IsNull(FileManager manager)
        => Assert.Null(FileRevealService.BuildStartInfo(manager, Song, fileExists: false, folderExists: false));

    [Theory]
    [InlineData(FileManager.Finder, "Show in Finder")]
    [InlineData(FileManager.Explorer, "Show in File Explorer")]
    [InlineData(FileManager.FreeDesktop, "Open containing folder")]
    public void LabelFor_EachFileManager_NamesItsOwnAction(FileManager manager, string label)
        => Assert.Equal(label, FileRevealService.LabelFor(manager));

    [Fact]
    public void CanReveal_FileOnDisk_IsTrue()
    {
        var path = Path.Combine(_root, "song.mp4");
        File.WriteAllText(path, "");

        Assert.True(_service.CanReveal(path));
    }

    /// <summary>An ephemeral row's file is deleted while its folder stays.</summary>
    [Fact]
    public void CanReveal_OnlyTheFolderLeft_IsTrue()
        => Assert.True(_service.CanReveal(Path.Combine(_root, "gone.mp4")));

    [Fact]
    public void CanReveal_NeitherFileNorFolder_IsFalse()
        => Assert.False(_service.CanReveal(Path.Combine(_root, "missing", "gone.mp4")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CanReveal_NoPath_IsFalse(string? path)
        => Assert.False(_service.CanReveal(path));
}
