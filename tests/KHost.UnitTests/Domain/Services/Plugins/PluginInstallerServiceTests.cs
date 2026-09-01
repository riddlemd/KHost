using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Plugins;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginInstallerServiceTests : IDisposable
{
    private static readonly Guid PluginId = Guid.Parse("0a000000-0000-4000-8000-0000000000b1");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-installer-test-");
    private readonly IMessageBroker _broker = new MessageBroker(NullLogger<MessageBroker>.Instance);

    private string StagingDir => Path.Combine(_root.FullName, "plugins-staging");

    public void Dispose()
    {
        try { _root.Delete(recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task InstallAsync_GoodPayload_StagesItForTheNextStart()
    {
        var zip = BuildZip();
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Staged, result.State);
        Assert.True(File.Exists(Path.Combine(StagingDir, PluginId.ToString(), PluginLoader.ManifestFileName)));
        Assert.Equal([PluginId], service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_GoodPayload_AnnouncesTheChange()
    {
        var raised = 0;

        using var subscription = _broker.Subscribe<PluginInstallsChanged>(_ => raised++);

        var zip = BuildZip();

        await BuildService(zip).InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.True(raised > 0);
    }

    [Fact]
    public async Task InstallAsync_ChecksumDoesNotMatch_FailsWithoutStaging()
    {
        var service = BuildService(BuildZip());

        var result = await service.InstallAsync(Entry(), Release("0000000000000000000000000000000000000000000000000000000000000000"));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Contains("checksum", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_ReleaseWithoutAChecksum_IsRefused()
    {
        var service = BuildService(BuildZip());

        var result = await service.InstallAsync(Entry(), Release(string.Empty));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_ReleaseServedOverPlainHttp_IsRefused()
    {
        var zip = BuildZip();
        var service = BuildService(zip);

        var release = Release(Sha256(zip));
        release.Url = "http://example.test/plugin.zip";

        var result = await service.InstallAsync(Entry(), release);

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_ManifestDeclaresAnotherPluginId_Fails()
    {
        var zip = BuildZip(id: Guid.Parse("0a000000-0000-4000-8000-0000000000ff"));
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Contains("declares plugin id", result.Error);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_ManifestTargetsAnotherApiVersion_Fails()
    {
        var zip = BuildZip(apiVersion: PluginApi.CurrentVersion + 1);
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Contains("plugin API", result.Error);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_EntryAssemblyMissingFromTheZip_Fails()
    {
        var zip = BuildZip(includeEntryAssembly: false);
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Contains("missing", result.Error);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_EntryAssemblyPointsOutsideTheFolder_Fails()
    {
        var zip = BuildZip(entryAssembly: "../escaped.dll");
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Contains("outside", result.Error);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public async Task InstallAsync_ZipEntryEscapesItsFolder_IsRefusedBeforeAnythingIsExtracted()
    {
        var zip = BuildZip(extraEntryName: "../escaped.txt");
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Failed, result.State);

        // This service's own message, not the one ExtractToDirectory throws: the entries are all
        // checked before a byte is written, so an escaping zip leaves nothing half-extracted.
        Assert.Contains("writes outside its folder", result.Error);
        Assert.Contains("escaped.txt", result.Error);
        Assert.False(Directory.Exists(Path.Combine(StagingDir, PluginPaths.WorkFolderName, PluginId.ToString())));
    }

    [Fact]
    public async Task InstallAsync_PayloadWrappedInOneFolder_StillStages()
    {
        var zip = BuildZip(wrapperFolder: "KHost.Plugins.Test-1.0.0");
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Staged, result.State);
        Assert.True(File.Exists(Path.Combine(StagingDir, PluginId.ToString(), PluginLoader.ManifestFileName)));
    }

    [Fact]
    public async Task InstallAsync_ZipWithoutAManifest_Fails()
    {
        var zip = BuildZip(includeManifest: false);
        var service = BuildService(zip);

        var result = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Failed, result.State);
        Assert.Contains(PluginLoader.ManifestFileName, result.Error);
    }

    [Fact]
    public async Task InstallAsync_ServerReturnsNotFound_Fails()
    {
        var service = BuildService([], HttpStatusCode.NotFound);

        var result = await service.InstallAsync(Entry(), Release("abc"));

        Assert.Equal(PluginInstallState.Failed, result.State);
    }

    [Fact]
    public async Task InstallAsync_LeavesNoScratchBehind()
    {
        var zip = BuildZip();

        await BuildService(zip).InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.False(Directory.Exists(Path.Combine(StagingDir, PluginPaths.WorkFolderName, PluginId.ToString())));
    }

    [Fact]
    public async Task InstallAsync_ReportsProgressAgainstTheContentLength()
    {
        var zip = BuildZip();
        var service = BuildService(zip);
        var seen = new List<double?>();

        using var subscription = _broker.Subscribe<PluginInstallsChanged>(
            _ => seen.Add(service.Snapshot().FirstOrDefault(i => i.PluginId == PluginId)?.Progress));

        await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Contains(seen, progress => progress is 1d);
    }

    [Fact]
    public async Task InstallAsync_AlreadyRunningForTheSameId_DoesNotStartASecond()
    {
        var zip = BuildZip();
        var gate = new SemaphoreSlim(0, 1);
        var service = BuildService(zip, gate: gate);

        var first = service.InstallAsync(Entry(), Release(Sha256(zip)));
        var second = await service.InstallAsync(Entry(), Release(Sha256(zip)));

        Assert.Equal(PluginInstallState.Downloading, second.State);

        gate.Release();

        Assert.Equal(PluginInstallState.Staged, (await first).State);
    }

    [Fact]
    public async Task Cancel_DownloadInFlight_SettlesAsCancelled()
    {
        var zip = BuildZip();
        var gate = new SemaphoreSlim(0, 1);
        var service = BuildService(zip, gate: gate);

        var install = service.InstallAsync(Entry(), Release(Sha256(zip)));

        service.Cancel(PluginId);
        gate.Release();

        Assert.Equal(PluginInstallState.Cancelled, (await install).State);
        Assert.Empty(service.Staged().Installs);
    }

    [Fact]
    public void MarkForRemoval_WritesAPendingRemoval()
    {
        var service = BuildService([]);

        service.MarkForRemoval(InstallFolder("youtube"));

        Assert.Equal(["youtube"], service.Staged().Removals);
    }

    [Fact]
    public void MarkForRemoval_TwoFoldersShareAnId_MarksOnlyTheOneNamed()
    {
        var service = BuildService([]);

        InstallFolder("youtube");
        service.MarkForRemoval(InstallFolder("youtube-copy"));

        Assert.Equal(["youtube-copy"], service.Staged().Removals);
    }

    [Fact]
    public void ClearRemoval_TwoFoldersShareAnId_ClearsOnlyTheOneNamed()
    {
        var service = BuildService([]);

        service.MarkForRemoval(InstallFolder("youtube"));
        service.MarkForRemoval(InstallFolder("youtube-copy"));
        service.ClearRemoval("youtube-copy");

        Assert.Equal(["youtube"], service.Staged().Removals);
    }

    [Fact]
    public async Task MarkForRemoval_PayloadAlreadyStaged_DropsTheStagedInstall()
    {
        var zip = BuildZip();
        var service = BuildService(zip);

        await service.InstallAsync(Entry(), Release(Sha256(zip)));
        service.MarkForRemoval(InstallFolder("youtube"));

        var staged = service.Staged();

        Assert.Empty(staged.Installs);
        Assert.Equal(["youtube"], staged.Removals);
    }

    [Fact]
    public async Task InstallAsync_PendingRemovalForTheSameId_ClearsIt()
    {
        var zip = BuildZip();
        var service = BuildService(zip);

        service.MarkForRemoval(InstallFolder("youtube"));
        await service.InstallAsync(Entry(), Release(Sha256(zip)));

        var staged = service.Staged();

        Assert.Empty(staged.Removals);
        Assert.Equal([PluginId], staged.Installs);
    }

    [Fact]
    public async Task ClearStaged_StagedInstall_RemovesIt()
    {
        var zip = BuildZip();
        var service = BuildService(zip);

        await service.InstallAsync(Entry(), Release(Sha256(zip)));
        service.ClearStaged(PluginId);

        Assert.True(service.Staged().IsEmpty);
    }

    [Fact]
    public void ClearStaged_PendingRemoval_RemovesIt()
    {
        var service = BuildService([]);

        service.MarkForRemoval(InstallFolder("youtube"));
        service.ClearStaged(PluginId);

        Assert.True(service.Staged().IsEmpty);
    }

    /// <summary>An installed plugin folder. A removal names a folder, and the id it carries is only
    /// readable from the manifest inside.</summary>
    private string InstallFolder(string folderName, Guid? id = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root.FullName, "plugins", folderName));

        var manifest = new PluginManifest
        {
            Id = id ?? PluginId,
            Name = "Test Plugin",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
        };

        File.WriteAllText(
            Path.Combine(directory.FullName, PluginLoader.ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web));

        return folderName;
    }

    private PluginInstallerService BuildService(byte[] payload, HttpStatusCode status = HttpStatusCode.OK, SemaphoreSlim? gate = null)
    {
        var handler = new StubHandler(status, payload, gate);
        var factory = Substitute.For<IHttpClientFactory>();

        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new PluginInstallerService(
            NullLogger<PluginInstallerService>.Instance,
            factory,
            new PluginStagingArea(Path.Combine(_root.FullName, "plugins"), StagingDir),
            new PluginPayloadReader(),
            TimeProvider.System,
            _broker);
    }

    private static PluginCatalogEntry Entry() => new() { Id = PluginId, Name = "Test Plugin" };

    private static PluginCatalogRelease Release(string sha256) => new()
    {
        Version = "1.0.0",
        ApiVersion = PluginApi.CurrentVersion,
        Url = "https://example.test/plugin.zip",
        Sha256 = sha256,
    };

    private static string Sha256(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private static byte[] BuildZip(
        Guid? id = null,
        int? apiVersion = null,
        string entryAssembly = "Test.dll",
        bool includeManifest = true,
        bool includeEntryAssembly = true,
        string? wrapperFolder = null,
        string? extraEntryName = null)
    {
        var prefix = wrapperFolder is null ? string.Empty : wrapperFolder + "/";

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeManifest)
            {
                var manifest = new PluginManifest
                {
                    Id = id ?? PluginId,
                    Name = "Test Plugin",
                    Version = "1.0.0",
                    EntryAssembly = entryAssembly,
                    ApiVersion = apiVersion ?? PluginApi.CurrentVersion,
                };

                Write(archive, prefix + PluginLoader.ManifestFileName, JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web));
            }

            if (includeEntryAssembly)
                Write(archive, prefix + "Test.dll", "not really an assembly");

            if (extraEntryName is not null)
                Write(archive, extraEntryName, "escaped");
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();

        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] payload, SemaphoreSlim? gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Holds the response open so a test can observe or cancel a download mid-flight.
            if (gate is not null)
                await gate.WaitAsync(cancellationToken);

            return new HttpResponseMessage(status) { Content = new ByteArrayContent(payload) };
        }
    }
}
