using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class FileScreenKeyStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"khost-screens-{Guid.NewGuid():n}");
    private readonly FileScreenKeyStore _store;

    public FileScreenKeyStoreTests()
        => _store = new FileScreenKeyStore(_root, NullLogger<FileScreenKeyStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Provision_ThenGetKey_RoundTripsTheSameKey()
    {
        var path = _store.Provision("Screen 1");

        Assert.True(File.Exists(path));
        var key = _store.GetKey("Screen 1");

        Assert.NotNull(key);
        Assert.Equal(32, key!.Length);
    }

    [Fact]
    public void GetKey_ForAScreenWithNoKey_IsNull()
        => Assert.Null(_store.GetKey("never-provisioned"));

    [Fact]
    public void Provision_ReplacesAnExistingKey_SoARelaunchReKeys()
    {
        _store.Provision("Screen 1");
        var first = _store.GetKey("Screen 1")!;

        _store.Provision("Screen 1");
        var second = _store.GetKey("Screen 1")!;

        Assert.NotEqual(Convert.ToBase64String(first), Convert.ToBase64String(second));
    }

    [Fact]
    public void Revoke_RemovesTheKey()
    {
        _store.Provision("Screen 1");
        _store.Revoke("Screen 1");

        Assert.Null(_store.GetKey("Screen 1"));
    }

    [Fact]
    public void ScreenIdWithPathTraversal_StaysInsideTheKeystore()
    {
        // A screen names its own id; a crafted one must not write or read outside the keystore. The
        // returned path is the direct proof — it is hashed to a safe filename inside the root, so it
        // cannot escape, whatever separators the id carried.
        var path = Path.GetFullPath(_store.Provision("../../evil"));
        var rootPrefix = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;

        Assert.StartsWith(rootPrefix, path);
        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(path));

        // And it still round-trips, hashed to that safe filename.
        Assert.NotNull(_store.GetKey("../../evil"));
    }
}
