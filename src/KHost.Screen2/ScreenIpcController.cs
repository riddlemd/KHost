using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Screen2;

internal sealed class ScreenIpcController : IAsyncDisposable
{
    private readonly IScreenClient _client;

    // Concrete: loading a host stream is not a file load, so it has no place on IMediaPlayer.
    private readonly StreamMediaPlayer _player;

    private readonly ILogger<ScreenIpcController> _logger;

    // Serialized so a load settles before a following Play, rather than racing on the page.
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    public ScreenIpcController(IScreenClient client, StreamMediaPlayer player, ILogger<ScreenIpcController> logger)
    {
        _client = client;
        _player = player;
        _logger = logger;

        _client.CommandReceived += OnCommandReceived;
        _client.StateChanged += OnClientStateChanged;
        _player.PlaybackEnded += OnPlaybackEnded;
    }

    /// <summary>Holds a scheduled start, and can be the screen the room hears.</summary>
    public async Task ConnectAsync(string serverUri, string screenId, CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(
            serverUri,
            screenId,
            new ScreenCapabilities { SupportsSync = true, SupportsAudio = true, SupportsVideo = true },
            cancellationToken);

        await ResyncClockAsync(cancellationToken);
    }

    /// <summary>Clocks drift over a long night, and a stale offset biases the whole group.</summary>
    public async Task ResyncClockAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _player.SetClockOffset(await _client.EstimateClockOffsetAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not estimate the clock offset to the host");
        }
    }

    private void OnClientStateChanged(object? sender, ScreenClientStateChangedEventArgs e) =>
        _logger.LogInformation("IPC state: {Old} -> {New}", e.OldState, e.NewState);

    private async void OnCommandReceived(object? sender, ScreenCommandReceivedEventArgs e)
    {
        await _commandGate.WaitAsync();
        try
        {
            _logger.LogInformation("Command received: {CommandType}", e.Command.GetType().Name);
            await ExecuteCommandAsync(e.Command);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error executing {Type}", e.Command.GetType().Name); }
        finally { _commandGate.Release(); }
    }

    private async Task ExecuteCommandAsync(IScreenCommand command)
    {
        switch (command)
        {
            case LoadMediaCommand cmd:
                // No decoder here, so LoadAsync reports the missing URL rather than no-oping.
                if (cmd.StreamUrl is { Length: > 0 } url)
                    _player.LoadStream(url, cmd.StreamStartOffset);
                else
                    await _player.LoadAsync(cmd.FilePath);
                break;
            case PlayCommand:
                _player.Play();
                break;
            case PauseCommand:
                _player.Pause();
                break;
            case StopCommand cmd:
                _player.Stop(cmd.FadeDuration);
                break;
            case SeekCommand cmd:
                _player.Seek(cmd.Position);
                break;
            case SetVolumeCommand cmd:
                _player.Volume = cmd.Volume;
                break;
            case SetTimelineCommand cmd:
                _player.SetTimeline(cmd.Position, cmd.AnchorUtc, cmd.IsPlaying, cmd.IsPrimary);
                break;
            case SetVideoCommand cmd:
                _player.SetVideoEnabled(cmd.Enabled);
                break;
            case SetPitchCommand cmd:
                _player.PitchSemitones = cmd.Semitones;
                break;
            default:
                _logger.LogWarning("Unhandled command: {Type}", command.GetType().Name);
                break;
        }

        // A timeline says where to be, not what changed. Answering one makes the host re-anchor,
        // which sends another timeline, forever.
        if (command is not SetTimelineCommand)
            await SendCurrentStateAsync();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => _ = SendCurrentStateAsync();

    public async Task SendCurrentStateAsync()
    {
        var state = new ScreenPlaybackState
        {
            LoadedFilePath = _player.Info?.FilePath,
            IsPlaying = _player.IsPlaying,
            Position = _player.Position,
            Duration = _player.Duration,
            SampledAtUtc = _player.SampledAtUtc,
        };

        try
        {
            await _client.SendStateAsync(state);
        }
        catch (InvalidOperationException)
        {
            // Not connected yet, or already torn down — state is resent on the next command.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client.CommandReceived -= OnCommandReceived;
        _client.StateChanged -= OnClientStateChanged;
        _player.PlaybackEnded -= OnPlaybackEnded;
        await _client.DisconnectAsync();
        if (_client is IAsyncDisposable disposable) await disposable.DisposeAsync();
        _commandGate.Dispose();
    }
}
