using System.Collections.Immutable;
using KHost.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KHost.UnitTests.Conventions;

/// <summary>KH0002 needs both the analyzer's report and the .editorconfig severity to hold.</summary>
public class ExplicitEnumValuesInAbstractionsTests
{
    [Fact]
    public async Task Analyzer_ImplicitMember_ReportsKH0002AtThatMember()
    {
        const string source = "public enum Sample { First = 0, Second }";

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ExplicitEnumValuesAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("'Second'", diagnostic.GetMessage());
        Assert.Equal("Second", source[diagnostic.Location.SourceSpan.Start..diagnostic.Location.SourceSpan.End]);
    }

    [Fact]
    public async Task Analyzer_ExplicitMembers_ReportsNothing()
    {
        var diagnostics = await AnalyzeAsync("public enum Sample { First = 0, Second = 1 }");

        Assert.Empty(diagnostics);
    }

    /// <summary>The descriptor ships Hidden so it can travel solution-wide; this raises it here.</summary>
    [Fact]
    public void Abstractions_RaisesTheRule_ToAnError()
    {
        var configuration = Path.Combine(SourceDirectory(), "KHost.Abstractions", ".editorconfig");

        Assert.True(File.Exists(configuration), $"KH0002 is only fatal via {configuration}, which is missing.");
        Assert.Contains("dotnet_diagnostic.KH0002.severity = error", File.ReadAllText(configuration));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return await compilation
            .WithAnalyzers([new ExplicitEnumValuesAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
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
