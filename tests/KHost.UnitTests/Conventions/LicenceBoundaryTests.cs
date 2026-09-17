using System.Xml.Linq;

namespace KHost.UnitTests.Conventions;

/// <summary>An MIT project referencing PolyForm code pulls it into what a plugin redistributes.</summary>
public class LicenceBoundaryTests
{
    private static readonly string[] MitProjects = ["KHost.Abstractions", "KHost.Common"];

    public static TheoryData<string> MitProjectNames()
    {
        var data = new TheoryData<string>();

        foreach (var name in MitProjects)
            data.Add(name);

        return data;
    }

    [Theory]
    [MemberData(nameof(MitProjectNames))]
    public void AnMitProject_ReferencesOnlyMitProjects(string projectName)
    {
        var project = Path.Combine(SourceDirectory(), projectName, $"{projectName}.csproj");

        Assert.True(File.Exists(project), $"{projectName} is listed as MIT but has no project file at {project}.");

        var referenced = XDocument.Load(project)
            .Descendants("ProjectReference")
            // An analyzer runs in the compiler and its assembly is never referenced by the output,
            // so it carries no code into what a plugin redistributes.
            .Where(reference => reference.Attribute("OutputItemType")?.Value != "Analyzer")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .ToList();

        var offending = referenced.Except(MitProjects).ToList();

        Assert.True(offending.Count == 0,
            $"{projectName} is MIT and a plugin redistributes it, so it may not reference "
            + $"PolyForm-licensed {string.Join(", ", offending)}.");
    }

    /// <summary>Every MIT project must say so itself, or its package claims the wrong terms.</summary>
    [Theory]
    [MemberData(nameof(MitProjectNames))]
    public void AnMitProject_DeclaresTheLicenceAndShipsIt(string projectName)
    {
        var directory = Path.Combine(SourceDirectory(), projectName);

        var expression = XDocument.Load(Path.Combine(directory, $"{projectName}.csproj"))
            .Descendants("PackageLicenseExpression")
            .Select(element => element.Value)
            .FirstOrDefault();

        Assert.Equal("MIT", expression);
        Assert.True(File.Exists(Path.Combine(directory, "LICENSE")),
            $"{projectName} declares MIT but ships no LICENSE beside it.");
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
