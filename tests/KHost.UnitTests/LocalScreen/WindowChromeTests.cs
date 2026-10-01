using System.Text.Json;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Edges = KHost.LocalScreen.WindowChrome.Edges;

namespace KHost.UnitTests.LocalScreen;

public class WindowChromeTests
{
    private static readonly WindowBounds Windowed = new(100, 80, 1200, 700);
    private static readonly WindowBounds WorkArea = new(0, 25, 1920, 1055);

    private readonly IScreenWindow _window = Substitute.For<IScreenWindow>();
    private readonly List<string> _sent = [];
    private readonly WindowChrome _chrome;

    public WindowChromeTests()
    {
        _window.Bounds.Returns(Windowed);
        _window.WorkArea.Returns(WorkArea);
        _chrome = new WindowChrome(_window, NullLogger.Instance) { SendToPage = _sent.Add };
    }

    [Fact]
    public void Handle_Minimise_MinimisesTheWindow()
    {
        Assert.True(Send(new { type = "window-minimise" }));

        _window.Received(1).Minimise();
    }

    [Fact]
    public void Handle_Close_ClosesTheWindow()
    {
        Assert.True(Send(new { type = "window-close" }));

        _window.Received(1).Close();
    }

    [Fact]
    public void Handle_PlayerMessage_IsNotTheTitleBars()
    {
        Assert.False(Send(new { type = "state" }));
        Assert.False(Send(new { type = "toggle-fullscreen" }));
    }

    [Fact]
    public void Handle_Maximise_FillsTheWorkAreaAndSaysSo()
    {
        Send(new { type = "window-maximise" });

        _window.Received(1).SetBounds(WorkArea);
        Assert.True(_chrome.IsMaximised);
        Assert.True(LastState().GetProperty("maximised").GetBoolean());
    }

    [Fact]
    public void Handle_MaximiseTwice_RestoresThePlaceItHad()
    {
        Send(new { type = "window-maximise" });
        _window.Bounds.Returns(WorkArea);

        Send(new { type = "window-maximise" });

        _window.Received(1).SetBounds(Windowed);
        Assert.False(_chrome.IsMaximised);
        Assert.False(LastState().GetProperty("maximised").GetBoolean());
    }

    [Fact]
    public void Handle_MaximiseInFullScreen_LeavesTheWindowAlone()
    {
        _chrome.FullScreen = true;

        Send(new { type = "window-maximise" });

        _window.DidNotReceive().SetBounds(Arg.Any<WindowBounds>());
        Assert.False(_chrome.IsMaximised);
    }

    [Fact]
    public void FullScreen_Set_TellsThePageToHideTheBar()
    {
        _chrome.FullScreen = true;
        Assert.True(LastState().GetProperty("fullScreen").GetBoolean());

        _chrome.FullScreen = false;
        Assert.False(LastState().GetProperty("fullScreen").GetBoolean());
    }

    [Fact]
    public void PublishState_BeforeThePageIsReady_SendsNothing()
    {
        var chrome = new WindowChrome(_window, NullLogger.Instance);

        // A send into a web view with no page is a native crash; null is how "not yet" is said.
        chrome.FullScreen = true;

        Assert.Empty(_sent);
    }

    [Fact]
    public void Handle_DragOnTheBar_MovesTheWindowByThePointersTravel()
    {
        Send(new { type = "window-drag", phase = "start", screenX = 500, screenY = 90, clientX = 400, innerWidth = 1200 });
        Send(new { type = "window-drag", phase = "move", screenX = 530.4, screenY = 70 });

        _window.Received(1).SetLocation(130, 60);
    }

    [Fact]
    public void Handle_DragOnAScaledDisplay_ConvertsThePagesPixels()
    {
        // A window placed in device pixels at 200%: the page is half as wide as the window.
        _window.Bounds.Returns(Windowed with { Width = 2400 });

        Send(new { type = "window-drag", phase = "start", screenX = 500, screenY = 90, clientX = 400, innerWidth = 1200 });
        Send(new { type = "window-drag", phase = "move", screenX = 510, screenY = 95 });

        _window.Received(1).SetLocation(120, 90);
    }

    [Fact]
    public void Handle_MoveWithNoDragStarted_DoesNothing()
    {
        Send(new { type = "window-drag", phase = "move", screenX = 510, screenY = 95 });

        _window.DidNotReceive().SetLocation(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void Handle_MoveAfterTheDragEnded_DoesNothing()
    {
        Send(new { type = "window-drag", phase = "start", screenX = 500, screenY = 90, clientX = 400, innerWidth = 1200 });
        Send(new { type = "window-drag", phase = "end" });
        Send(new { type = "window-drag", phase = "move", screenX = 510, screenY = 95 });

        _window.DidNotReceive().SetLocation(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void Handle_DragInFullScreen_LeavesTheWindowAlone()
    {
        _chrome.FullScreen = true;

        Send(new { type = "window-drag", phase = "start", screenX = 500, screenY = 90, clientX = 400, innerWidth = 1200 });
        Send(new { type = "window-drag", phase = "move", screenX = 510, screenY = 95 });

        _window.DidNotReceive().SetLocation(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void FullScreen_MidDrag_EndsTheDrag()
    {
        Send(new { type = "window-drag", phase = "start", screenX = 500, screenY = 90, clientX = 400, innerWidth = 1200 });
        _chrome.FullScreen = true;
        _chrome.FullScreen = false;

        Send(new { type = "window-drag", phase = "move", screenX = 510, screenY = 95 });

        _window.DidNotReceive().SetLocation(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void Handle_DragAMaximisedWindow_RestoresItUnderThePointer()
    {
        Send(new { type = "window-maximise" });
        _window.Bounds.Returns(WorkArea);
        _sent.Clear();

        // Grabbed three quarters of the way along a 1920-wide bar.
        Send(new { type = "window-drag", phase = "start", screenX = 1440, screenY = 40, clientX = 1440, innerWidth = 1920 });

        _window.Received(1).SetBounds(new WindowBounds(1440 - 900, WorkArea.Top, 1200, 700));
        Assert.False(_chrome.IsMaximised);
        Assert.False(LastState().GetProperty("maximised").GetBoolean());
    }

    [Fact]
    public void Handle_PullAnEdge_ResizesFromThatEdge()
    {
        Send(new { type = "window-drag", phase = "start", edge = "se", screenX = 1300, screenY = 780, innerWidth = 1200 });
        Send(new { type = "window-drag", phase = "move", screenX = 1350, screenY = 800 });

        _window.Received(1).SetBounds(new WindowBounds(100, 80, 1250, 720));
        _window.DidNotReceive().SetLocation(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void Handle_PullAnEdgeWhileMaximised_LeavesTheWindowAlone()
    {
        Send(new { type = "window-maximise" });
        _window.ClearReceivedCalls();

        Send(new { type = "window-drag", phase = "start", edge = "e", screenX = 1900, screenY = 500, innerWidth = 1920 });
        Send(new { type = "window-drag", phase = "move", screenX = 1800, screenY = 500 });

        _window.DidNotReceive().SetBounds(Arg.Any<WindowBounds>());
        Assert.True(_chrome.IsMaximised);
    }

    [Theory]
    [InlineData("e", 50, 0, 100, 80, 1250, 700)]
    [InlineData("w", 50, 0, 150, 80, 1150, 700)]
    [InlineData("s", 0, 40, 100, 80, 1200, 740)]
    [InlineData("n", 0, 40, 100, 120, 1200, 660)]
    [InlineData("nw", -10, -20, 90, 60, 1210, 720)]
    [InlineData("w", 5000, 0, 100 + 1200 - WindowChrome.MinimumWidth, 80, WindowChrome.MinimumWidth, 700)]
    [InlineData("n", 0, 5000, 100, 80 + 700 - WindowChrome.MinimumHeight, 1200, WindowChrome.MinimumHeight)]
    [InlineData("e", -5000, 0, 100, 80, WindowChrome.MinimumWidth, 700)]
    [InlineData("s", 0, -5000, 100, 80, 1200, WindowChrome.MinimumHeight)]
    public void Resize_FromAnEdge_HoldsTheOppositeEdgesStill(
        string edge, int dx, int dy, int left, int top, int width, int height)
    {
        var resized = WindowChrome.Resize(Windowed, WindowChrome.ParseEdge(edge), dx, dy);

        Assert.Equal(new WindowBounds(left, top, width, height), resized);
    }

    [Theory]
    [InlineData("n", (int)(Edges.Top))]
    [InlineData("s", (int)(Edges.Bottom))]
    [InlineData("w", (int)(Edges.Left))]
    [InlineData("e", (int)(Edges.Right))]
    [InlineData("ne", (int)(Edges.Top | Edges.Right))]
    [InlineData("sw", (int)(Edges.Bottom | Edges.Left))]
    [InlineData(null, (int)(Edges.None))]
    public void ParseEdge_CompassName_NamesTheEdges(string? name, int expected)
        => Assert.Equal((Edges)expected, WindowChrome.ParseEdge(name));

    [Theory]
    [InlineData(1200, 1200, 1.0)]
    [InlineData(2400, 1200, 2.0)]
    [InlineData(1200, 0, 1.0)]
    public void UnitsPerCssPixel_WindowAgainstPage_IsTheirRatio(int windowWidth, double pageWidth, double expected)
        => Assert.Equal(expected, WindowChrome.UnitsPerCssPixel(windowWidth, pageWidth));

    private bool Send(object message)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        return _chrome.Handle(document.RootElement);
    }

    private JsonElement LastState()
    {
        Assert.NotEmpty(_sent);
        var root = JsonDocument.Parse(_sent[^1]).RootElement;
        Assert.Equal("window-state", root.GetProperty("type").GetString());
        return root;
    }
}
