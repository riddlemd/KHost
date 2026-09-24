using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models.Plugins;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Interactions.Handlers;

public class ShowPluginTableDialogHandlerTests
{
    private static ShowPluginTableRequest MakeRequest() => new()
    {
        Title = "Table",
        Columns = [],
        LoadAsync = _ => Task.FromResult(new PluginTableContent()),
    };

    [Fact]
    public async Task HandleAsync_CancelledAfterRequestIsShown_CompletesAsCancelled()
    {
        var dialogService = Substitute.For<IDialogService>();
        var handler = new ShowPluginTableDialogHandler(dialogService);
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
        var handler = new ShowPluginTableDialogHandler(dialogService);

        await handler.HandleAsync(MakeRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }
}
