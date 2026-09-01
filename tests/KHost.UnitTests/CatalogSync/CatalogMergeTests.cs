using KHost.Abstractions.Models.Plugins;
using KHost.CatalogSync;

namespace KHost.UnitTests.CatalogSync;

public class CatalogMergeTests
{
    private static readonly Guid PluginId = Guid.Parse("0a000000-0000-4000-8000-0000000000d1");
    private static readonly Guid OtherId = Guid.Parse("0a000000-0000-4000-8000-0000000000d2");

    [Fact]
    public void Apply_PluginNotListed_AddsItFromThePayload()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        var entry = Assert.Single(catalog.Plugins);

        Assert.Equal(PluginId, entry.Id);
        Assert.Equal("YouTube Search", entry.Name);
        Assert.Equal("https://github.com/riddlemd/KHost.Plugins.YouTube", entry.Repository);
        Assert.Equal("1.0.0", Assert.Single(entry.Releases).Version);
    }

    [Fact]
    public void Apply_NewRelease_KeepsTheOlderOne()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0"));
        CatalogMerge.Apply(catalog, Facts("1.1.0"));

        Assert.Equal(["1.1.0", "1.0.0"], Assert.Single(catalog.Plugins).Releases.Select(r => r.Version));
    }

    [Fact]
    public void Apply_ReleasesOutOfOrder_ListsThemNewestFirst()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.9.0"));
        CatalogMerge.Apply(catalog, Facts("1.10.0"));
        CatalogMerge.Apply(catalog, Facts("1.2.0"));

        Assert.Equal(["1.10.0", "1.9.0", "1.2.0"], Assert.Single(catalog.Plugins).Releases.Select(r => r.Version));
    }

    [Fact]
    public void Apply_TwoVersionsThatParseAlike_StillOrdersThemTheSameWayEveryRun()
    {
        // "1.0" and "1.0.0" are distinct strings that parse to one version, so the version compare
        // ties and List.Sort is free to order them either way. Without a tiebreak a re-sync could
        // reshuffle them and produce a diff for nothing.
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0"));
        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        Assert.Equal(["1.0.0", "1.0"], Assert.Single(catalog.Plugins).Releases.Select(r => r.Version));

        var first = Snapshot(catalog);

        CatalogMerge.Apply(catalog, Facts("1.0"));

        Assert.Equal(first, Snapshot(catalog));
    }

    [Fact]
    public void Apply_SameVersionTwice_ReplacesRatherThanDuplicates()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0", sha256: "aaa"));
        CatalogMerge.Apply(catalog, Facts("1.0.0", sha256: "bbb"));

        var release = Assert.Single(Assert.Single(catalog.Plugins).Releases);

        Assert.Equal("bbb", release.Sha256);
    }

    [Fact]
    public void Apply_SameVersionForTwoPlatforms_KeepsBoth()
    {
        // The whole point of the rid: one version can ship a neutral build and a per-platform one.
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0"));
        CatalogMerge.Apply(catalog, Facts("1.0.0", rid: "win"));

        var releases = Assert.Single(catalog.Plugins).Releases;

        Assert.Equal(2, releases.Count);
        Assert.Equal([null, "win"], releases.Select(r => r.Rid).OrderBy(r => r ?? string.Empty));
    }

    [Fact]
    public void Apply_SameVersionAndPlatformTwice_ReplacesRatherThanDuplicates()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0", rid: "win", sha256: "aaa"));
        CatalogMerge.Apply(catalog, Facts("1.0.0", rid: "win", sha256: "bbb"));

        var release = Assert.Single(Assert.Single(catalog.Plugins).Releases);

        Assert.Equal("bbb", release.Sha256);
    }

    [Fact]
    public void Apply_TwoPlatformsAtOneVersion_OrdersThemTheSameWayEveryRun()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0", rid: "win"));
        CatalogMerge.Apply(catalog, Facts("1.0.0", rid: "linux"));

        var first = Snapshot(catalog);

        CatalogMerge.Apply(catalog, Facts("1.0.0", rid: "win"));

        Assert.Equal(first, Snapshot(catalog));
    }

    [Fact]
    public void Apply_RerunningTheSameRelease_ChangesNothing()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        var first = Snapshot(catalog);

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        Assert.Equal(first, Snapshot(catalog));
    }

    [Fact]
    public void Apply_EntryAlreadyCurated_DoesNotOverwriteTheEditedFields()
    {
        var catalog = Catalog();

        catalog.Plugins.Add(new PluginCatalogEntry
        {
            Id = PluginId,
            Name = "YouTube",
            Description = "A description someone wrote for the browse list.",
            Author = "riddlemd",
            Repository = "https://example.test/mirror",
            Capabilities = ["Media provider"],
        });

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        var entry = Assert.Single(catalog.Plugins);

        Assert.Equal("YouTube", entry.Name);
        Assert.Equal("A description someone wrote for the browse list.", entry.Description);
        Assert.Equal("https://example.test/mirror", entry.Repository);
        Assert.Equal(["Media provider"], entry.Capabilities);
    }

    [Fact]
    public void Apply_EntryMissingAField_FillsItFromThePayload()
    {
        var catalog = Catalog();

        catalog.Plugins.Add(new PluginCatalogEntry { Id = PluginId, Name = "YouTube" });

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        var entry = Assert.Single(catalog.Plugins);

        Assert.Equal("YouTube", entry.Name);
        Assert.Equal("Search YouTube for karaoke videos.", entry.Description);
    }

    [Fact]
    public void Apply_AnotherPluginListed_LeavesItAlone()
    {
        var catalog = Catalog();

        catalog.Plugins.Add(new PluginCatalogEntry { Id = OtherId, Name = "Spotify Break Music" });

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        Assert.Equal(2, catalog.Plugins.Count);
        Assert.Empty(catalog.Plugins.Single(p => p.Id == OtherId).Releases);
    }

    [Fact]
    public void Apply_SeveralPlugins_SortsThemByName()
    {
        var catalog = Catalog();

        catalog.Plugins.Add(new PluginCatalogEntry { Id = OtherId, Name = "Zebra" });

        CatalogMerge.Apply(catalog, Facts("1.0.0"));

        Assert.Equal(["YouTube Search", "Zebra"], catalog.Plugins.Select(p => p.Name));
    }

    [Fact]
    public void Apply_CapabilitiesGiven_RecordsThemOnANewEntry()
    {
        var catalog = Catalog();

        CatalogMerge.Apply(catalog, Facts("1.0.0") with { Capabilities = ["Media provider"] });

        Assert.Equal(["Media provider"], Assert.Single(catalog.Plugins).Capabilities);
    }

    private static PluginCatalog Catalog() => new() { SchemaVersion = PluginCatalog.SupportedSchemaVersion };

    private static string Snapshot(PluginCatalog catalog)
        => System.Text.Json.JsonSerializer.Serialize(catalog, System.Text.Json.JsonSerializerOptions.Web);

    private static SyncFacts Facts(string version, string sha256 = "abc123", string? rid = null) => new()
    {
        Id = PluginId,
        Name = "YouTube Search",
        Author = "riddlemd",
        Description = "Search YouTube for karaoke videos.",
        Repository = "https://github.com/riddlemd/KHost.Plugins.YouTube",
        Release = new PluginCatalogRelease
        {
            Version = version,
            ApiVersion = 1,
            Url = $"https://example.test/p-{version}.zip",
            Sha256 = sha256,
            SizeBytes = 26934,
            Rid = rid,
        },
    };
}
