using System.Reflection;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UnitTests.UserInterface.Components;

public class ComboBoxTests
{
    // Deliberately not a KHost model: the box must not care what its rows are.
    private sealed record Song(string Title);

    private readonly ComboBox<Song> _combo = new();

    private Song? _reported;
    private bool _wasReported;
    private int _searches;

    public ComboBoxTests()
    {
        SetParameter(nameof(ComboBox<Song>.DisplayName), (Func<Song, string>)(song => song.Title));
        SetParameter(nameof(ComboBox<Song>.Search),
            (Func<string, Task<IReadOnlyList<Song>>>)(_ =>
            {
                _searches++;
                return Task.FromResult<IReadOnlyList<Song>>([]);
            }));
        SetParameter(nameof(ComboBox<Song>.ValueChanged), new EventCallback<Song?>(
            null, (Action<Song?>)(song => { _reported = song; _wasReported = true; })));
    }

    [Fact]
    public void SettingAValue_ShowsItsName()
    {
        SetParameter(nameof(ComboBox<Song>.Value), new Song("Jolene"));
        ParametersSet();

        Assert.Equal("Jolene", Field<string>("_query"));
    }

    [Fact]
    public void SettingNoValue_ShowsAnEmptyField()
    {
        SetParameter(nameof(ComboBox<Song>.Value), null);
        ParametersSet();

        Assert.Equal("", Field<string>("_query"));
    }

    [Fact]
    public async Task Selecting_ReportsTheRowAndClosesTheMenu()
    {
        var song = new Song("Crazy");
        Open(song);

        await InvokeAsync("SelectAsync", song);

        Assert.Equal(song, _reported);
        Assert.Equal("Crazy", Field<string>("_query"));
        Assert.False(Field<bool>("_isOpen"));
    }

    [Fact]
    public async Task Enter_SelectsTheHighlightedRow()
    {
        var second = new Song("Respect");
        Open(new Song("At Last"), second);
        SetField("_highlighted", 1);

        await InvokeAsync("OnKeyDownAsync", new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(second, _reported);
    }

    [Fact]
    public async Task ArrowDown_PastTheLastRow_WrapsToTheFirst()
    {
        Open(new Song("At Last"), new Song("Respect"));
        SetField("_highlighted", 1);

        await InvokeAsync("OnKeyDownAsync", new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal(0, Field<int>("_highlighted"));
    }

    [Fact]
    public async Task ArrowUp_FromTheFirstRow_WrapsToTheLast()
    {
        Open(new Song("At Last"), new Song("Respect"));
        SetField("_highlighted", 0);

        await InvokeAsync("OnKeyDownAsync", new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Equal(1, Field<int>("_highlighted"));
    }

    [Fact]
    public async Task Escape_ClosesTheMenuWithoutChoosing()
    {
        Open(new Song("At Last"));

        await InvokeAsync("OnKeyDownAsync", new KeyboardEventArgs { Key = "Escape" });

        Assert.False(Field<bool>("_isOpen"));
        Assert.False(_wasReported);
    }

    // Editing the name of a chosen row must not leave that row bound, or a form saves the wrong one.
    [Fact]
    public async Task TypingOverAChosenRow_UnchoosesIt()
    {
        var song = new Song("Crazy");
        Open(song);
        await InvokeAsync("SelectAsync", song);
        _wasReported = false;

        await TypeAsync("Craz");

        Assert.True(_wasReported);
        Assert.Null(_reported);
        Assert.Null(_combo.Value);
    }

    // The parent hands the chosen row straight back; that echo must not rewrite the field.
    [Fact]
    public async Task AParentEchoingTheChosenRowBack_DoesNotRewriteTheField()
    {
        var song = new Song("Crazy");
        Open(song);
        await InvokeAsync("SelectAsync", song);
        SetField("_query", "half-typed");

        SetParameter(nameof(ComboBox<Song>.Value), song);
        ParametersSet();

        Assert.Equal("half-typed", Field<string>("_query"));
    }

    // With nothing to choose, Enter belongs to whatever surrounds the box — a form that accepts a
    // name matching no row.
    [Fact]
    public async Task Enter_WithNoMatches_ChoosesNothing()
    {
        SetField("_isOpen", true);
        SetField("_results", new List<Song>());

        await InvokeAsync("OnKeyDownAsync", new KeyboardEventArgs { Key = "Enter" });

        Assert.False(_wasReported);
        Assert.Null(_combo.Value);
    }

    [Fact]
    public void TextReplacedByTheCaller_DismissesTheMenu()
    {
        Open(new Song("Jolene"));

        SetParameter(nameof(ComboBox<Song>.Text), "");
        ParametersSet();

        Assert.False(Field<bool>("_isOpen"));
        Assert.Empty(Field<List<Song>>("_results"));
    }

    [Fact]
    public void ValueReplacedByTheCaller_DismissesTheMenu()
    {
        Open(new Song("Jolene"));

        SetParameter(nameof(ComboBox<Song>.Value), new Song("Crazy"));
        ParametersSet();

        Assert.False(Field<bool>("_isOpen"));
        Assert.Empty(Field<List<Song>>("_results"));
    }

    [Fact]
    public void MinimumSearchLength_DefaultsToThree()
    {
        Assert.Equal(3, _combo.MinimumSearchLength);
    }

    [Fact]
    public async Task Typing_FewerCharactersThanTheMinimum_ShowsNothingAndAsksNothing()
    {
        await TypeAsync("Jo");

        Assert.False(Field<bool>("_isOpen"));
        Assert.Equal(0, _searches);
    }

    [Fact]
    public async Task Typing_AsManyCharactersAsTheMinimum_OpensTheMenu()
    {
        await TypeAsync("Jol");

        Assert.True(Field<bool>("_isOpen"));
    }

    [Fact]
    public async Task DeletingBackUnderTheMinimum_ClosesTheMenuAndDropsTheRows()
    {
        Open(new Song("Jolene"));

        await TypeAsync("Jo");

        Assert.False(Field<bool>("_isOpen"));
        Assert.Empty(Field<List<Song>>("_results"));
    }

    // Cancelling a spent token source a second time throws out of DisposeAsync, killing the circuit.
    [Fact]
    public async Task Disposing_AfterFallingBackUnderTheMinimum_DoesNotThrow()
    {
        var searching = InvokeAsync("OnQueryChangedAsync", new ChangeEventArgs { Value = "Jol" });
        await InvokeAsync("OnQueryChangedAsync", new ChangeEventArgs { Value = "Jo" });
        await searching;

        await _combo.DisposeAsync();
    }

    // Disposing releases the debounce delay, so the search — and the render it asks for, which
    // needs a renderer these tests do not have — never runs.
    private async Task TypeAsync(string text)
    {
        var typing = InvokeAsync("OnQueryChangedAsync", new ChangeEventArgs { Value = text });
        await _combo.DisposeAsync();
        await typing;
    }

    [Fact]
    public void WithoutAGroupName_NoRowStartsAGroup()
    {
        Open(new Song("At Last"), new Song("Respect"));

        Assert.False(StartsGroup(0));
        Assert.False(StartsGroup(1));
    }

    // Rows arrive already sorted, so a heading belongs wherever the group changes — and only there.
    [Fact]
    public void WithAGroupName_AHeadingStartsEachRunOfRows()
    {
        GroupBy(song => song.Title.StartsWith('A') ? "Here" : "Elsewhere");
        Open(new Song("At Last"), new Song("Alfie"), new Song("Respect"), new Song("Rehab"));

        Assert.True(StartsGroup(0));
        Assert.False(StartsGroup(1));
        Assert.True(StartsGroup(2));
        Assert.False(StartsGroup(3));
    }

    [Fact]
    public void WithAGroupName_ARunThatReturnsNothing_GetsNoHeading()
    {
        GroupBy(song => song.Title.StartsWith('A') ? null : "Elsewhere");
        Open(new Song("At Last"), new Song("Respect"));

        Assert.False(StartsGroup(0));
        Assert.True(StartsGroup(1));
    }

    [Fact]
    public void GroupOf_IsTheHeadingText()
    {
        GroupBy(_ => "Here");
        Open(new Song("At Last"));

        Assert.Equal("Here", GroupOf(0));
    }

    private void GroupBy(Func<Song, string?> group)
        => SetParameter(nameof(ComboBox<Song>.GroupName), group);

    private bool StartsGroup(int index)
        => (bool)typeof(ComboBox<Song>)
            .GetMethod("StartsGroup", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_combo, [index])!;

    private string? GroupOf(int index)
        => (string?)typeof(ComboBox<Song>)
            .GetMethod("GroupOf", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_combo, [index]);

    private void Open(params Song[] results)
    {
        SetField("_results", results.ToList());
        SetField("_isOpen", true);
    }

    private void SetParameter(string name, object? value)
        => typeof(ComboBox<Song>).GetProperty(name)!.SetValue(_combo, value);

    private void SetField(string name, object? value)
        => typeof(ComboBox<Song>)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_combo, value);

    private T Field<T>(string name)
        => (T)typeof(ComboBox<Song>)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_combo)!;

    private void ParametersSet()
        => typeof(ComboBox<Song>)
            .GetMethod("OnParametersSet", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_combo, null);

    private Task InvokeAsync(string method, params object?[] args)
        => (Task)typeof(ComboBox<Song>)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_combo, args)!;
}
