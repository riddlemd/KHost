using KHost.UnitTests.Domain.Services.Visualisations;

namespace KHost.UnitTests.UserInterface.Assets;

/// <summary>The console's preview runs the screen's own visualiser and words overlay, copied into
/// its static assets by <c>copy:vendors</c>. A copy that drifts previews something the screen does
/// not draw.</summary>
public class VisualiserPreviewAssetsTests
{
    [Theory]
    [InlineData("butterchurn.min.js")]
    [InlineData("visualiser-presets.js")]
    [InlineData("eq-visualisers.js")]
    [InlineData("visualiser.js")]
    [InlineData("lyrics-overlay.js")]
    [InlineData("VISUALISER-NOTICE.md")]
    public void ThePreviewsCopy_IsTheScreensFile(string file)
    {
        var root = VisualiserPresetServiceTests.RepositoryRoot();
        var screen = File.ReadAllBytes(Path.Combine(root, "src", "KHost.LocalScreen", "screen-ui", file));
        var console = File.ReadAllBytes(Path.Combine(root, "src", "KHost.UserInterface", "wwwroot", "js", "visualiser", file));

        Assert.True(screen.AsSpan().SequenceEqual(console),
            $"wwwroot/js/visualiser/{file} differs from screen-ui/{file}: run `npm run copy:vendors` in src/KHost.UserInterface.");
    }
}
