using System.Diagnostics;

namespace KHost.IntegrationTests.Build;

/// <summary>Runs the repo's Directory.Build.targets Clean against a seeded output layout.</summary>
public sealed class DeepCleanTests : IDisposable
{
    private static readonly string[] RuntimeFiles =
    [
        "cache/khost.db",
        "cache/singer-queue.json",
        "cache/screens/screen.window.json",
        "plugins/some-plugin/manifest.json",
        "plugins-staging/some-plugin/manifest.json",
        "logs/khost.log",
    ];

    private static readonly string[] BuildFiles = ["KHost.dll", "wwwroot/app.css", "backgrounds/amber.jpg"];

    private readonly string _project = Path.Combine(Path.GetTempPath(), "khost-deepclean-" + Guid.NewGuid().ToString("N"));

    public DeepCleanTests()
    {
        Directory.CreateDirectory(_project);
        File.Copy(Path.Combine(RepositoryRoot(), "Directory.Build.targets"), Path.Combine(_project, "Directory.Build.targets"));
        File.WriteAllText(Path.Combine(_project, "Probe.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_project, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("bin/Debug/net10.0", null)]
    [InlineData("obj/_build/Debug/net10.0", "./obj/_build")]
    public void Clean_OutputHoldsRuntimeState_KeepsItAndDeletesTheBuild(string output, string? baseOutputPath)
    {
        Seed(output, RuntimeFiles.Concat(BuildFiles));

        RunClean(baseOutputPath);

        foreach (var file in RuntimeFiles)
            Assert.True(File.Exists(Path.Combine(_project, output, file)), $"Clean deleted runtime state {file}.");

        foreach (var file in BuildFiles)
            Assert.False(File.Exists(Path.Combine(_project, output, file)), $"Clean left build output {file}.");
    }

    [Fact]
    public void Clean_NoRuntimeState_RemovesBothRootsWhole()
    {
        Seed("bin/Debug/net10.0", BuildFiles);
        Seed("obj/Debug/net10.0", ["Probe.dll"]);

        RunClean(baseOutputPath: null);

        Assert.False(Directory.Exists(Path.Combine(_project, "bin")));
        Assert.False(Directory.Exists(Path.Combine(_project, "obj")));
    }

    private void Seed(string directory, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(_project, directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
    }

    private void RunClean(string? baseOutputPath)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _project,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "msbuild", "Probe.csproj", "-t:Clean", "-nologo", "-nodeReuse:false" })
            start.ArgumentList.Add(argument);
        if (baseOutputPath is not null)
            start.ArgumentList.Add($"-p:BaseOutputPath={baseOutputPath}");

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Clean failed ({process.ExitCode}):\n{output.Result}\n{error}");
    }

    // Walked up rather than hardcoded: the depth from the test binary to the root changes with
    // BaseOutputPath, which this repo's build redirects.
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.GetFiles("KHost.slnx").Length > 0)
                return directory.FullName;
        }

        throw new InvalidOperationException($"No KHost.slnx above {AppContext.BaseDirectory}.");
    }
}
