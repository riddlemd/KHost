using System.Text.RegularExpressions;

namespace KHost.UnitTests.Conventions;

/// <summary>No test clears SQLite's connection pools.</summary>
/// <remarks><c>SqliteConnection.ClearAllPools()</c> is process-wide: it disposes the native handle under
/// a connection another test is using in parallel, and that test fails with an ObjectDisposedException
/// on <c>SQLitePCL.sqlite3</c>, a different one each run. Open a file database with
/// <c>Pooling=False</c> to delete it afterwards.</remarks>
public partial class NoSqlitePoolClearingTests
{
    [Fact]
    public void TestSources_NeverClearSqlitePools()
    {
        var testsRoot = Path.Combine(RepositoryRoot(), "tests");

        var offenders = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => PoolClearCall().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(testsRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"\bClear(All)?Pools?\s*\([^)]*\)\s*;")]
    private static partial Regex PoolClearCall();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }
}
