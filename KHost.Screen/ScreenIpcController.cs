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

    public ScreenIpcController(IScreenClient client, IMediaPlayer player, ILogger<ScreenIpcController> logger)
    {
        _client = client;
        _player = player;
        _logger = logger;

        _client.CommandReceived += OnCommandReceived;
        _client.StateChanged += OnClientStateChanged;
        _player.PlaybackEnded += OnPlaybackEnded;
    }

    public Task ConnectAsync(string serverUri, string screenId, CancellationToken cancellationToken = default) =>
        _client.ConnectAsync(serverUri, screenId, cancellationToken);

    private void OnClientStateChanged(object? sender, ScreenClientStateChangedEventArgs e) =>
        _logger.LogInformation("IPC state: {Old} -> {New}", e.OldState, e.NewState);

    private async void OnCommandReceived(object? sender, ScreenCommandReceivedEventArgs e)
    {
        try { await ExecuteCommandAsync(e.Command); }
        catch (Exception ex) { _logger.LogError(ex, "Error executing {Type}", e.Command.GetType().Name); }
    }

    private async Task ExecuteCommandAsync(IScreenCommand command)
    {
        switch (command)
        {
            case LoadMediaCommand cmd: await _player.LoadAsync(cmd.FilePath); break;
            case PlayCommand: _player.Play(); break;
            case PauseCommand: _player.Pause(); break;
            case StopCommand cmd: _player.Stop(cmd.FadeDuration); break;
            case SeekCommand cmd: _player.Seek(cmd.Position); break;
            case SetVolumeCommand cmd: _player.Volume = cmd.Volume; break;
            case SetPitchCommand cmd: _player.PitchSemitones = cmd.Semitones; break;
            default: _logger.LogWarning("Unhandled command: {Type}", command.GetType().Name); break;
        }

        await SendCurrentStateAsync();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) =>
        _ = SendCurrentStateAsync();

    private Task SendCurrentStateAsync() =>
        _client.SendStateAsync(new ScreenPlaybackState
        {
            LoadedFilePath = _player.Info?.FilePath,
            IsPlaying = _player.IsPlaying,
            Position = _player.Position,
            Duration = _player.Duration,
        });

    public async ValueTask DisposeAsync()
    {
        _client.CommandReceived -= OnCommandReceived;
        _client.StateChanged -= OnClientStateChanged;
        _player.PlaybackEnded -= OnPlaybackEnded;
        await _client.DisconnectAsync();
        if (_client is IAsyncDisposable d) await d.DisposeAsync();
    }
}
