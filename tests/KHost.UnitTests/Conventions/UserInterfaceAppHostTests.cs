using System.Xml.Linq;

namespace KHost.UnitTests.Conventions;

/// <summary>The host's Windows executable opens no console window and still serves Blazor's scripts.</summary>
/// <remarks>Both faults pass every other test: a WinExe publish builds clean and serves a white page,
/// and a console apphost is only seen by launching it.</remarks>
public class UserInterfaceAppHostTests
{
    private const string ProjectPath = "src/KHost.UserInterface/KHost.UserInterface.csproj";

    [Fact]
    public void Project_NeverSetsOutputTypeWinExe()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), ProjectPath));

        // The framework assets package adds blazor.web.js to a publish only when OutputType is Exe.
        Assert.DoesNotContain(project.Descendants("OutputType"), element => element.Value.Trim() == "WinExe");
    }

    [Fact]
    public void Project_StampsAGuiAppHostOnWindows()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), ProjectPath));

        var target = Assert.Single(project.Descendants("Target"),
            element => (string?)element.Attribute("AfterTargets") == "_GetAppHostCreationConfiguration");

        Assert.Contains("win", (string?)target.Attribute("Condition") ?? "", StringComparison.Ordinal);
        Assert.Equal("true", target.Descendants("_UseWindowsGraphicalUserInterface").Single().Value.Trim());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }
}
