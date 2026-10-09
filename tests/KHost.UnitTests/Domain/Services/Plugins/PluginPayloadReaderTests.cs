using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Plugins;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginPayloadReaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-payload-test-");
    private readonly PluginPayloadReader _reader = new();

    public void Dispose()
    {
        try { _root.Delete(recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Both ends of this host's range, once each: today they are the same version.
    public static TheoryData<int> EndsOfThisHostsRange()
    {
        var data = new TheoryData<int>();

        foreach (var end in new[] { PluginApi.MinimumVersion, PluginApi.CurrentVersion }.Distinct())
            data.Add(end);

        return data;
    }

    [Theory]
    [MemberData(nameof(EndsOfThisHostsRange))]
    public void Unpack_BuiltAgainstAnApiThisHostRuns_ReturnsTheManifest(int apiVersion)
        => Assert.Equal(apiVersion, _reader.Unpack(BuildZip(apiVersion), Destination()).Manifest.ApiVersion);

    [Fact]
    public void Unpack_BuiltAgainstANewerApi_RefusesSayingKHostNeedsTheUpdate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.Unpack(BuildZip(PluginApi.CurrentVersion + 1), Destination()));

        Assert.Contains("Needs a newer KHost", error.Message);
    }

    [Fact]
    public void Unpack_BuiltAgainstAnOlderApi_RefusesSayingThePluginNeedsTheUpdate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.Unpack(BuildZip(PluginApi.MinimumVersion - 1), Destination()));

        Assert.Contains("it needs an update", error.Message);
    }

    [Fact]
    public void UnpackForCatalog_BuiltAgainstANewerApi_ReturnsTheManifest()
        => Assert.Equal(PluginApi.CurrentVersion + 5, _reader.UnpackForCatalog(BuildZip(PluginApi.CurrentVersion + 5), Destination()).Manifest.ApiVersion);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnpackForCatalog_ApiBelowOne_Refuses(int apiVersion)
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.UnpackForCatalog(BuildZip(apiVersion), Destination()));

        Assert.Contains("start at 1", error.Message);
    }

    [Fact]
    public void UnpackForCatalog_EntryAssemblyMissing_StillRefuses()
        => Assert.Throws<InvalidOperationException>(() => _reader.UnpackForCatalog(BuildZip(PluginApi.CurrentVersion + 1, includeEntryAssembly: false), Destination()));

    private string Destination() => Path.Combine(_root.FullName, Guid.NewGuid().ToString("N"));

    private string BuildZip(int apiVersion, bool includeEntryAssembly = true)
    {
        var path = Path.Combine(_root.FullName, $"{Guid.NewGuid():N}.zip");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var manifest = new PluginManifest
            {
                Id = Guid.NewGuid(),
                Name = "Test Plugin",
                Version = "1.0.0",
                EntryAssembly = "Test.dll",
                ApiVersion = apiVersion,
            };

            Write(archive, PluginLoader.ManifestFileName, JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web));

            if (includeEntryAssembly)
                Write(archive, "Test.dll", "not really an assembly");
        }

        return path;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();

        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
