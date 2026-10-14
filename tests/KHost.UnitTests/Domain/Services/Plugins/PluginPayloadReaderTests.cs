using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;
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

    /// <summary>Requirements this host runs: its own version, its own major's first release, and the
    /// legacy 0.61 surface every released plugin was built against.</summary>
    public static TheoryData<string> ContractsThisHostRuns()
    {
        var host = ContractsVersion.ThisHost;

        return new TheoryData<string>(new[] { host.ToString(), $"{host.Major}.0.0", "0.61.0", "0.61.3" }.Distinct());
    }

    [Theory]
    [MemberData(nameof(ContractsThisHostRuns))]
    public void Unpack_NeedsContractsThisHostHas_ReturnsTheManifest(string contracts)
        => Assert.Equal("Test Plugin", _reader.Unpack(BuildZip(contracts), Destination()).Manifest.Name);

    [Fact]
    public void Unpack_NeedsANewerMinor_RefusesSayingKHostNeedsTheUpdate()
    {
        var host = ContractsVersion.ThisHost;

        var error = Assert.Throws<InvalidOperationException>(() => _reader.Unpack(BuildZip($"{host.Major}.{host.Minor + 1}.0"), Destination()));

        Assert.Contains("Needs a newer KHost", error.Message);
    }

    [Fact]
    public void Unpack_NeedsAnOlderMajor_RefusesSayingThePluginNeedsTheUpdate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.Unpack(BuildZip("0.60.0"), Destination()));

        Assert.Contains("it needs an update", error.Message);
    }

    [Fact]
    public void Unpack_NoDepsJsonButLegacyApiVersion8_ReturnsTheManifest()
        => Assert.Equal(8, _reader.Unpack(BuildZip(contracts: null, apiVersion: 8), Destination()).Manifest.ApiVersion);

    [Fact]
    public void Unpack_NoDepsJsonAndAnOlderLegacyApiVersion_RefusesSayingThePluginNeedsTheUpdate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.Unpack(BuildZip(contracts: null, apiVersion: 7), Destination()));

        Assert.Contains("it needs an update", error.Message);
    }

    [Fact]
    public void Unpack_DepsJsonNamesNewerContracts_RefusesEvenWithLegacyApiVersion8()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.Unpack(BuildZip($"{ContractsVersion.ThisHost.Major + 1}.0.0", apiVersion: 8), Destination()));

        Assert.Contains("Needs a newer KHost", error.Message);
    }

    [Fact]
    public void UnpackForCatalog_NeedsANewerMajor_ReturnsTheManifest()
        => Assert.Equal("Test Plugin", _reader.UnpackForCatalog(BuildZip($"{ContractsVersion.ThisHost.Major + 1}.0.0"), Destination()).Manifest.Name);

    [Fact]
    public void UnpackForCatalog_NamesNoContractsAtAll_Refuses()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _reader.UnpackForCatalog(BuildZip(contracts: null), Destination()));

        Assert.Contains("Names no contracts version", error.Message);
    }

    [Fact]
    public void UnpackForCatalog_EntryAssemblyMissing_StillRefuses()
        => Assert.Throws<InvalidOperationException>(() => _reader.UnpackForCatalog(BuildZip($"{ContractsVersion.ThisHost.Major + 1}.0.0", includeEntryAssembly: false), Destination()));

    private string Destination() => Path.Combine(_root.FullName, Guid.NewGuid().ToString("N"));

    private string BuildZip(string? contracts, int? apiVersion = null, bool includeEntryAssembly = true)
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

            if (contracts is not null)
                Write(archive, "Test.deps.json", PluginDepsJson.For(contracts));

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
