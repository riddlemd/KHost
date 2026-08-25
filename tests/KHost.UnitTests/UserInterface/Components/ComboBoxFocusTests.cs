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

    private List<Song> _catalogue = [new("Jolene"), new("Respect")];
    private int _searches;

    public ComboBoxFocusTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<ComboBox<Song>> RenderBox(bool openWhenEmpty, Song? value = null, int? maxRows = null)
        => Render<ComboBox<Song>>(parameters => parameters
            .Add(c => c.DisplayName, song => song.Title)
            .Add(c => c.OpenWhenEmpty, openWhenEmpty)
            .Add(c => c.MaxRowsWhenEmpty, maxRows ?? new ComboBox<Song>().MaxRowsWhenEmpty)
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

    /// <summary>
    /// An empty field is not a search. Without a cap, opening a box over a library answers with
    /// the library — hundreds of rows a host has to scroll past to reach the field again.
    /// </summary>
    [Fact]
    public void Focusing_AnEmptyBox_ShowsNoMoreRowsThanTheCap()
    {
        _catalogue = [.. Enumerable.Range(1, 50).Select(i => new Song($"Song {i}"))];

        var combo = RenderBox(openWhenEmpty: true, maxRows: 10);

        combo.Find("input").Focus();

        Assert.Equal(10, combo.FindAll(OptionSelector).Count);
    }

    [Fact]
    public void Focusing_AnEmptyBox_DefaultsToTenRows()
    {
        _catalogue = [.. Enumerable.Range(1, 50).Select(i => new Song($"Song {i}"))];

        var combo = RenderBox(openWhenEmpty: true);

        combo.Find("input").Focus();

        Assert.Equal(10, combo.FindAll(OptionSelector).Count);
    }

    /// <summary>A cap nothing admits to reads as "this is all there is".</summary>
    [Fact]
    public void Focusing_AnEmptyBoxWithMoreToShow_SaysThereIsMore()
    {
        _catalogue = [.. Enumerable.Range(1, 50).Select(i => new Song($"Song {i}"))];

        var combo = RenderBox(openWhenEmpty: true, maxRows: 10);

        combo.Find("input").Focus();

        Assert.Contains("Start typing", combo.Find(".kh-combobox__message").TextContent);
    }

    [Fact]
    public void Focusing_AnEmptyBoxShowingEverythingItHas_SaysNothingAboutMore()
    {
        var combo = RenderBox(openWhenEmpty: true, maxRows: 10);

        combo.Find("input").Focus();

        Assert.Equal(_catalogue.Count, combo.FindAll(OptionSelector).Count);
        Assert.Empty(combo.FindAll(".kh-combobox__message"));
    }
}
