using System.Xml.Linq;

namespace KHost.UnitTests.UserInterface;

/// <summary>The host's build carries a copy of the screen's output into its own, and the screen's
/// output folder can hold state from a run made straight from that project.</summary>
public class LocalScreenCopyTests
{
    [Theory]
    [InlineData("cache")]
    [InlineData("logs")]
    public void CopyLocalScreenToOutput_RuntimeFolder_IsNotCopied(string folder)
    {
        var project = Path.Combine(SourceDirectory(), "KHost.UserInterface", "KHost.UserInterface.csproj");

        var files = XDocument.Load(project)
            .Descendants("Target")
            .Single(target => target.Attribute("Name")?.Value == "CopyLocalScreenToOutput")
            .Descendants("LocalScreenFiles")
            .Single();

        var excluded = (files.Attribute("Exclude")?.Value ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains($"$(LocalScreenOutputDir){folder}\\**", excluded);
    }

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
