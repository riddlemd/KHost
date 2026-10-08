using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml.Linq;

namespace KHost.UnitTests.Conventions;

/// <summary>The committed icon files are well-formed and every executable points at one.</summary>
/// <remarks>A malformed .ico still builds: the compiler embeds whatever bytes it is given, and the
/// fault shows only as a blank icon in Explorer.</remarks>
public class AppIconAssetsTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static TheoryData<string, int[]> IcoFiles => new()
    {
        { "assets/icon/khost.ico", [16, 24, 32, 48, 64, 128, 256] },
        { "assets/icon/khost-screen.ico", [16, 24, 32, 48, 64, 128, 256] },
        { "src/KHost.UserInterface/wwwroot/favicon.ico", [16, 32, 48] },
    };

    private static readonly string[] ExecutableProjects =
    [
        "src/KHost.UserInterface/KHost.UserInterface.csproj",
        "src/KHost.LocalScreen/KHost.LocalScreen.csproj",
    ];

    public static TheoryData<string> Executables => new(ExecutableProjects);

    [Theory]
    [MemberData(nameof(IcoFiles))]
    public void Ico_HasOneWellFormedLayerPerSize(string relativePath, int[] expectedSizes)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), relativePath));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        Assert.Equal(expectedSizes.Length, count);

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + i * 16, 16);
            // The one-byte size fields cannot hold 256, which the format writes as 0.
            var size = entry[0] == 0 ? 256 : entry[0];
            Assert.Equal(entry[0], entry[1]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.InRange(offset + length, 0, bytes.Length);

            var layer = bytes.AsSpan(offset, length);
            if (layer.StartsWith(PngSignature))
                AssertPngDecodes(layer.ToArray(), size);
            else
                AssertDibIsWellFormed(layer, size);

            sizes.Add(size);
        }

        Assert.Equal(expectedSizes, sizes);
    }

    [Theory]
    [InlineData("khost.ico")]
    [InlineData("khost-screen.ico")]
    public void AppIco_StoresTheLargeLayersAsPng(string fileName)
    {
        // BMP at 256 is a quarter of a megabyte per exe for nothing; the small ones stay BMP for old shells.
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "assets", "icon", fileName));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + i * 16, 16);
            var size = entry[0] == 0 ? 256 : entry[0];
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.Equal(size >= 64, bytes.AsSpan(offset).StartsWith(PngSignature));
        }
    }

    [Theory]
    [MemberData(nameof(Executables))]
    public void Executable_DeclaresAnApplicationIconThatExists(string relativeProject)
    {
        var project = Path.Combine(RepositoryRoot(), relativeProject);

        var icon = XDocument.Load(project).Descendants("ApplicationIcon").Select(e => e.Value.Trim()).SingleOrDefault();

        Assert.False(string.IsNullOrEmpty(icon), $"{relativeProject} declares no ApplicationIcon");
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, icon!.Replace('\\', Path.DirectorySeparatorChar)));
        Assert.True(File.Exists(resolved), $"{relativeProject} names {icon}, which does not exist");
        Assert.Equal(".ico", Path.GetExtension(resolved));
    }

    [Fact]
    public void Executables_DoNotShareAnApplicationIcon()
    {
        // With one icon, the console and a launched screen are two identical tiles in the Dock and taskbar.
        var icons = ExecutableProjects.Select(ResolvedApplicationIcon).ToArray();

        Assert.Equal(icons.Length, icons.Distinct().Count());
    }

    [Fact]
    public void AppIconCopies_NameDifferentFiles()
    {
        // The console copies the screen's output into its own, so a shared name is the screen's icon
        // silently replacing the console's.
        Assert.NotEqual(KHost.UserInterface.AppIcon.WindowsIconFileName, KHost.LocalScreen.AppIcon.WindowsIconFileName);
        Assert.NotEqual(KHost.UserInterface.AppIcon.MacIconFileName, KHost.LocalScreen.AppIcon.MacIconFileName);
        Assert.NotEqual(KHost.UserInterface.AppIcon.LinuxIconFileName, KHost.LocalScreen.AppIcon.LinuxIconFileName);
    }

    [Theory]
    [InlineData(KHost.LocalScreen.AppIcon.WindowsIconFileName)]
    [InlineData(KHost.LocalScreen.AppIcon.MacIconFileName)]
    [InlineData(KHost.LocalScreen.AppIcon.LinuxIconFileName)]
    public void LocalScreen_CopiesTheScreenIconUnderTheNameAppIconReads(string runtimeName)
    {
        const string relativeProject = "src/KHost.LocalScreen/KHost.LocalScreen.csproj";
        var project = Path.Combine(RepositoryRoot(), relativeProject);
        var link = Path.Combine(KHost.LocalScreen.AppIcon.DirectoryName, runtimeName);

        var source = XDocument.Load(project).Descendants("None")
            .Where(e => NormalisePath((string?)e.Attribute("Link")) == link)
            .Select(e => ResolveFromProject(project, (string)e.Attribute("Include")!))
            .SingleOrDefault();

        Assert.True(source is not null, $"{relativeProject} copies nothing to {link}, so the window or Dock has no icon");
        Assert.True(File.Exists(source), $"{relativeProject} copies {source} to {link}, which does not exist");
        Assert.StartsWith("khost-screen", Path.GetFileName(source));
    }

    private static string ResolvedApplicationIcon(string relativeProject)
    {
        var project = Path.Combine(RepositoryRoot(), relativeProject);
        var icon = XDocument.Load(project).Descendants("ApplicationIcon").Single().Value.Trim();
        return ResolveFromProject(project, icon);
    }

    private static string ResolveFromProject(string project, string relative)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, NormalisePath(relative)!));

    private static string? NormalisePath(string? path) => path?.Replace('\\', Path.DirectorySeparatorChar);

    private static void AssertPngDecodes(byte[] png, int size)
    {
        var position = PngSignature.Length;
        int width = 0, height = 0;
        using var idat = new MemoryStream();

        while (position < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            var type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            var body = png.AsSpan(position + 8, length);

            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                Assert.Equal(8, body[8]);
                Assert.Equal(6, body[9]);
            }
            else if (type == "IDAT")
            {
                idat.Write(body);
            }

            position += 12 + length;
        }

        Assert.Equal((size, size), (width, height));

        idat.Position = 0;
        using var inflater = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflater.CopyTo(raw);

        // A filter byte then RGBA for every row.
        Assert.Equal(height * (1 + width * 4), raw.Length);
    }

    private static void AssertDibIsWellFormed(ReadOnlySpan<byte> dib, int size)
    {
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(dib));
        Assert.Equal(size, BinaryPrimitives.ReadInt32LittleEndian(dib[4..]));
        // Twice the height: the colour rows and then the AND mask.
        Assert.Equal(size * 2, BinaryPrimitives.ReadInt32LittleEndian(dib[8..]));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]));

        var mask = (size + 31) / 32 * 4 * size;
        Assert.Equal(40 + size * size * 4 + mask, dib.Length);
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
