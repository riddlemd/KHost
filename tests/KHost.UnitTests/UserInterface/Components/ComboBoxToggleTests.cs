using Bunit;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>Driven through real clicks and keys: a handler called directly passes when it is wired to nothing.</summary>
public class ComboBoxToggleTests : BunitContext
{
    // Deliberately not a KHost model: the box must not care what its rows are.
    private sealed record Song(string Title);

    private const string ToggleSelector = ".kh-combobox__toggle";
    private const string OptionSelector = ".kh-combobox__option";
    private const string MenuSelector = ".kh-combobox__menu";

    private readonly BunitJSModuleInterop _keys;
    private List<Song> _catalogue = [new("Jolene"), new("Respect")];
    private readonly List<string> _terms = [];

    public ComboBoxToggleTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _keys = JSInterop.SetupModule("/js/combobox-keys.js").SetupModule("init", _ => true);
    }

    private int Reveals => _keys.Invocations.Count(invocation => invocation.Identifier == "reveal");

    private bool? LastRevealWasWholeMenu => _keys.Invocations
        .LastOrDefault(invocation => invocation.Identifier == "reveal").Arguments?.FirstOrDefault() as bool?;

    private IRenderedComponent<ComboBox<Song>> RenderBox(bool openWhenEmpty = true, Song? value = null)
        => Render<ComboBox<Song>>(parameters => parameters
            .Add(c => c.DisplayName, song => song.Title)
            .Add(c => c.OpenWhenEmpty, openWhenEmpty)
            .Add(c => c.MinimumSearchLength, 0)
            .Add(c => c.Value, value)
            .Add(c => c.Search, term =>
            {
                _terms.Add(term);
                return Task.FromResult<IReadOnlyList<Song>>(_catalogue);
            }));

    // A free-text name box would otherwise offer to list every singer there is.
    [Fact]
    public void Toggle_OnABoxThatDoesNotListEverything_IsNotDrawn()
    {
        var combo = RenderBox(openWhenEmpty: false);

        Assert.Empty(combo.FindAll(ToggleSelector));
    }

    // The point of the chevron: a box holding a choice shows only that name, and typing over it
    // is the only other way to see what else there is.
    [Fact]
    public async Task Toggle_OverAChosenValue_ListsEverything()
    {
        var combo = RenderBox(value: new Song("Jolene"));

        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());

        Assert.Equal(_catalogue.Count, combo.FindAll(OptionSelector).Count);
        Assert.Equal("", _terms.Last());
        Assert.Equal("true", combo.Find(ToggleSelector).GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task Toggle_WhileOpen_ClosesTheMenu()
    {
        var combo = RenderBox();
        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());

        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());

        Assert.Empty(combo.FindAll(MenuSelector));
        Assert.Equal("false", combo.Find(ToggleSelector).GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task Search_FindingMoreThanTwelve_ShowsTwelveAndSaysToNarrow()
    {
        _catalogue = [.. Enumerable.Range(1, 13).Select(n => new Song($"Song {n}"))];
        var combo = RenderBox();

        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());

        Assert.Equal(12, combo.FindAll(OptionSelector).Count);
        Assert.Contains("type to narrow", combo.Find(MenuSelector).TextContent);
    }

    [Fact]
    public async Task Search_FindingExactlyTwelve_ShowsThemAllWithNoHint()
    {
        _catalogue = [.. Enumerable.Range(1, 12).Select(n => new Song($"Song {n}"))];
        var combo = RenderBox();

        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());

        Assert.Equal(12, combo.FindAll(OptionSelector).Count);
        Assert.DoesNotContain("type to narrow", combo.Find(MenuSelector).TextContent);
    }

    // Only three rows show, so the fourth press of ArrowDown would otherwise highlight a row
    // the host cannot see.
    [Fact]
    public async Task ArrowDown_AsksTheMenuToBringTheHighlightIntoView()
    {
        var combo = RenderBox();
        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());
        var before = Reveals;

        await combo.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal(before + 1, Reveals);
        Assert.False(LastRevealWasWholeMenu);
    }

    // In a scrolling dialog the menu can open below the fold, and its dismiss overlay keeps the
    // wheel from scrolling the dialog to it.
    [Fact]
    public async Task Opening_BringsTheWholeMenuIntoView()
    {
        var combo = RenderBox();

        await combo.Find(ToggleSelector).ClickAsync(new MouseEventArgs());

        Assert.True(LastRevealWasWholeMenu);
    }
}
