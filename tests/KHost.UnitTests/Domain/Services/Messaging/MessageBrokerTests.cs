using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Messaging;

public class MessageBrokerTests
{
    private sealed record SongStarted(string Title);
    private sealed record SongEnded(string Title);

    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    [Fact]
    public async Task PublishAsync_DeliversToASubscriber()
    {
        string? seen = null;
        _broker.Subscribe<SongStarted>(message => seen = message.Title);

        await _broker.PublishAsync(new SongStarted("Bohemian Rhapsody"));

        Assert.Equal("Bohemian Rhapsody", seen);
    }

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNothing()
        => await _broker.PublishAsync(new SongStarted("Nobody Listening"));

    [Fact]
    public async Task PublishAsync_DeliversOnlyToHandlersOfThatType()
    {
        var started = 0;
        var ended = 0;
        _broker.Subscribe<SongStarted>(_ => started++);
        _broker.Subscribe<SongEnded>(_ => ended++);

        await _broker.PublishAsync(new SongStarted("One"));

        Assert.Equal(1, started);
        Assert.Equal(0, ended);
    }

    // The reason this exists rather than an event: a publisher has to be able to wait for what its
    // handlers did before deciding what happens next.
    [Fact]
    public async Task PublishAsync_WaitsForAnAsyncHandlerToFinish()
    {
        var finished = false;
        _broker.Subscribe<SongEnded>(async (_, _) =>
        {
            await Task.Delay(30);
            finished = true;
        });

        await _broker.PublishAsync(new SongEnded("Slow"));

        Assert.True(finished);
    }

    [Fact]
    public async Task PublishAsync_RunsHandlersOneAtATimeInSubscriptionOrder()
    {
        var order = new List<string>();

        _broker.Subscribe<SongEnded>(async (_, _) =>
        {
            await Task.Delay(30);
            order.Add("first");
        });
        _broker.Subscribe<SongEnded>(_ => order.Add("second"));

        await _broker.PublishAsync(new SongEnded("Ordered"));

        Assert.Equal(["first", "second"], order);
    }

    // A broken subscriber must not take the publisher down with it, or one bad handler stops the
    // queue moving on to the next singer.
    [Fact]
    public async Task PublishAsync_AHandlerThatThrows_DoesNotStopTheRest()
    {
        var reached = false;
        _broker.Subscribe<SongEnded>(_ => throw new InvalidOperationException("boom"));
        _broker.Subscribe<SongEnded>(_ => reached = true);

        await _broker.PublishAsync(new SongEnded("Broken"));

        Assert.True(reached);
    }

    [Fact]
    public async Task PublishAsync_CancellationReachesTheHandlerAndPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _broker.Subscribe<SongEnded>((_, token) => Task.FromCanceled(token));

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _broker.PublishAsync(new SongEnded("Cancelled"), cancellation.Token));
    }

    [Fact]
    public async Task DisposingASubscription_StopsDelivery()
    {
        var received = 0;
        var subscription = _broker.Subscribe<SongStarted>(_ => received++);

        await _broker.PublishAsync(new SongStarted("First"));
        subscription.Dispose();
        await _broker.PublishAsync(new SongStarted("Second"));

        Assert.Equal(1, received);
    }

    // Two handlers written the same way are still two subscriptions; dropping one must not take
    // the other's delivery with it.
    [Fact]
    public async Task DisposingOneOfTwoIdenticalSubscriptions_LeavesTheOtherDelivering()
    {
        var received = 0;
        var first = _broker.Subscribe<SongStarted>(_ => received++);
        _broker.Subscribe<SongStarted>(_ => received++);

        first.Dispose();
        await _broker.PublishAsync(new SongStarted("Only One Left"));

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task DisposingASubscriptionTwice_DoesNotRemoveAnother()
    {
        var received = 0;
        var first = _broker.Subscribe<SongStarted>(_ => received++);
        _broker.Subscribe<SongStarted>(_ => received++);

        first.Dispose();
        first.Dispose();
        await _broker.PublishAsync(new SongStarted("Still One Left"));

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task Subscribe_DuringDelivery_DoesNotDisturbTheRunInFlight()
    {
        var late = 0;
        _broker.Subscribe<SongStarted>(_ => _broker.Subscribe<SongStarted>(__ => late++));

        await _broker.PublishAsync(new SongStarted("First"));

        Assert.Equal(0, late);
    }

    [Fact]
    public void Subscribe_WithNoHandler_Throws()
        => Assert.Throws<ArgumentNullException>(() => _broker.Subscribe<SongStarted>((Action<SongStarted>)null!));
}
