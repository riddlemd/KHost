using KHost.Abstractions.MediaPlayer;
using KHost.Abstractions.Services.IPC;
using KHost.IPC.SignalR;
using Microsoft.Extensions.Logging;

namespace KHost.Screen;

internal sealed class ScreenIpcController : IAsyncDisposable
{
    private readonly IScreenClient _client;
    private readonly IMediaPlayer _player;
    private readonly ILogger<ScreenIpcController> _logger;

    // Commands arrive as independent async invocations; serialize them so that, e.g.,
    // a LoadMedia fully completes (FFProbe) before a following Play runs — otherwise Play
    // throws "No file loaded" and the screen never starts.
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    public ScreenIpcController(IScreenClient client, IMediaPlayer player, ILogger<ScreenIpcController> logger)
    {
        _client = client;
        _player = player;
        _logger = logger;

        _client.CommandReceived += OnCommandReceived;
        _client.StateChanged += OnClientStateChanged;
        _player.PlaybackEnded += OnPlaybackEnded;
    }

    // Plays audio, but decodes locally with no way to be held to a shared schedule — the same
    // shape a Cast receiver reports, so the host treats it as a loose consumer either way.
    public Task ConnectAsync(string serverUri, string screenId, CancellationToken cancellationToken = default) =>
        _client.ConnectAsync(
            serverUri, screenId, new ScreenCapabilities { SupportsAudio = true, SupportsVideo = true }, cancellationToken);

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
                _logger.LogInformation("Load {FilePath}", cmd.FilePath);
                await _player.LoadAsync(cmd.FilePath);
                break;
            case PlayCommand:
                _logger.LogInformation("Play");
                _player.Play();
                break;
            case PauseCommand:
                _logger.LogInformation("Pause");
                _player.Pause();
                break;
            case StopCommand cmd:
                _logger.LogInformation("Stop (fade={Fade})", cmd.FadeDuration);
                _player.Stop(cmd.FadeDuration);
                break;
            case SeekCommand cmd:
                _logger.LogInformation("Seek {Position}", cmd.Position);
                _player.Seek(cmd.Position);
                break;
            case SetVolumeCommand cmd:
                _logger.LogInformation("Volume {Volume}", cmd.Volume);
                _player.Volume = cmd.Volume;
                break;
            case SetPitchCommand cmd:
                _logger.LogInformation("PitchAdjust {Semitones}", cmd.Semitones);
                _player.PitchSemitones = cmd.Semitones;
                break;
            default:
                _logger.LogWarning("Unhandled command: {Type}", command.GetType().Name);
                break;
        }

        await SendCurrentStateAsync();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        _logger.LogInformation("Playback ended, sending state");
        _ = SendCurrentStateAsync();
    }

    private async Task SendCurrentStateAsync()
    {
        var state = new ScreenPlaybackState
        {
            LoadedFilePath = _player.Info?.FilePath,
            IsPlaying = _player.IsPlaying,
            Position = _player.Position,
            Duration = _player.Duration,
        };
        _logger.LogDebug("SendCurrentStateAsync IsPlaying={IsPlaying} Position={Position}", state.IsPlaying, state.Position);
        await _client.SendStateAsync(state);
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("ScreenIpcController disposing");
        _client.CommandReceived -= OnCommandReceived;
        _client.StateChanged -= OnClientStateChanged;
        _player.PlaybackEnded -= OnPlaybackEnded;
        await _client.DisconnectAsync();
        if (_client is IAsyncDisposable d) await d.DisposeAsync();
        _commandGate.Dispose();
    }
}
