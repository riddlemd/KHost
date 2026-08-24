using KHost.Abstractions.Models;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class FlashServiceTests
{
    private readonly FlashService _flash = new();
    private int _changes;

    public FlashServiceTests() => _flash.StateChanged += (_, _) => _changes++;

    [Fact]
    public void Show_PublishesTheMessageAndAnnouncesIt()
    {
        _flash.Show("Saved.");

        Assert.Equal("Saved.", _flash.Current?.Text);
        Assert.Equal(FlashType.Success, _flash.Current?.Type);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Show_CarriesTheKind()
    {
        _flash.Show("Careful.", FlashType.Warning);

        Assert.Equal(FlashType.Warning, _flash.Current?.Type);
    }

    [Fact]
    public void Dismiss_ClearsTheMessageAndAnnouncesIt()
    {
        _flash.Show("Saved.");
        _changes = 0;

        _flash.Dismiss();

        Assert.Null(_flash.Current);
        Assert.Equal(1, _changes);
    }

    /// <summary>
    /// Dismissing twice would otherwise announce a change that did not happen, and every banner in
    /// the app re-renders for it.
    /// </summary>
    [Fact]
    public void Dismiss_WithNothingShowing_AnnouncesNothing()
    {
        _flash.Dismiss();

        Assert.Equal(0, _changes);
    }

    [Fact]
    public void ANewerMessage_ReplacesTheOneShowing()
    {
        _flash.Show("First.");
        _flash.Show("Second.");

        Assert.Equal("Second.", _flash.Current?.Text);
    }

    /// <summary>
    /// The banner tells one message from another by identity to decide whether its countdown is
    /// still the current one, so re-showing the same words has to be a different message.
    /// </summary>
    [Fact]
    public void EachShow_IsADistinctMessage()
    {
        _flash.Show("Saved.");
        var first = _flash.Current;

        _flash.Show("Saved.");

        Assert.NotSame(first, _flash.Current);
    }
}
