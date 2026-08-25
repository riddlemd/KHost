using Bunit;
using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>
/// Rendered rather than driven by reflection: what this behaviour is, is a menu appearing, and the
/// search that fills it asks for a render the plain-instance tests have no renderer for.
/// </summary>
public class ComboBoxFocusTests : BunitContext
{
    // Deliberately not a KHost model: the box must not care what its rows are.
    private sealed record Song(string Title);

    private const string OptionSelector = ".kh-combobox__option";

    private readonly List<Song> _catalogue = [new("Jolene"), new("Respect")];
    private int _searches;

    public ComboBoxFocusTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<ComboBox<Song>> RenderBox(bool openWhenEmpty, Song? value = null)
        => Render<ComboBox<Song>>(parameters => parameters
            .Add(c => c.DisplayName, song => song.Title)
            .Add(c => c.OpenWhenEmpty, openWhenEmpty)
            .Add(c => c.Value, value)
            .Add(c => c.Search, _ =>
            {
                _searches++;
                return Task.FromResult<IReadOnlyList<Song>>(_catalogue);
            }));

    /// <summary>
    /// Off by default. Against a list of every song in the library, a menu that opens itself is a
    /// wall of rows in front of a field the host meant to type into.
    /// </summary>
    [Fact]
    public void Focusing_AnEmptyBox_StaysShutUnlessAskedToOpen()
    {
        var combo = RenderBox(openWhenEmpty: false);

        combo.Find("input").Focus();

        Assert.Empty(combo.FindAll(OptionSelector));
        Assert.Equal(0, _searches);
    }

    [Fact]
    public void Focusing_AnEmptyBoxThatOpensWhenEmpty_ShowsEverything()
    {
        var combo = RenderBox(openWhenEmpty: true);

        combo.Find("input").Focus();

        Assert.Equal(_catalogue.Count, combo.FindAll(OptionSelector).Count);
    }

    /// <summary>
    /// A chosen row puts its name in the field. Reopening over that would sit a menu between the
    /// host and the answer they already gave.
    /// </summary>
    [Fact]
    public void Focusing_ABoxThatAlreadyHasAValue_LeavesItShut()
    {
        var combo = RenderBox(openWhenEmpty: true, value: new Song("Jolene"));

        combo.Find("input").Focus();

        Assert.Empty(combo.FindAll(OptionSelector));
        Assert.Equal(0, _searches);
    }
}
