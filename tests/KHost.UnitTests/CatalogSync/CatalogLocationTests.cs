using KHost.CatalogSync;

namespace KHost.UnitTests.CatalogSync;

public class CatalogLocationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-catalog-location-");

    public void Dispose()
    {
        try { _root.Delete(recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void FindDefault_PerBranchLayout_FindsReleasesMain()
    {
        var repo = Repo(Path.Combine("riddlemd", "KHost", "feat-x"));
        var catalog = Catalog(Path.Combine("riddlemd", "KHost.Releases", "main"));

        Assert.Equal(catalog, CatalogLocation.FindDefault(repo));
    }

    [Fact]
    public void FindDefault_StartedInsideTheRepo_WalksUpToItsRoot()
    {
        var repo = Repo(Path.Combine("riddlemd", "KHost", "main"));
        var catalog = Catalog(Path.Combine("riddlemd", "KHost.Releases", "main"));

        Assert.Equal(catalog, CatalogLocation.FindDefault(Directory.CreateDirectory(Path.Combine(repo, "tools", "x")).FullName));
    }

    [Fact]
    public void FindDefault_FlatSiblings_FindsTheSibling()
    {
        var repo = Repo("KHost");
        var catalog = Catalog("KHost.Releases");

        Assert.Equal(catalog, CatalogLocation.FindDefault(repo));
    }

    [Fact]
    public void FindDefault_BothLayoutsPresent_PrefersPerBranch()
    {
        var repo = Repo(Path.Combine("o", "KHost", "main"));
        var perBranch = Catalog(Path.Combine("o", "KHost.Releases", "main"));
        Catalog(Path.Combine("o", "KHost", "KHost.Releases"));

        Assert.Equal(perBranch, CatalogLocation.FindDefault(repo));
    }

    [Fact]
    public void FindDefault_NoCheckout_IsNull()
        => Assert.Null(CatalogLocation.FindDefault(Repo(Path.Combine("o", "KHost", "main"))));

    private string Repo(string relative)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root.FullName, relative)).FullName;

        File.WriteAllText(Path.Combine(directory, "KHost.slnx"), "<Solution />");

        return directory;
    }

    private string Catalog(string relative)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root.FullName, relative)).FullName;
        var path = Path.Combine(directory, CatalogLocation.FileName);

        File.WriteAllText(path, "{}");

        return path;
    }
}
