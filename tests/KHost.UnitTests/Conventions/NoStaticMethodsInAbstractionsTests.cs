using System.Xml.Linq;

namespace KHost.UnitTests.Conventions;

/// <summary>KH0001 needs both the analyzer reference and the .editorconfig severity to hold.</summary>
public class NoStaticMethodsInAbstractionsTests
{
    [Fact]
    public void Abstractions_ReferencesTheAnalyzer_AsAnAnalyzer()
    {
        var references = XDocument.Load(Path.Combine(SourceDirectory(), "KHost.Abstractions", "KHost.Abstractions.csproj"))
            .Descendants("ProjectReference")
            .Where(reference => reference.Attribute("Include")!.Value.Contains("KHost.Analyzers"))
            .ToList();

        var analyzer = Assert.Single(references);

        Assert.Equal("Analyzer", analyzer.Attribute("OutputItemType")?.Value);

        // Referencing the output as well would put the analyzer's assembly (and its Roslyn
        // dependencies) into what a plugin redistributes.
        Assert.Equal("false", analyzer.Attribute("ReferenceOutputAssembly")?.Value);
    }

    /// <summary>The descriptor ships Hidden so it can travel solution-wide; this raises it here.</summary>
    [Fact]
    public void Abstractions_RaisesTheRule_ToAnError()
    {
        var configuration = Path.Combine(SourceDirectory(), "KHost.Abstractions", ".editorconfig");

        Assert.True(File.Exists(configuration), $"KH0001 is only fatal via {configuration}, which is missing.");
        Assert.Contains("dotnet_diagnostic.KH0001.severity = error", File.ReadAllText(configuration));
    }

    // Walked up rather than hardcoded: the depth from the test binary to the root changes with
    // BaseOutputPath, which this repo's build redirects.
    private static string SourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.GetFiles("KHost.slnx").Length > 0)
                return Path.Combine(directory.FullName, "src");
        }

        throw new InvalidOperationException(
            $"No KHost.slnx above {AppContext.BaseDirectory}, so the repository root could not be found.");
    }
}
