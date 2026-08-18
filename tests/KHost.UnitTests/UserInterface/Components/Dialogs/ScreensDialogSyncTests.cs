using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.UserInterface.Components.Dialogs;
using NSubstitute;
using System.Reflection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class ScreensDialogSyncTests
{
    private readonly IScreenCoordinationService _coordination = Substitute.For<IScreenCoordinationService>();
    private readonly ScreensDialog _dialog = new();

    public ScreensDialogSyncTests()
    {
        typeof(ScreensDialog)
            .GetProperty("ScreenCoordination", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_dialog, _coordination);

        _coordination.PrimaryScreenId.Returns("Main");
    }

    [Fact]
    public void GetSyncStatus_ForAScreenThatCannotSync_ReportsNotSupported()
    {
        var cast = Screen("Living Room", ScreenCapabilities.CastDevice);

        Assert.Equal(ScreensDialog.SyncStatus.NotSupported, _dialog.GetSyncStatus(cast));
    }

    [Fact]
    public void GetSyncStatus_ForThePrimary_ReportsReference()
    {
        // The primary is what the others are measured against, so it is never "out of sync".
        Assert.Equal(ScreensDialog.SyncStatus.Reference, _dialog.GetSyncStatus(Screen("Main")));
    }

    [Fact]
    public void GetSyncStatus_BeforeAnythingPlays_IsUnknown()
    {
        Assert.Equal(ScreensDialog.SyncStatus.Unknown, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Fact]
    public void GetSyncStatus_WhenTheScreenTracksThePrimary_ReportsInSync()
    {
        var now = DateTime.UtcNow;
        Report("Main", TimeSpan.FromSeconds(30), now);
        Report("Second", TimeSpan.FromSeconds(30.05), now);

        Assert.Equal(ScreensDialog.SyncStatus.InSync, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(-0.4)]
    public void GetSyncStatus_WhenTheScreenIsOffThePrimary_ReportsDrifting(double offsetSeconds)
    {
        var now = DateTime.UtcNow;
        Report("Main", TimeSpan.FromSeconds(30), now);
        Report("Second", TimeSpan.FromSeconds(30 + offsetSeconds), now);

        Assert.Equal(ScreensDialog.SyncStatus.Drifting, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Fact]
    public void GetSyncStatus_RollsEachPositionForwardFromItsOwnSampleTime()
    {
        var now = DateTime.UtcNow;

        // Same playback point, reported a second apart. Comparing the raw positions would call this
        // a full second of drift; projecting each from its own sample time cancels it out.
        Report("Main", TimeSpan.FromSeconds(30), now - TimeSpan.FromSeconds(1));
        Report("Second", TimeSpan.FromSeconds(31), now);

        Assert.Equal(ScreensDialog.SyncStatus.InSync, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Fact]
    public void GetSyncStatus_WithoutASampleTime_IsUnknownRatherThanDrifting()
    {
        var now = DateTime.UtcNow;
        Report("Main", TimeSpan.FromSeconds(30), now);
        Report("Second", TimeSpan.FromSeconds(30), sampledAtUtc: null);

        // The report carries an unmeasured delivery latency, which would read as drift it does not have.
        Assert.Equal(ScreensDialog.SyncStatus.Unknown, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Fact]
    public void GetSyncStatus_WhenTheScreenIsPaused_IsUnknown()
    {
        var now = DateTime.UtcNow;
        Report("Main", TimeSpan.FromSeconds(30), now);
        Report("Second", TimeSpan.FromSeconds(30), now, isPlaying: false);

        Assert.Equal(ScreensDialog.SyncStatus.Unknown, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Fact]
    public void GetSyncStatus_WithNoPrimaryElected_IsUnknown()
    {
        _coordination.PrimaryScreenId.Returns((string?)null);
        Report("Second", TimeSpan.FromSeconds(30), DateTime.UtcNow);

        Assert.Equal(ScreensDialog.SyncStatus.Unknown, _dialog.GetSyncStatus(Screen("Second")));
    }

    [Fact]
    public void SyncTitle_SaysWhichSideOfThePrimaryTheScreenIsOn()
    {
        var now = DateTime.UtcNow;
        Report("Main", TimeSpan.FromSeconds(30), now);
        Report("Second", TimeSpan.FromSeconds(30.5), now);

        Assert.Contains("ahead", _dialog.SyncTitle(Screen("Second")));

        Report("Second", TimeSpan.FromSeconds(29.5), now);
        Assert.Contains("behind", _dialog.SyncTitle(Screen("Second")));
    }

    [Fact]
    public void SyncIcon_IsTheSameGlyphForEveryLiveState()
    {
        var now = DateTime.UtcNow;
        var second = Screen("Second");

        Report("Main", TimeSpan.FromSeconds(30), now);
        var idle = _dialog.SyncIcon(second);

        Report("Second", TimeSpan.FromSeconds(30), now);
        var inSync = _dialog.SyncIcon(second);

        Report("Second", TimeSpan.FromSeconds(31), now);
        var drifting = _dialog.SyncIcon(second);

        // Colour carries the state; a glyph that changed with it would read as four unrelated marks.
        Assert.Equal("bi-diagram-3", idle);
        Assert.Equal(idle, inSync);
        Assert.Equal(idle, drifting);
        Assert.Equal(idle, _dialog.SyncIcon(Screen("Main")));
    }

    [Fact]
    public void SyncIcon_ForAScreenThatCannotSync_IsADifferentGlyph()
    {
        // Not a worse value of the same fact — this screen has no timeline to be on, ever.
        Assert.Equal("bi-slash-circle", _dialog.SyncIcon(Screen("Living Room", ScreenCapabilities.CastDevice)));
    }

    private void Report(string screenId, TimeSpan position, DateTime? sampledAtUtc, bool isPlaying = true)
        => _dialog._screenStates[screenId] = new ScreenPlaybackState
        {
            LoadedFilePath = "song.mp4",
            IsPlaying = isPlaying,
            Position = position,
            Duration = TimeSpan.FromMinutes(4),
            SampledAtUtc = sampledAtUtc,
        };

    private static IScreenConnection Screen(string screenId, ScreenCapabilities? capabilities = null)
    {
        var screen = Substitute.For<IScreenConnection>();
        screen.ScreenId.Returns(screenId);
        screen.Capabilities.Returns(capabilities ?? new ScreenCapabilities { SupportsSync = true, SupportsAudio = true, SupportsVideo = true });
        return screen;
    }
}
