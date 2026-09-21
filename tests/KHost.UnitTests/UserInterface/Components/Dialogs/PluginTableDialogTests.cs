using Bunit;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Draws a table a plugin described. The plugin ships no markup, so everything here comes
/// from the request it handed over.</summary>
public class PluginTableDialogTests : BunitContext
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private int _reads;
    private PluginTableContent _content = new();

    public PluginTableDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    private static PluginTableColumn[] Columns =>
        [new() { Key = "name", Header = "Receiver" }, new() { Key = "status", Header = "" }];

    private static PluginTableRow Row(string id, string name, bool current = false, params PluginTableAction[] actions)
        => new()
        {
            Id = id,
            IsCurrent = current,
            Fields = new Dictionary<string, string> { ["name"] = name, ["status"] = current ? "Showing" : "" },
            Actions = actions,
        };

    private IRenderedComponent<PluginTableDialog> Render(string? note = null)
        => Render<PluginTableDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Request, new ShowPluginTableRequest
            {
                Title = "Chromecast devices",
                Note = note,
                Columns = Columns,
                LoadAsync = _ =>
                {
                    _reads++;
                    return Task.FromResult(_content);
                },
            }));

    [Fact]
    public void Render_DrawsAColumnPerDeclaredColumn_AndACellPerField()
    {
        _content = new PluginTableContent { Rows = [Row("d1", "Living Room")] };

        var dialog = Render();

        Assert.Equal(["Receiver", ""], dialog.FindAll("thead th").Select(th => th.TextContent.Trim()));
        Assert.Contains("Living Room", dialog.Find("tbody tr").TextContent);
    }

    [Fact]
    public void Render_ShowsTheEmptyMessageTheContentCarries_RatherThanAnEmptyTable()
    {
        _content = new PluginTableContent { Rows = [], EmptyMessage = "Not searching for devices." };

        var dialog = Render();

        Assert.Equal("Not searching for devices.", dialog.Find(".kh-plugin-table__empty").TextContent.Trim());
        Assert.Empty(dialog.FindAll("table"));
    }

    [Fact]
    public void Render_DrawsNoActionsColumn_WhenNoRowOffersOne()
    {
        _content = new PluginTableContent { Rows = [Row("d1", "Living Room")] };

        // A header over nothing is furniture.
        Assert.Single(Render().FindAll("thead th"), th => th.TextContent.Trim() == "Receiver");
        Assert.Empty(Render().FindAll(".kh-table__cell--actions"));
    }

    [Fact]
    public async Task ClickingARowAction_RunsWhatThePluginHandedOver()
    {
        var ran = 0;
        _content = new PluginTableContent
        {
            Rows = [Row("d1", "Living Room", current: false, new PluginTableAction
            {
                DisplayName = "Show here",
                PerformAsync = _ => { ran++; return Task.CompletedTask; },
            })],
        };

        var dialog = Render();
        await dialog.Find(".kh-table__cell--actions button").ClickAsync(new());

        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task ClickingAnAction_RereadsTheTable_SoTheRowsFollowWhatItDid()
    {
        _content = new PluginTableContent
        {
            Rows = [Row("d1", "Living Room", current: false, new PluginTableAction
            {
                DisplayName = "Show here",
                PerformAsync = _ => Task.CompletedTask,
            })],
        };

        var dialog = Render();
        var readsAfterOpen = _reads;

        await dialog.Find(".kh-table__cell--actions button").ClickAsync(new());

        Assert.Equal(readsAfterOpen + 1, _reads);
    }

    [Fact]
    public async Task AnActionThatThrows_LeavesTheDialogUp_SoAnotherRowCanBeTried()
    {
        _content = new PluginTableContent
        {
            Rows = [Row("d1", "Living Room", current: false, new PluginTableAction
            {
                DisplayName = "Show here",
                PerformAsync = _ => throw new InvalidOperationException("no route to host"),
            })],
        };

        var dialog = Render();
        await dialog.Find(".kh-table__cell--actions button").ClickAsync(new());

        Assert.Equal("Show here did not work.", dialog.Find(".kh-plugin-table__warning").TextContent.Trim());
        Assert.NotEmpty(dialog.FindAll("tbody tr"));
    }

    [Fact]
    public async Task PluginTableChanged_RereadsTheTable_SoASweepFillsItWhileItIsOpen()
    {
        _content = new PluginTableContent { Rows = [], EmptyMessage = "Looking for devices…" };

        var dialog = Render();
        _content = new PluginTableContent { Rows = [Row("d1", "Living Room")] };

        await _broker.PublishAsync(new PluginTableChanged());

        dialog.WaitForAssertion(() => Assert.Contains("Living Room", dialog.Find("tbody tr").TextContent));
    }

    [Fact]
    public void Render_MarksTheRowInEffect_SoItReadsApartFromTheRest()
    {
        _content = new PluginTableContent { Rows = [Row("d1", "Living Room", current: true)] };

        Assert.Contains("kh-plugin-table__row--current", Render().Find("tbody tr").GetAttribute("class"));
    }

    [Fact]
    public void Render_DrawsTheNote_OnlyWhenTheRequestCarriesOne()
    {
        _content = new PluginTableContent { Rows = [Row("d1", "Living Room")] };

        Assert.Empty(Render().FindAll(".kh-note"));
        Assert.Equal("Always unsynced.", Render(note: "Always unsynced.").Find(".kh-note").TextContent.Trim());
    }

    [Fact]
    public void Render_DrawsTableActionsInTheFooter_PressedWhenTheyAreActive()
    {
        _content = new PluginTableContent
        {
            Rows = [],
            Actions = [new PluginTableAction
            {
                DisplayName = "Searching",
                IsActive = true,
                PerformAsync = _ => Task.CompletedTask,
            }],
        };

        var button = Render().Find(".kh-dialog__footer button");

        Assert.Contains("Searching", button.TextContent);
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
    }
}
