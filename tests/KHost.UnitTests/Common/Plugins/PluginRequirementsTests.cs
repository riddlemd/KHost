using System.Text;
using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Common.Plugins;

public class PluginRequirementsTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("khost-requirements-test-");

    public void Dispose()
    {
        try { _folder.Delete(recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void ReadDepsJson_NamesKHostAbstractions_IsThatVersion()
        => Assert.Equal(new ContractsVersion(1, 2, 3), Read(PluginDepsJson.For("1.2.3")));

    [Fact]
    public void ReadDepsJson_APrereleasePackage_KeepsTheLabel()
        => Assert.Equal(ContractsVersion.Parse("1.3.0-beta.1"), Read(PluginDepsJson.For("1.3.0-beta.1")));

    [Fact]
    public void ReadDepsJson_NoKHostAbstractionsLibrary_IsNull()
        => Assert.Null(Read(PluginDepsJson.For(null)));

    [Fact]
    public void ReadDepsJson_KHostAbstractionsOnlyAsADependencyInTargets_IsNull()
        => Assert.Null(Read("""{ "targets": { "x": { "P/1.0.0": { "dependencies": { "KHost.Abstractions": "1.0.0" } } } }, "libraries": {} }"""));

    [Fact]
    public void ReadDepsJson_KHostAbstractionsWithAnUnreadableVersion_IsNull()
        => Assert.Null(Read("""{ "libraries": { "KHost.Abstractions/1.0": { "type": "package" } } }"""));

    [Fact]
    public void ReadDepsJson_ASimilarlyNamedPackageListedFirst_IsNotTakenForTheContracts()
        => Assert.Equal(
            new ContractsVersion(1, 0, 0),
            Read("""{ "libraries": { "KHost.AbstractionsExtra/2.0.0": { "type": "package" }, "KHost.Abstractions/1.0.0": { "type": "package" } } }"""));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "libraries": [] }""")]
    [InlineData("")]
    public void ReadDepsJson_Malformed_IsNull(string text)
        => Assert.Null(Read(text));

    [Fact]
    public void FromLegacyApiVersion_8_IsTheLegacyContracts()
        => Assert.Equal(new ContractsVersion(0, 61, 0), PluginRequirements.FromLegacyApiVersion(8));

    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    [InlineData(9)]
    public void FromLegacyApiVersion_AnythingElse_IsNull(int? apiVersion)
        => Assert.Null(PluginRequirements.FromLegacyApiVersion(apiVersion));

    [Fact]
    public void ReadFromFolder_DepsJsonPresent_WinsOverTheLegacyApiVersion()
    {
        WriteDeps(PluginDepsJson.For("2.1.0"));

        Assert.Equal(new ContractsVersion(2, 1, 0), PluginRequirements.ReadFromFolder(_folder.FullName, Manifest(apiVersion: 8)));
    }

    [Fact]
    public void ReadFromFolder_DepsJsonMissing_FallsBackToTheLegacyApiVersion()
        => Assert.Equal(ContractsVersion.Legacy, PluginRequirements.ReadFromFolder(_folder.FullName, Manifest(apiVersion: 8)));

    [Fact]
    public void ReadFromFolder_DepsJsonNamesNoContracts_FallsBackToTheLegacyApiVersion()
    {
        WriteDeps(PluginDepsJson.For(null));

        Assert.Equal(ContractsVersion.Legacy, PluginRequirements.ReadFromFolder(_folder.FullName, Manifest(apiVersion: 8)));
    }

    [Fact]
    public void ReadFromFolder_DepsJsonMalformed_FallsBackToTheLegacyApiVersion()
    {
        WriteDeps("{ broken");

        Assert.Equal(ContractsVersion.Legacy, PluginRequirements.ReadFromFolder(_folder.FullName, Manifest(apiVersion: 8)));
    }

    [Fact]
    public void ReadFromFolder_NeitherDepsJsonNorApiVersion_IsNull()
        => Assert.Null(PluginRequirements.ReadFromFolder(_folder.FullName, Manifest(apiVersion: null)));

    [Fact]
    public void ReadFromFolder_EntryAssemblyInASubfolder_ReadsTheDepsJsonBesideIt()
    {
        Directory.CreateDirectory(Path.Combine(_folder.FullName, "lib"));
        File.WriteAllText(Path.Combine(_folder.FullName, "lib", "Plugin.deps.json"), PluginDepsJson.For("1.1.0"));

        Assert.Equal(new ContractsVersion(1, 1, 0), PluginRequirements.ReadFromFolder(_folder.FullName, Manifest(apiVersion: null, entryAssembly: "lib/Plugin.dll")));
    }

    [Fact]
    public void DescribeRefusal_ARequirementTheHostCovers_IsNull()
        => Assert.Null(new ContractsVersion(1, 0, 0).DescribeRefusal(ContractsVersion.Legacy, legacyApiVersion: null));

    [Fact]
    public void DescribeRefusal_NoRequirementAndNoApiVersion_SaysNoContractsAreNamed()
        => Assert.StartsWith("Names no contracts version", new ContractsVersion(1, 0, 0).DescribeRefusal(null, legacyApiVersion: null));

    [Fact]
    public void DescribeRefusal_NoRequirementAndAnOlderApiVersion_SaysThePluginNeedsTheUpdate()
        => Assert.EndsWith("it needs an update.", new ContractsVersion(1, 0, 0).DescribeRefusal(null, legacyApiVersion: 7));

    [Fact]
    public void DescribeRefusal_NoRequirementAndANewerApiVersion_SaysKHostNeedsTheUpdate()
        => Assert.StartsWith("Needs a newer KHost", new ContractsVersion(1, 0, 0).DescribeRefusal(null, legacyApiVersion: 9));

    private static ContractsVersion? Read(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        return PluginRequirements.ReadDepsJson(stream);
    }

    private void WriteDeps(string text) => File.WriteAllText(Path.Combine(_folder.FullName, "Plugin.deps.json"), text);

    private static PluginManifest Manifest(int? apiVersion, string entryAssembly = "Plugin.dll") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Plugin",
        Version = "1.0.0",
        EntryAssembly = entryAssembly,
        ApiVersion = apiVersion,
    };
}
