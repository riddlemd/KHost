using System.Reflection;

namespace KHost.UserInterface.Services;

public sealed class AppInfoService : IAppInfoService
{
    // Matches the LogicalName each file is embedded under in KHost.UserInterface.csproj.
    private const string ResourcePrefix = "legal/";

    public string Version { get; }
    public string Copyright { get; }
    public string LicenseName { get; }
    public string LicenseText { get; }
    public string ThirdPartyNotices { get; }
    public IReadOnlyList<ReferencedLicense> ReferencedLicenses { get; }
    public string RepositoryUrl { get; }
    public string IssuesUrl { get; }
    public string WikiUrl { get; }
    public string ContributingUrl { get; }

    public AppInfoService()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // AssemblyVersion in the csproj.
        Version = assembly.GetName().Version?.ToString() ?? "_ERROR_";

        LicenseText = ReadResource(assembly, "LICENSE");
        ThirdPartyNotices = ReadResource(assembly, "THIRD-PARTY-NOTICES.md");

        Copyright = ExtractCopyright(LicenseText);
        LicenseName = ExtractLicenseName(LicenseText);

        ReferencedLicenses =
        [
            new ReferencedLicense("Apache License 2.0", ReadResource(assembly, "Apache-2.0.txt")),
            new ReferencedLicense("GNU LGPL v2.1", ReadResource(assembly, "LGPL-2.1.txt")),
            new ReferencedLicense("SIL Open Font License 1.1", ReadResource(assembly, "SIL-OFL-1.1.txt")),
        ];

        // The csproj's RepositoryUrl becomes an AssemblyMetadataAttribute automatically (no
        // SourceLink needed) — it's the same URL `git remote get-url origin` resolves to, minus
        // the .git suffix, so this can't drift from the csproj without drifting with it. No runtime
        // .git access, so it still resolves in a published build with no repo checked out.
        RepositoryUrl = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value ?? "";

        // GitHub gives every repository these paths for free; no separate metadata to keep in sync.
        IssuesUrl = RepositoryUrl.Length > 0 ? $"{RepositoryUrl}/issues" : "";
        WikiUrl = RepositoryUrl.Length > 0 ? $"{RepositoryUrl}/wiki" : "";
        // "master" is this repo's current default branch (git symbolic-ref refs/remotes/origin/HEAD) —
        // not derivable from build metadata, so it needs updating by hand if that branch is ever renamed.
        ContributingUrl = RepositoryUrl.Length > 0 ? $"{RepositoryUrl}/blob/master/CONTRIBUTING.md" : "";
    }

    // Read from LICENSE itself rather than hardcoded here, so the page can't drift from the file.
    private static string ExtractCopyright(string licenseText)
        => licenseText
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Copyright", StringComparison.Ordinal))
            ?? "";

    // The Markdown heading above the license body, e.g. "# PolyForm Shield License 1.0.0".
    private static string ExtractLicenseName(string licenseText)
        => licenseText
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..]
            ?? "";

    private static string ReadResource(Assembly assembly, string fileName)
    {
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"Embedded legal resource '{fileName}' is missing.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
