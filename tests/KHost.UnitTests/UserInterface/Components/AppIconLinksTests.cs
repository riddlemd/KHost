using System.Text.RegularExpressions;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>App.razor's head names the icons, and each one it names ships in wwwroot.</summary>
/// <remarks>Read from source: App hosts Routes, ImportMap and the render-mode outlets, none of which
/// render outside a running host.</remarks>
public partial class AppIconLinksTests
{
    public static TheoryData<string, string> ExpectedLinks => new()
    {
        { "icon", "favicon.svg" },
        { "icon", "favicon.ico" },
        { "icon", "icon-192.png" },
        { "icon", "icon-512.png" },
        { "apple-touch-icon", "apple-touch-icon.png" },
    };

    [Theory]
    [MemberData(nameof(ExpectedLinks))]
    public void Head_LinksTheIcon(string rel, string href)
    {
        var links = HeadLinks();

        Assert.Contains((rel, href), links);
    }

    [Fact]
    public void Head_EveryIconLink_NamesAFileInWwwroot()
    {
        var wwwroot = Path.Combine(RepositoryRoot(), "src", "KHost.UserInterface", "wwwroot");

        var icons = HeadLinks().Where(l => l.Rel is "icon" or "apple-touch-icon").ToList();

        Assert.NotEmpty(icons);
        Assert.All(icons, l => Assert.True(File.Exists(Path.Combine(wwwroot, l.Href)), $"{l.Href} is not in wwwroot"));
    }

    [Fact]
    public void Head_SvgIcon_DeclaresItsType()
    {
        // Without the type a browser that cannot draw SVG icons still picks it over the .ico.
        var head = Head();

        Assert.Matches(@"<link rel=""icon"" type=""image/svg\+xml"" href=""favicon\.svg""", head);
    }

    private static List<(string Rel, string Href)> HeadLinks()
        => LinkPattern().Matches(Head())
            .Select(m => (m.Groups["rel"].Value, m.Groups["href"].Value))
            .ToList();

    private static string Head()
    {
        var app = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "KHost.UserInterface", "Components", "App.razor"));

        var start = app.IndexOf("<head>", StringComparison.Ordinal);
        var end = app.IndexOf("</head>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "App.razor has no <head>");

        return app[start..end];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }

    [GeneratedRegex(@"<link\s+rel=""(?<rel>[^""]+)""[^>]*?\bhref=""(?<href>[^""]+)""")]
    private static partial Regex LinkPattern();
}
