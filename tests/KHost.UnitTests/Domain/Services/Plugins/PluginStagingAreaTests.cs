using KHost.Domain.Services.Plugins;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using System.Text.Json;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginStagingAreaTests : IDisposable
{
    private static readonly Guid PluginId = Guid.Parse("0a000000-0000-4000-8000-0000000000a1");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-staging-test-");

    private string PluginsDir => Path.Combine(_root.FullName, "plugins");

    private string StagingDir => Path.Combine(_root.FullName, "plugins-staging");

    public void Dispose()
    {
        try { _root.Delete(recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void ApplyPending_StagingDirectoryMissing_DoesNothing()
    {
        Area().ApplyPending();

        Assert.False(Directory.Exists(PluginsDir));
    }

    [Fact]
    public void ApplyPending_StagedPayload_MovesItIntoThePluginsFolder()
    {
        WriteStaged(PluginId, "1.1.0");

        Area().ApplyPending();

        var landed = Path.Combine(PluginsDir, PluginId.ToString());

        Assert.True(File.Exists(Path.Combine(landed, PluginLoader.ManifestFileName)));
        Assert.False(Directory.Exists(Path.Combine(StagingDir, PluginId.ToString())));
    }

    [Fact]
    public void ApplyPending_StagedPayloadReplacesInstalled_RemovesTheOldFolderWhateverItsName()
    {
        WriteInstalled("hand-dropped-folder", PluginId, "1.0.0");
        WriteStaged(PluginId, "1.1.0");

        Area().ApplyPending();

        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "hand-dropped-folder")));
        Assert.Equal("1.1.0", ReadVersion(Path.Combine(PluginsDir, PluginId.ToString())));
    }

    [Fact]
    public void ApplyPending_RemovalMarker_DeletesTheInstalledFolderAndTheMarker()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteRemovalMarker("youtube");

        Area().ApplyPending();

        Assert.Empty(Directory.GetDirectories(PluginsDir));
        Assert.Empty(Directory.GetFiles(StagingDir));
    }

    [Fact]
    public void ApplyPending_RemovalMarkerForAPluginThatIsNotInstalled_ClearsTheMarker()
    {
        Directory.CreateDirectory(PluginsDir);
        WriteRemovalMarker("never-installed");

        Area().ApplyPending();

        Assert.Empty(Directory.GetFiles(StagingDir));
    }

    [Fact]
    public void ApplyPending_StagedInstallAndRemovalForTheSameId_KeepsTheInstall()
    {
        WriteInstalled("old", PluginId, "1.0.0");
        WriteRemovalMarker("old");
        WriteStaged(PluginId, "2.0.0");

        Area().ApplyPending();

        Assert.Equal("2.0.0", ReadVersion(Path.Combine(PluginsDir, PluginId.ToString())));
    }

    [Fact]
    public void ApplyPending_WorkFolder_IsLeftAlone()
    {
        var work = Path.Combine(StagingDir, PluginPaths.WorkFolderName);

        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "payload.zip"), "scratch");

        Area().ApplyPending();

        Assert.True(File.Exists(Path.Combine(work, "payload.zip")));
    }

    [Fact]
    public void ApplyPending_PayloadThatCannotBeMoved_IsSetAsideWithTheReason()
    {
        WriteStaged(PluginId, "1.0.0");

        // A file where the plugin folder must go: Directory.Move cannot replace it, and the id maps
        // to no installed folder, so nothing clears the way first.
        Directory.CreateDirectory(PluginsDir);
        File.WriteAllText(Path.Combine(PluginsDir, PluginId.ToString()), string.Empty);

        Area().ApplyPending();

        var failed = Path.Combine(StagingDir, PluginId + PluginPaths.FailureSuffix);

        Assert.True(Directory.Exists(failed));
        Assert.NotEmpty(File.ReadAllText(Path.Combine(failed, PluginPaths.FailureFileName)));
        Assert.False(Directory.Exists(Path.Combine(StagingDir, PluginId.ToString())));
    }

    [Fact]
    public void ApplyPending_InstalledFolderHasAnUnreadableManifest_StillInstalls()
    {
        var broken = Path.Combine(PluginsDir, "broken");

        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, PluginLoader.ManifestFileName), "{ not json");
        WriteStaged(PluginId, "1.0.0");

        Area().ApplyPending();

        Assert.Equal("1.0.0", ReadVersion(Path.Combine(PluginsDir, PluginId.ToString())));
    }

    [Fact]
    public void ApplyPending_PayloadAlreadySetAside_IsNotRetried()
    {
        var failed = Path.Combine(StagingDir, PluginId + PluginPaths.FailureSuffix);

        Directory.CreateDirectory(failed);
        File.WriteAllText(Path.Combine(failed, PluginPaths.FailureFileName), "went wrong");

        Area().ApplyPending();

        Assert.True(Directory.Exists(failed));
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, PluginId.ToString())));
    }

    [Fact]
    public void Read_StagingDirectoryMissing_ReturnsEmpty()
        => Assert.True(Area().Read().IsEmpty);

    [Fact]
    public void Read_StagedInstall_ReportsIt()
    {
        WriteStaged(PluginId, "1.0.0");

        var state = Area().Read();

        Assert.Equal([PluginId], state.Installs);
        Assert.Empty(state.Removals);
        Assert.False(state.IsEmpty);
    }

    [Fact]
    public void Read_RemovalMarker_ReportsIt()
    {
        WriteRemovalMarker("youtube");

        var state = Area().Read();

        Assert.Equal(["youtube"], state.Removals);
        Assert.Empty(state.Installs);
    }

    [Fact]
    public void Read_SetAsidePayload_ReportsTheFailureReason()
    {
        var failed = Path.Combine(StagingDir, PluginId + PluginPaths.FailureSuffix);

        Directory.CreateDirectory(failed);
        File.WriteAllText(Path.Combine(failed, PluginPaths.FailureFileName), "entry assembly missing");

        var state = Area().Read();

        Assert.Equal("entry assembly missing", state.Failures[PluginId]);
        Assert.Empty(state.Installs);
    }

    [Fact]
    public void Read_WorkFolder_IsNotAStagedInstall()
    {
        Directory.CreateDirectory(Path.Combine(StagingDir, PluginPaths.WorkFolderName));

        Assert.True(Area().Read().IsEmpty);
    }

    [Fact]
    public void ApplyPending_TwoFoldersShareAnId_RemovalTakesOnlyTheOneNamed()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteInstalled("youtube-copy", PluginId, "1.0.0");
        WriteRemovalMarker("youtube-copy");

        Area().ApplyPending();

        Assert.True(Directory.Exists(Path.Combine(PluginsDir, "youtube")));
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "youtube-copy")));
    }

    [Fact]
    public void ApplyPending_TwoFoldersShareAnId_AStagedInstallReplacesEveryCopy()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteInstalled("youtube-copy", PluginId, "1.0.0");
        WriteStaged(PluginId, "2.0.0");

        Area().ApplyPending();

        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "youtube")));
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "youtube-copy")));
        Assert.Equal("2.0.0", ReadVersion(Path.Combine(PluginsDir, PluginId.ToString())));
    }

    [Fact]
    public void ApplyPending_RemovalMarkerNamingAPathOutsideThePluginsFolder_DeletesNothing()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");

        var sibling = Directory.CreateDirectory(Path.Combine(_root.FullName, "not-plugins"));

        // ".." + the suffix: the only escaping name a single file name can carry.
        WriteRemovalMarker("..");

        Area().ApplyPending();

        Assert.True(sibling.Exists);
        Assert.True(Directory.Exists(Path.Combine(PluginsDir, "youtube")));
    }

    [Fact]
    public void MarkForRemoval_FolderNameThatEscapesThePluginsFolder_WritesNoMarker()
    {
        Directory.CreateDirectory(PluginsDir);

        Area().MarkForRemoval("..");

        Assert.Empty(Area().Read().Removals);
    }

    [Fact]
    public void MarkForRemoval_StagedInstallForTheSameId_DropsIt()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteStaged(PluginId, "2.0.0");

        Area().MarkForRemoval("youtube");

        var state = Area().Read();

        Assert.Empty(state.Installs);
        Assert.Equal(["youtube"], state.Removals);
    }

    [Fact]
    public void ClearRemoval_TwoCopiesPendingRemoval_ClearsOnlyTheOneNamed()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteInstalled("youtube-copy", PluginId, "1.0.0");
        WriteRemovalMarker("youtube");
        WriteRemovalMarker("youtube-copy");

        Area().ClearRemoval("youtube-copy");

        Assert.Equal(["youtube"], Area().Read().Removals);
    }

    [Fact]
    public void Clear_TwoCopiesPendingRemoval_ClearsBoth()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteInstalled("youtube-copy", PluginId, "1.0.0");
        WriteRemovalMarker("youtube");
        WriteRemovalMarker("youtube-copy");

        Area().Clear(PluginId);

        Assert.Empty(Area().Read().Removals);
    }

    [Fact]
    public void Stage_PendingRemovalOfAnotherCopy_ClearsIt()
    {
        WriteInstalled("youtube", PluginId, "1.0.0");
        WriteRemovalMarker("youtube");

        var payload = Path.Combine(_root.FullName, "payload");

        WriteManifest(payload, PluginId, "2.0.0");

        Area().Stage(payload, PluginId);

        var state = Area().Read();

        Assert.Empty(state.Removals);
        Assert.Equal([PluginId], state.Installs);
    }

    private PluginStagingArea Area() => new(PluginsDir, StagingDir);

    private void WriteStaged(Guid id, string version)
        => WriteManifest(Path.Combine(StagingDir, id.ToString()), id, version);

    private void WriteInstalled(string folderName, Guid id, string version)
        => WriteManifest(Path.Combine(PluginsDir, folderName), id, version);

    private void WriteRemovalMarker(string folderName)
    {
        Directory.CreateDirectory(StagingDir);
        File.WriteAllText(Path.Combine(StagingDir, folderName + PluginPaths.RemovalSuffix), string.Empty);
    }

    private static void WriteManifest(string directory, Guid id, string version)
    {
        Directory.CreateDirectory(directory);

        var manifest = new PluginManifest
        {
            Id = id,
            Name = "Test Plugin",
            Version = version,
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
        };

        File.WriteAllText(
            Path.Combine(directory, PluginLoader.ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web));
    }

    private static string? ReadVersion(string directory)
    {
        var json = File.ReadAllText(Path.Combine(directory, PluginLoader.ManifestFileName));

        return JsonSerializer.Deserialize<PluginManifest>(json, JsonSerializerOptions.Web)?.Version;
    }
}
