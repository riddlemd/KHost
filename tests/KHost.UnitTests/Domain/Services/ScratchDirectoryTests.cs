using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class ScratchDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "khost-scratch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TryDelete_RemovesTheFolderAndItsContents()
    {
        var nested = Path.Combine(_root, "a", "b");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "file.txt"), "x");

        ScratchDirectory.TryDelete(_root);

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void TryDelete_MissingFolder_DoesNothing()
    {
        ScratchDirectory.TryDelete(_root);

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void TryDelete_LeavesSiblingsAlone()
    {
        var doomed = Path.Combine(_root, "doomed");
        var kept = Path.Combine(_root, "kept");
        Directory.CreateDirectory(doomed);
        Directory.CreateDirectory(kept);

        ScratchDirectory.TryDelete(doomed);

        Assert.False(Directory.Exists(doomed));
        Assert.True(Directory.Exists(kept));
    }

    [Fact]
    public void TryDelete_AFileInsteadOfAFolder_IsNotDeletedAndDoesNotThrow()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "plain.txt");
        File.WriteAllText(file, "x");

        ScratchDirectory.TryDelete(file);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void TryDelete_AFolderThatRefusesToGo_DoesNotThrow()
    {
        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        var inside = Path.Combine(locked, "held.txt");
        File.WriteAllText(inside, "x");

        // Windows refuses to delete a file another handle holds; elsewhere a read-only folder
        // refuses to give up its entries.
        using var hold = OperatingSystem.IsWindows() ? new FileStream(inside, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var exception = Record.Exception(() => ScratchDirectory.TryDelete(locked));

            Assert.Null(exception);
            Assert.True(File.Exists(inside));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
