using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Interactions.Handlers;

public class TextPromptDialogHandlerTests
{
    private static TextPromptRequest MakeRequest()
        => new("Title", null, []);

    [Fact]
    public async Task HandleAsync_CancelledAfterRequestIsShown_CompletesAsCancelled()
    {
        // The substitute never calls back, so the task can only settle through cancellation.
        var dialogService = Substitute.For<IDialogService>();
        var handler = new TextPromptDialogHandler(dialogService);
        using var cts = new CancellationTokenSource();

        var task = handler.HandleAsync(MakeRequest(), cts.Token);
        Assert.False(task.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task HandleAsync_NoConsoleOpen_CompletesRatherThanHanging()
    {
        var dialogService = new DialogService(NullLogger<DialogService>.Instance, []);
        var handler = new TextPromptDialogHandler(dialogService);

        var result = await handler.HandleAsync(MakeRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(result);
    }
}
