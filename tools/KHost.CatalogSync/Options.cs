namespace KHost.CatalogSync;

public sealed record Options
{
    public const string Usage = """
        Adds a plugin's GitHub release to the KHost plugin catalog.

          catalog-sync <owner/repo> [options]

          --tag <v1.2.0>          A specific release; default is the latest.
          --asset <name.zip>      Which asset to publish, when a release carries more than one zip.
          --catalog <path>        Catalog file to update (default plugin-catalog.json).
          --capabilities <a,b>    What the plugin provides; the manifest does not carry this.
          --rid <win|osx|linux>   Platform this build is for; omit for a build that runs anywhere.
          --include-prerelease    Allow a release marked prerelease.
        """;

    public required string Repository { get; init; }

    public string? Tag { get; init; }

    public string? Asset { get; init; }

    public string CatalogPath { get; init; } = "plugin-catalog.json";

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Null for a platform-neutral build, which is what most plugins should ship.</summary>
    public string? Rid { get; init; }

    public bool IncludePrerelease { get; init; }

    /// <summary>Null when the arguments do not name a repository, or a flag is missing its value.</summary>
    public static Options? Parse(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-') || !args[0].Contains('/'))
            return null;

        string? tag = null, asset = null, capabilities = null, rid = null;
        var catalog = "plugin-catalog.json";
        var includePrerelease = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--include-prerelease": includePrerelease = true; break;
                case "--tag" when i + 1 < args.Length: tag = args[++i]; break;
                case "--asset" when i + 1 < args.Length: asset = args[++i]; break;
                case "--catalog" when i + 1 < args.Length: catalog = args[++i]; break;
                case "--capabilities" when i + 1 < args.Length: capabilities = args[++i]; break;
                case "--rid" when i + 1 < args.Length: rid = args[++i]; break;
                default: return null;
            }
        }

        return new Options
        {
            Repository = args[0],
            Tag = tag,
            Asset = asset,
            CatalogPath = catalog,
            Rid = rid,
            IncludePrerelease = includePrerelease,
            Capabilities = capabilities is null
                ? []
                : [.. capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        };
    }
}
