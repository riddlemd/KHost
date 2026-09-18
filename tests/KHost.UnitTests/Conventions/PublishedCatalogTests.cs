using KHost.Abstractions.Models.Plugins;
using System.Text.Json;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Conventions;

// The catalog at the repo root is served raw from master and read by every installed host, so a
// bad edit is live the moment it merges. There is no build between it and the Available tab.
public class PublishedCatalogTests
{
    private const string CatalogFileName = "plugin-catalog.json";

    [Fact]
    public void Catalog_IsReadableByTheModelTheHostDeserialisesInto()
        => Assert.NotNull(Read());

    [Fact]
    public void Catalog_SchemaVersion_IsTheOneThisHostReads()
    {
        // The worst failure in the file: an unknown schema version makes the host refuse the whole
        // document, so a wrong number here empties the browse list rather than spoiling one row.
        Assert.Equal(PluginCatalog.SupportedSchemaVersion, Read().SchemaVersion);
    }

    [Fact]
    public void CatalogEntries_HaveAnIdThatCouldMatchAnInstalledPlugin()
    {
        var offenders = Read().Plugins
            .Where(entry => entry.Id == Guid.Empty)
            .Select(entry => Describe(entry))
            .ToArray();

        Assert.True(offenders.Length == 0, Message("Entries need an id matching their manifest's", offenders));
    }

    [Fact]
    public void CatalogEntries_DoNotRepeatAPluginId()
    {
        // The browse list is keyed by id; two rows sharing one is a duplicate @key at render time.
        var offenders = Read().Plugins
            .GroupBy(entry => entry.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({group.Count()} entries)")
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Message("Plugin ids must be unique", offenders));
    }

    [Fact]
    public void CatalogEntries_HaveANameAndSomethingToOffer()
    {
        var offenders = Read().Plugins
            .Where(entry => string.IsNullOrWhiteSpace(entry.Name) || entry.Releases.Count == 0)
            .Select(Describe)
            .ToArray();

        Assert.True(offenders.Length == 0, Message("Entries need a name and at least one release", offenders));
    }

    /// <summary>The catalog lists what the current host can install, and nothing else.</summary>
    /// <remarks>`LatestCompatibleRelease` matches the api version exactly, so an entry the gate has
    /// passed by is one no host will ever select again: the Available tab reads "Not compatible"
    /// and there is nothing to install. This has shipped twice, on the move to api 2 and again on
    /// the move to 3, because nothing tied the published file to the number the host enforces.
    /// Moving `PluginApi.CurrentVersion` is therefore meant to fail here until every entry has been
    /// rebuilt, re-released and the superseded one removed.</remarks>
    [Fact]
    public void CatalogReleases_AllDeclareTheApiVersionThisHostEnforces()
    {
        var offenders = Read().Plugins
            .SelectMany(entry => entry.Releases.Select(release => (entry, release)))
            .Where(pair => pair.release.ApiVersion != PluginApi.CurrentVersion)
            .Select(pair => $"{Describe(pair.entry)} v{pair.release.Version} declares api {pair.release.ApiVersion}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            Message($"Every published release must declare api {PluginApi.CurrentVersion}, which is what this host installs", offenders));
    }

    [Fact]
    public void CatalogReleases_CanAllActuallyBeInstalled()
    {
        // Not just the newest: a release the host refuses is dead weight the page still has to
        // explain, and IsInstallable is what decides whether it is offered at all.
        var offenders = Read().Plugins
            .SelectMany(entry => entry.Releases.Select(release => (entry, release)))
            .Where(pair => !pair.release.IsInstallable
                        || pair.release.ApiVersion <= 0
                        || PluginVersion.Parse(pair.release.Version) == new Version(0, 0, 0, 0))
            .Select(pair => $"{Describe(pair.entry)} v{pair.release.Version} " +
                            $"(api {pair.release.ApiVersion}, url '{pair.release.Url}', sha256 '{pair.release.Sha256}')")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            Message("Releases need a readable version, a positive api version, an https url and a checksum", offenders));
    }

    [Fact]
    public void CatalogReleases_PublishAFullSha256()
    {
        var offenders = Read().Plugins
            .SelectMany(entry => entry.Releases.Select(release => (entry, release)))
            .Where(pair => !IsSha256(pair.release.Sha256))
            .Select(pair => $"{Describe(pair.entry)} v{pair.release.Version} — '{pair.release.Sha256}'")
            .ToArray();

        Assert.True(offenders.Length == 0, Message("Checksums must be 64 hex characters", offenders));
    }

    [Fact]
    public void CatalogReleases_NameAPlatformTheHostRecognises()
    {
        // A rid this host cannot parse matches nothing, so the release reads as "not for this
        // platform" on every machine, a failure that looks like a deliberate omission.
        var offenders = Read().Plugins
            .SelectMany(entry => entry.Releases.Select(release => (entry, release)))
            .Where(pair => !PluginRid.IsKnown(pair.release.Rid))
            .Select(pair => $"{Describe(pair.entry)} v{pair.release.Version} — rid '{pair.release.Rid}'")
            .ToArray();

        Assert.True(offenders.Length == 0, Message("Release platforms must be win, osx or linux", offenders));
    }

    [Fact]
    public void CatalogReleases_DoNotRepeatAVersionForOnePlatform()
    {
        // Two rows for the same version and platform means one is dead weight, and which one a
        // host installs depends on sort order rather than on anything a publisher decided.
        var offenders = Read().Plugins
            .SelectMany(entry => entry.Releases.Select(release => (entry, release)))
            .GroupBy(pair => (pair.entry.Id, pair.release.Version, Rid: pair.release.Rid ?? string.Empty))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Id} v{group.Key.Version} rid '{group.Key.Rid}' ({group.Count()} rows)")
            .ToArray();

        Assert.True(offenders.Length == 0, Message("A version may appear once per platform", offenders));
    }

    [Fact]
    public void CatalogEntries_LinkRepositoriesTheHostWillOpen()
    {
        // A link the host silently declines to open reads as a dead button, not as a bad url.
        var offenders = Read().Plugins
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Repository))
            .Where(entry => !Uri.TryCreate(entry.Repository, UriKind.Absolute, out var uri)
                         || uri.Scheme is not ("https" or "http"))
            .Select(entry => $"{Describe(entry)} — '{entry.Repository}'")
            .ToArray();

        Assert.True(offenders.Length == 0, Message("Repository links must be absolute http(s) urls", offenders));
    }

    private static PluginCatalog Read()
    {
        var file = FindCatalog();

        Assert.True(file.Exists, $"{CatalogFileName} is missing from the repository root ({file.FullName}).");

        // The host's own options, so a casing mismatch fails here rather than silently reading as
        // a default once it is live.
        return JsonSerializer.Deserialize<PluginCatalog>(File.ReadAllText(file.FullName), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{CatalogFileName} deserialised to null.");
    }

    // Walked up rather than hardcoded: the depth from the test binary to the root changes with
    // BaseOutputPath, which this repo's build redirects.
    private static FileInfo FindCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.GetFiles("KHost.slnx").Length > 0)
                return new FileInfo(Path.Combine(directory.FullName, CatalogFileName));
        }

        throw new InvalidOperationException(
            $"No KHost.slnx above {AppContext.BaseDirectory}, so the repository root could not be found.");
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Describe(PluginCatalogEntry entry)
        => string.IsNullOrWhiteSpace(entry.Name) ? entry.Id.ToString() : $"{entry.Name} ({entry.Id})";

    private static string Message(string problem, string[] offenders)
        => $"{problem} in {CatalogFileName}:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}";
}
