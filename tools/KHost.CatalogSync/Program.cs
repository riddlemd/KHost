using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Plugins;
using System.Text.Json;

namespace KHost.CatalogSync;

// Named rather than top-level: top-level statements emit a Program in the global namespace, and
// KHost.Screen2 already has one that the test project resolves against.
internal static class Program
{
    // Not async: the entry point is the one method that cannot take the Async suffix the rest of
    // the codebase enforces, so the awaiting happens one level down.
    private static int Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (Options.Parse(args) is not { } options)
        {
            Console.Error.WriteLine(Options.Usage);

            return 2;
        }

        var work = Directory.CreateTempSubdirectory("khost-catalog-sync-");

        using var http = new HttpClient();

        // GitHub rejects a request with no User-Agent outright.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KHost.CatalogSync/1.0 (+https://github.com/riddlemd/KHost)");

        try
        {
            return await SyncAsync(new GitHubClient(http), options, work.FullName);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<int> SyncAsync(GitHubClient github, Options options, string work)
    {
        Console.WriteLine($"Reading {options.Repository} {(options.Tag is null ? "(latest release)" : options.Tag)}…");

        var release = await github.ReadReleaseAsync(options.Repository, options.Tag, CancellationToken.None);

        if (release.Draft)
            return Fail("That release is still a draft, so its asset is not public.");

        if (release.Prerelease && !options.IncludePrerelease)
            return Fail("That release is a prerelease. Pass --include-prerelease to list it anyway.");

        if (!PluginRid.IsKnown(options.Rid))
            return Fail($"'{options.Rid}' is not a platform this host recognises; use win, osx or linux.");

        if (SelectAsset(release, options.Asset) is not { } asset)
            return Fail("Could not choose an asset. Name one with --asset.");

        if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            return Fail($"Asset '{asset.Name}' is in state '{asset.State}'; GitHub has not finished processing it.");

        Console.WriteLine($"  release {release.TagName}, asset {asset.Name} ({asset.Size:N0} bytes)");

        var zipPath = Path.Combine(work, asset.Name);
        var sha256 = await github.DownloadAsync(asset.BrowserDownloadUrl, zipPath, CancellationToken.None);

        Console.WriteLine($"  downloaded anonymously, sha256 {sha256}");

        // The digest is checked, never copied: GitHub recomputes it from whatever was uploaded, so
        // it proves transport integrity and nothing about what was reviewed.
        if (asset.Sha256FromDigest is { } published)
        {
            if (!published.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                return Fail($"GitHub publishes digest {published}, but the bytes hash to {sha256}.");

            Console.WriteLine("  matches the digest GitHub publishes");
        }
        else
        {
            Console.WriteLine("  GitHub publishes no digest for this asset; using the hash computed here");
        }

        // No expected id: the payload decides, so a catalog can never claim an id the plugin does
        // not have. That mismatch is otherwise only caught at install time.
        var manifest = new PluginPayloadReader().Unpack(zipPath, Path.Combine(work, "payload")).Manifest;

        Console.WriteLine($"  manifest: {manifest.Name} {manifest.Version}, id {manifest.Id}, api v{manifest.ApiVersion}");

        if (PluginVersion.Parse(manifest.Version) != PluginVersion.Parse(release.TagName))
            Console.WriteLine($"  warning: tag {release.TagName} and manifest version {manifest.Version} disagree");

        var catalogPath = Path.GetFullPath(options.CatalogPath);
        var catalog = Read(catalogPath);
        var before = Serialize(catalog);

        CatalogMerge.Apply(catalog, new SyncFacts
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Author = manifest.Author,
            Description = manifest.Description,
            Repository = $"https://github.com/{options.Repository}",
            Capabilities = options.Capabilities,
            Release = new PluginCatalogRelease
            {
                Version = manifest.Version,
                ApiVersion = manifest.ApiVersion,
                Url = asset.BrowserDownloadUrl,
                Sha256 = sha256,
                SizeBytes = asset.Size,
                Rid = options.Rid,
            },
        });

        var after = Serialize(catalog);

        if (before == after)
        {
            Console.WriteLine($"\n{Path.GetFileName(catalogPath)} already describes this release; nothing to do.");

            return 0;
        }

        File.WriteAllText(catalogPath, after);

        Console.WriteLine($"\nWrote {catalogPath}. Review the diff, then commit it — that commit is what a host trusts.");

        if (catalog.Plugins.Find(p => p.Id == manifest.Id)?.Capabilities.Count == 0)
            Console.WriteLine("Note: no capabilities recorded. The manifest does not carry them; pass --capabilities to set them.");

        return 0;
    }

    private static PluginCatalog Read(string path)
    {
        var fresh = new PluginCatalog { SchemaVersion = PluginCatalog.SupportedSchemaVersion };

        if (!File.Exists(path))
            return fresh;

        return JsonSerializer.Deserialize<PluginCatalog>(File.ReadAllText(path), JsonSerializerOptions.Web) ?? fresh;
    }

    private static string Serialize(PluginCatalog catalog)
        => JsonSerializer.Serialize(catalog, new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true })
           + Environment.NewLine;

    private static GitHubAsset? SelectAsset(GitHubRelease release, string? named)
    {
        if (named is not null)
            return release.Assets.Find(a => string.Equals(a.Name, named, StringComparison.OrdinalIgnoreCase));

        var zips = release.Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();

        // Ambiguity is refused rather than guessed: picking the wrong asset publishes a checksum
        // for something nobody meant to ship.
        return zips.Count == 1 ? zips[0] : null;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");

        return 1;
    }
}
