using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class FlashServiceTests : IDisposable
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly FlashService _flash;
    private readonly IDisposable _subscription;
    private int _changes;

    public FlashServiceTests()
    {
        _flash = new FlashService(_broker);
        _subscription = _broker.Subscribe<FlashChanged>(_ => _changes++);
    }

    public void Dispose() => _subscription.Dispose();

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

    /// <summary>Dismissing twice must not announce a change that did not happen.</summary>
    [Fact]
    public void Dismiss_WithNothingShowing_AnnouncesNothing()
    {
        _flash.Dismiss();

        Assert.Equal(0, _changes);
    }

    [Fact]
    public void ANewerMessage_BecomesCurrentWhileTheOlderStaysInTheStack()
    {
        _flash.Show("First.");
        _flash.Show("Second.");

        Assert.Equal("Second.", _flash.Current?.Text);
        Assert.Equal(["First.", "Second."], _flash.Messages.Select(m => m.Text));
    }

    /// <summary>The banner tells messages apart by identity, so the same words must still differ.</summary>
    [Fact]
    public void EachShow_IsADistinctMessage()
    {
        _flash.Show("Saved.");
        var first = _flash.Current;

        _flash.Show("Saved.");

        Assert.NotSame(first, _flash.Current);
        Assert.NotEqual(first?.Id, _flash.Current?.Id);
    }

    [Fact]
    public void Messages_WithNothingShown_IsEmpty()
    {
        Assert.Empty(_flash.Messages);
    }

    [Fact]
    public void Dismiss_ByMessage_RemovesOnlyThatOneAndAnnouncesIt()
    {
        _flash.Show("First.");
        _flash.Show("Second.");
        var first = _flash.Messages[0];
        _changes = 0;

        _flash.Dismiss(first);

        Assert.Equal(["Second."], _flash.Messages.Select(m => m.Text));
        Assert.Equal(1, _changes);
    }

    /// <summary>Dismissing an id no longer in the stack must not announce a change that did not
    /// happen, the same rule the no-arg overload already keeps.</summary>
    [Fact]
    public void Dismiss_ByMessage_AlreadyGone_AnnouncesNothing()
    {
        _flash.Show("First.");
        var first = _flash.Messages[0];
        _flash.Dismiss(first);
        _changes = 0;

        _flash.Dismiss(first);

        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Dismiss_Parameterless_RemovesTheMostRecentAndKeepsTheOlder()
    {
        _flash.Show("First.");
        _flash.Show("Second.");

        _flash.Dismiss();

        Assert.Equal(["First."], _flash.Messages.Select(m => m.Text));
    }
}
