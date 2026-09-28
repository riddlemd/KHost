using Bunit;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>A menu left up after its field lost focus sits over the queue, and a click aimed at
/// the queue picks one of its rows instead: that is how a singer was once added by accident.</summary>
public class ComboBoxDismissTests : BunitContext
{
    private const string InputSelector = "input.kh-combobox__input";
    private const string MenuSelector = ".kh-combobox__menu";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource _searchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<IReadOnlyList<string>> _results = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _picks;

    public ComboBoxDismissTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public async Task Blur_WhileASearchIsInFlight_TheAnswerDoesNotReopenTheMenu()
    {
        var cut = RenderBox(_ => SlowSearchAsync());
        var typing = cut.Find(InputSelector).InputAsync(new ChangeEventArgs { Value = "Zo" });
        await _searchStarted.Task.WaitAsync(Wait);

        await cut.Find(InputSelector).FocusOutAsync(new FocusEventArgs());
        _results.SetResult(["Zoe"]);
        await typing.WaitAsync(Wait);

        Assert.Empty(cut.FindAll(MenuSelector));
    }

    [Fact]
    public async Task Escape_WhileASearchIsInFlight_TheAnswerDoesNotReopenTheMenu()
    {
        var cut = RenderBox(_ => SlowSearchAsync());
        var typing = cut.Find(InputSelector).InputAsync(new ChangeEventArgs { Value = "Zo" });
        await _searchStarted.Task.WaitAsync(Wait);

        await cut.Find(InputSelector).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        _results.SetResult(["Zoe"]);
        await typing.WaitAsync(Wait);

        Assert.Empty(cut.FindAll(MenuSelector));
    }

    [Fact]
    public async Task ClickOutside_ClosesTheMenuWithoutPickingARow()
    {
        var cut = await OpenMenuAsync();

        await cut.Find(".kh-combobox__overlay").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(MenuSelector));
        Assert.Equal(0, _picks);
    }

    [Fact]
    public async Task ClickOnARow_StillPicksIt()
    {
        var cut = await OpenMenuAsync();

        await cut.Find(".kh-combobox__option").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, _picks);
    }

    /// <summary>Awaits the whole keystroke, debounce and search included, so no render is still
    /// pending when the test clicks.</summary>
    private async Task<IRenderedComponent<ComboBox<string>>> OpenMenuAsync()
    {
        var cut = RenderBox(_ => Task.FromResult<IReadOnlyList<string>>(["Zoe"]));
        await cut.Find(InputSelector).InputAsync(new ChangeEventArgs { Value = "Zo" });
        Assert.NotEmpty(cut.FindAll(".kh-combobox__option"));
        return cut;
    }

    private IRenderedComponent<ComboBox<string>> RenderBox(Func<string, Task<IReadOnlyList<string>>> search)
        => Render<ComboBox<string>>(parameters => parameters
            .Add(p => p.Search, search)
            .Add(p => p.DisplayName, (Func<string, string>)(name => name))
            .Add(p => p.MinimumSearchLength, 1)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, (string? _) => _picks++)));

    private Task<IReadOnlyList<string>> SlowSearchAsync()
    {
        _searchStarted.TrySetResult();
        return _results.Task;
    }
}
