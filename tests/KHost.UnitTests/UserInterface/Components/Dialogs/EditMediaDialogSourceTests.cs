using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What produced a file, shown but never decided from: the column is an unchecked claim.
/// </summary>
public class EditMediaDialogSourceTests : BunitContext
{
    private const string SourceSelector = "#edit-media-source";

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();

    public EditMediaDialogSourceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _search.Providers.Returns([]);
        Services.AddSingleton(_search);
    }

    /// <summary>The folder scan names nothing, so an empty column is the local library rather than
    /// a gap, and a host must not be shown a blank where every other row says something.</summary>
    [Fact]
    public void SourceIsEmpty_ReadsAsLocal()
    {
        var cut = Render(new Media { Title = "Neon Moon", FilePath = "/x.mp4", Source = "" });

        Assert.Equal("Local", cut.Find(SourceSelector).GetAttribute("value"));
    }

    /// <summary>The column stores a SourceName, which is an assembly id a host should never read.
    /// </summary>
    [Fact]
    public void SourceNamesALoadedProvider_ShowsThatProvidersDisplayName()
    {
        WithProviders(Provider(sourceName: "KHost.Plugins.YouTube", displayName: "YouTube"));

        var cut = Render(new Media { Title = "Neon Moon", FilePath = "/x.mp4", Source = "KHost.Plugins.YouTube" });

        Assert.Equal("YouTube", cut.Find(SourceSelector).GetAttribute("value"));
    }

    /// <summary>Matched the way every other provider lookup here matches, so a manifest that cases
    /// its own id differently still resolves instead of falling through to the raw string.</summary>
    [Fact]
    public void SourceDiffersOnlyByCase_StillResolvesTheProvider()
    {
        WithProviders(Provider(sourceName: "KHost.Plugins.YouTube", displayName: "YouTube"));

        var cut = Render(new Media { Title = "Neon Moon", FilePath = "/x.mp4", Source = "khost.plugins.youtube" });

        Assert.Equal("YouTube", cut.Find(SourceSelector).GetAttribute("value"));
    }

    /// <summary>An uninstalled plugin resolves to nobody. Falling back to "Local" would claim the
    /// file came from somewhere it did not, so its own name stands instead.</summary>
    [Fact]
    public void SourceNamesAPluginThatIsGone_KeepsTheStoredNameRatherThanClaimingLocal()
    {
        WithProviders(Provider(sourceName: "KHost.Plugins.Spotify", displayName: "Spotify"));

        var cut = Render(new Media { Title = "Neon Moon", FilePath = "/x.mp4", Source = "KHost.Plugins.Removed" });

        Assert.Equal("KHost.Plugins.Removed", cut.Find(SourceSelector).GetAttribute("value"));
    }

    /// <summary>A host cannot type here: the value is the importer's to state, not theirs to edit.
    /// </summary>
    [Fact]
    public void TheSourceField_IsReadonly()
    {
        var cut = Render(new Media { Title = "Neon Moon", FilePath = "/x.mp4", Source = "" });

        Assert.True(cut.Find(SourceSelector).HasAttribute("readonly"));
    }

    private void WithProviders(params IMediaProvider[] providers)
        => _search.Providers.Returns(providers);

    private static IMediaProvider Provider(string sourceName, string displayName)
    {
        var provider = Substitute.For<IMediaProvider>();
        provider.SourceName.Returns(sourceName);
        provider.DisplayName.Returns(displayName);
        return provider;
    }

    private IRenderedComponent<EditMediaDialog> Render(Media media)
        => Render<EditMediaDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Media, media));
}
