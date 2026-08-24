namespace KHost.UserInterface.Services;

/// <summary>One licensed text bundled under <c>licenses/</c> and referenced from THIRD-PARTY-NOTICES.md.</summary>
public sealed record ReferencedLicense(string Name, string Text);

/// <summary>App identity and legal text for the About page, sourced from the assembly and the
/// repo-root LICENSE / THIRD-PARTY-NOTICES.md / licenses/ files embedded at build time.</summary>
public interface IAppInfoService
{
    string Version { get; }
    string Copyright { get; }
    string LicenseName { get; }
    string LicenseText { get; }
    string ThirdPartyNotices { get; }
    IReadOnlyList<ReferencedLicense> ReferencedLicenses { get; }

    /// <summary>The GitHub repository, from the csproj's RepositoryUrl (same value `git remote
    /// get-url origin` resolves to) — empty if the build carries no RepositoryUrl metadata.</summary>
    string RepositoryUrl { get; }
    string IssuesUrl { get; }
    string WikiUrl { get; }
    string ContributingUrl { get; }
}
