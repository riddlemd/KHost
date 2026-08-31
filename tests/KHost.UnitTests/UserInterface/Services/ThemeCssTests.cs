using System.Text.RegularExpressions;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;

namespace KHost.UnitTests.UserInterface.Services;

public class ThemeCssTests
{
    private static readonly Regex _declaration = new(@"(--[A-Za-z0-9-]+)\s*:\s*([^;{}]+)\s*;", RegexOptions.Compiled);

    public static TheoryData<string> ShippedThemes()
    {
        var data = new TheoryData<string>();

        foreach (var file in Directory.GetFiles(ThemesDirectory(), "*.scss").OrderBy(f => f))
            data.Add(Path.GetFileNameWithoutExtension(file));

        return data;
    }

    /// <summary>
    /// A clone has to be the theme the host actually picked, so every shipped theme is round-tripped
    /// through the editable set and compared property by property. This is what caught the shipped
    /// themes tuning their own shade alphas, which an earlier derive-everything build got wrong.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedThemes))]
    public void Build_FromShippedTheme_ReproducesEveryProperty(string themeName)
    {
        var source = ReadShipped(themeName);

        var theme = new ThemeDefinition
        {
            Id = themeName,
            Name = themeName,
            Variables = source
                .Where(v => ThemeVariableCatalog.IsKnown(v.Key))
                .ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal)
        };

        var rebuilt = Declarations(ThemeCss.Build(theme));

        Assert.Equal(source.Keys.OrderBy(k => k, StringComparer.Ordinal),
            rebuilt.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (key, expected) in source)
            Assert.Equal(expected, rebuilt[key]);
    }

    [Fact]
    public void Fields_CoverEveryEditablePropertyOfAShippedTheme()
    {
        var grape = ReadShipped("grape");

        var missing = ThemeVariableCatalog.Fields.Select(f => f.Key).Except(grape.Keys).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Parse_KeepsEditablePropertiesAndDropsGeneratedOnes()
    {
        var parsed = ThemeCss.Parse(":root { --kh-primary: #112233; --bs-primary-rgb: 1, 2, 3; --unknown: x; }");

        Assert.Equal("#112233", parsed["--kh-primary"]);
        Assert.False(parsed.ContainsKey("--bs-primary-rgb"));
        Assert.False(parsed.ContainsKey("--unknown"));
    }

    [Theory]
    [InlineData("#112233", true)]
    [InlineData("rgba(1, 2, 3, 0.5)", true)]
    [InlineData("red; } body { display: none", false)]
    [InlineData("</style><script>", false)]
    [InlineData("blue /* comment */", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidValue_RejectsAnythingThatCouldLeaveTheDeclaration(string value, bool expected)
        => Assert.Equal(expected, ThemeCss.IsValidValue(value));

    [Fact]
    public void Build_WithAValueThatWouldEscape_FallsBackRatherThanEmittingIt()
    {
        var theme = new ThemeDefinition
        {
            Id = "x",
            Name = "X",
            Variables = new Dictionary<string, string>(ThemeVariableCatalog.Defaults())
            {
                ["--kh-bg"] = "red; } body { display: none"
            }
        };

        var css = ThemeCss.Build(theme);

        Assert.DoesNotContain("display: none", css);
        Assert.Contains("--kh-bg: #0B0814;", css);
    }

    [Fact]
    public void Build_DerivesTheBootstrapTripletFromPrimary()
    {
        var theme = new ThemeDefinition
        {
            Id = "x",
            Name = "X",
            Variables = new Dictionary<string, string>(ThemeVariableCatalog.Defaults())
            {
                ["--kh-primary"] = "#0A141E"
            }
        };

        Assert.Contains("--bs-primary-rgb: 10, 20, 30;", ThemeCss.Build(theme));
    }

    [Fact]
    public void Build_EmitsTheStoredShadeRatherThanRederivingIt()
    {
        var theme = new ThemeDefinition
        {
            Id = "x",
            Name = "X",
            Variables = new Dictionary<string, string>(ThemeVariableCatalog.Defaults())
            {
                ["--kh-primary"] = "#0A141E",
                ["--kh-border"] = "rgba(200, 200, 200, 0.24)"
            }
        };

        Assert.Contains("--kh-border: rgba(200, 200, 200, 0.24);", ThemeCss.Build(theme));
    }

    [Fact]
    public void DeriveShades_RebasesOnTheSourceColourAndKeepsTheThemesOwnAlpha()
    {
        var values = new Dictionary<string, string>(ThemeVariableCatalog.Defaults())
        {
            ["--kh-primary"] = "#0A141E",
            ["--kh-border"] = "rgba(200, 200, 200, 0.24)"
        };

        ThemeCss.DeriveShades(values);

        Assert.Equal("rgba(10, 20, 30, 0.24)", values["--kh-border"]);
        Assert.Equal("rgba(10, 20, 30, 0.14)", values["--kh-primary-subtle"]);
    }

    /// <summary>Grape follows the standard recipe throughout, so deriving must be a no-op on it.</summary>
    [Fact]
    public void DeriveShades_LeavesAThemeThatAlreadyFollowsTheRecipeUnchanged()
    {
        var grape = ReadShipped("grape");
        var values = ThemeVariableCatalog.Fields
            .ToDictionary(f => f.Key, f => grape[f.Key], StringComparer.Ordinal);

        var before = new Dictionary<string, string>(values, StringComparer.Ordinal);
        ThemeCss.DeriveShades(values);

        Assert.Equal(before, values);
    }

    [Theory]
    [InlineData("--kh-primary", "#5D2B90", true)]
    [InlineData("--kh-primary", "#abc", true)]
    [InlineData("--kh-primary", "rebeccapurple", false)]
    [InlineData("--kh-primary", "rgb(93, 43, 144)", false)]
    [InlineData("--kh-primary", "var(--kh-accent)", false)]
    [InlineData("--kh-radius", "8px", true)]
    [InlineData("--bs-link-color", "var(--kh-primary-bright)", true)]
    public void IsValidFor_RequiresAHexLiteralOnlyForAColourField(string key, string value, bool expected)
        => Assert.Equal(expected, ThemeCss.IsValidFor(ThemeVariableCatalog.Find(key)!, value));

    /// <summary>
    /// --bs-primary-rgb is Bootstrap's copy of --kh-primary, and every alpha Bootstrap derives comes
    /// from it. A value the triplet cannot parse used to leave the two describing different colours.
    /// </summary>
    [Theory]
    [InlineData("#0A141E")]
    [InlineData("#abc")]
    [InlineData("rebeccapurple")]
    [InlineData("rgb(1, 2, 3)")]
    [InlineData("")]
    public void Build_EmitsATripletThatMatchesThePrimaryItEmitted(string primary)
    {
        var theme = new ThemeDefinition
        {
            Id = "x",
            Name = "X",
            Variables = new Dictionary<string, string>(ThemeVariableCatalog.Defaults())
            {
                ["--kh-primary"] = primary
            }
        };

        var css = Declarations(ThemeCss.Build(theme));

        Assert.True(ThemeCss.TryParseHex(css["--kh-primary"], out var r, out var g, out var b),
            $"--kh-primary was emitted as {css["--kh-primary"]}, which no triplet can describe.");
        Assert.Equal($"{r}, {g}, {b}", css["--bs-primary-rgb"]);
    }

    [Theory]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    [InlineData("#0A141E", 10, 20, 30)]
    [InlineData("0A141E", 10, 20, 30)]
    public void TryParseHex_AcceptsShortAndLongForms(string value, int r, int g, int b)
    {
        Assert.True(ThemeCss.TryParseHex(value, out var red, out var green, out var blue));
        Assert.Equal((r, g, b), (red, green, blue));
    }

    [Theory]
    [InlineData("var(--kh-primary)")]
    [InlineData("rgba(1, 2, 3, 0.5)")]
    [InlineData("#12345")]
    [InlineData(null)]
    public void TryParseHex_RejectsWhatIsNotAHexLiteral(string? value)
        => Assert.False(ThemeCss.TryParseHex(value, out _, out _, out _));

    private static Dictionary<string, string> Declarations(string css)
        => _declaration.Matches(css)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);

    private static Dictionary<string, string> ReadShipped(string themeName)
        => Declarations(File.ReadAllText(Path.Combine(ThemesDirectory(), themeName + ".scss")));

    // Walked up rather than hardcoded: the depth from the test binary to the root changes with
    // BaseOutputPath, which this repo's build redirects.
    private static string ThemesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.GetFiles("KHost.slnx").Length > 0)
                return Path.Combine(directory.FullName, "src", "KHost.UserInterface", "wwwroot", "scss", "themes");
        }

        throw new InvalidOperationException(
            $"No KHost.slnx above {AppContext.BaseDirectory}, so the repository root could not be found.");
    }
}
