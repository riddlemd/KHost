using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KHost.Abstractions.MediaPlayer;
using KHost.Abstractions.Services.IPC;
using KHost.Screen.OpenAl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace KHost.Screen.Views;

public partial class MainWindow : Window
{
    private readonly IMediaPlayer _player;
    internal IMediaPlayer Player => _player;

    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _positionTimer;

    // Double-buffered bitmap swap: background thread writes _pendingBitmap;
    // the UI thread promotes it to _displayBitmap.
    private WriteableBitmap? _pendingBitmap;
    private WriteableBitmap? _displayBitmap;

    // Prevents position timer from fighting with manual slider drags.
    private bool _userDraggingSlider;

    // The launch preference; full screen hides the chrome on top of this rather than replacing
    // it, so leaving full screen restores whatever the screen started with.
    private readonly bool _showControls;
    private bool _isFullScreen;

    public MainWindow() : this(NullLoggerFactory.Instance)
    {
    }

    public MainWindow(ILoggerFactory loggerFactory, bool showControls = true)
    {
        InitializeComponent();

        _showControls = showControls;
        ApplyChrome();

        _logger = loggerFactory.CreateLogger<MainWindow>();
        var audio = new OpenAlAudioPlayer(loggerFactory.CreateLogger<OpenAlAudioPlayer>());
        _player = new DefaultMediaPlayer(audio, loggerFactory.CreateLogger<DefaultMediaPlayer>());
        _player.FrameAvailable += OnFrameAvailable;
        _player.PlaybackEnded += OnPlaybackEnded;
        _player.ErrorOccurred += OnErrorOccurred;

        // Slider marks pointer events as Handled during thumb/track interaction,
        // so XAML-attached handlers miss them. Register with handledEventsToo.
        SldPosition.AddHandler(
            PointerPressedEvent,
            SldPosition_PointerPressed,
            handledEventsToo: true);
        SldPosition.AddHandler(
            PointerReleasedEvent,
            SldPosition_PointerReleased,
            handledEventsToo: true);

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += PositionTimer_Tick;
        _positionTimer.Start();
    }

    private void VideoArea_DoubleTapped(object? sender, TappedEventArgs e)
    {
        _isFullScreen = !_isFullScreen;
        ApplyChrome();
        e.Handled = true;
    }

    // Escape is the way back out when the toolbars are hidden and there is no title bar to
    // grab — without it a screen started with --no-controls has no visible exit.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullScreen)
        {
            _isFullScreen = false;
            ApplyChrome();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void ApplyChrome()
    {
        bool chromeVisible = _showControls && !_isFullScreen;

        TopToolbar.IsVisible = chromeVisible;
        BottomToolbar.IsVisible = chromeVisible;

        // FullScreen drops the border and title bar itself, so the decorations do not need
        // touching separately.
        WindowState = _isFullScreen ? WindowState.FullScreen : WindowState.Normal;

        // Re-gate rather than set: the placeholder starts visible from the XAML, and whether it
        // should be showing at all depends on playback rather than on the chrome.
        SetPlaceholderVisible(TxtPlaceholder.IsVisible);

        // Key input routes through the focused element, and hiding the toolbars leaves nothing
        // else focusable — without this Escape stops reaching OnKeyDown.
        Focus();
    }

    // The placeholder points at the Load button, so it has nothing to say on a screen that has
    // no controls and takes its media from the host.
    private void SetPlaceholderVisible(bool visible)
        => TxtPlaceholder.IsVisible = visible && _showControls;

    internal void SetConnectionState(ScreenClientState state)
    {
        bool connected = state == ScreenClientState.Connected;
        Dispatcher.UIThread.Post(() => ConnDot.IsVisible = !connected);
    }

    private void OnFrameAvailable(object? sender, IMediaPlayer.FrameData frame)
    {
        // Build a WriteableBitmap from the raw BGRA bytes off-thread.
        var wb = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var fb = wb.Lock())
        {
            int dstStride = fb.RowBytes;
            int srcStride = frame.Width * 4; // bgra = 4 bytes/pixel

            if (dstStride == srcStride)
            {
                Marshal.Copy(frame.Pixels, 0, fb.Address, frame.Pixels.Length);
            }
            else
            {
                // Copy row-by-row to handle stride padding
                for (int row = 0; row < frame.Height; row++)
                    Marshal.Copy(
                        frame.Pixels, row * srcStride,
                        IntPtr.Add(fb.Address, row * dstStride),
                        srcStride);
            }
        }

        // Swap in the new bitmap; discard any frame that was never shown.
        var old = Interlocked.Exchange(ref _pendingBitmap, wb);
        old?.Dispose();

        // Promote to the Image on the UI thread without blocking the decoder.
        var alpha = frame.Alpha;
        Dispatcher.UIThread.Post(() =>
        {
            var bmp = Interlocked.Exchange(ref _pendingBitmap, null);
            if (bmp is null) return;

            _displayBitmap?.Dispose();
            _displayBitmap = bmp;

            ImgVideo.Opacity = alpha;
            ImgVideo.Source = _displayBitmap;
            SetPlaceholderVisible(false);
        }, DispatcherPriority.Render);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            ClearFrame();
            UpdateControlState();
        });

    // The player stops feeding frames on stop, so the last one would otherwise stay on screen —
    // and after a fade it lingers as a fully transparent image rather than a blanked one.
    private void ClearFrame()
    {
        ImgVideo.Source = null;
        ImgVideo.Opacity = 1;
        _displayBitmap?.Dispose();
        _displayBitmap = null;
        SetPlaceholderVisible(true);
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        _logger.LogError("Player error: {Message}", message);
        Dispatcher.UIThread.Post(() => ShowError(message));
    }

    private async void BtnLoad_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Video File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video Files")
                {
                    Patterns = new[]
                    {
                        "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv",
                        "*.flv", "*.webm", "*.ts",  "*.m4v", "*.mpg", "*.mpeg",
                        "*.cdg"
                    }
                },
                FilePickerFileTypes.All,
            }
        });

        if (files.Count > 0)
            await LoadFileAsync(files[0].Path.LocalPath);
    }

    private void BtnPlay_Click(object? sender, RoutedEventArgs e)
    {
        if (_player.IsPlaying)
            _player.Pause();
        else
            _player.Play();

        UpdateControlState();
    }

    private void BtnStop_Click(object? sender, RoutedEventArgs e)
    {
        _player.Stop();
        SldPosition.Value = 0;
        TxtTime.Text = "--:-- / --:--";
        UpdateControlState();
    }

    private void BtnPitchDown_Click(object? sender, RoutedEventArgs e)
    {
        _player.PitchSemitones = Math.Max(-12, _player.PitchSemitones - 1);
        UpdatePitchLabel();
    }

    private void BtnPitchUp_Click(object? sender, RoutedEventArgs e)
    {
        _player.PitchSemitones = Math.Min(12, _player.PitchSemitones + 1);
        UpdatePitchLabel();
    }

    private void UpdatePitchLabel()
    {
        int v = _player.PitchSemitones;
        TxtPitch.Text = v switch
        {
            0 => "Key: 0",
            > 0 => $"Key: +{v}",
            _ => $"Key: {v}",
        };
    }

    private void SldPosition_PointerPressed(object? sender, PointerPressedEventArgs e)
        => _userDraggingSlider = true;

    private void SldPosition_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _userDraggingSlider = false;

        if (!_player.IsLoaded) return;

        double fraction = SldPosition.Value / SldPosition.Maximum;
        _player.Seek(TimeSpan.FromTicks((long)(fraction * _player.Duration.Ticks)));
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_player.IsLoaded) return;

        var pos = _player.Position;
        var dur = _player.Duration;

        TxtTime.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";

        if (!_userDraggingSlider && dur > TimeSpan.Zero)
            SldPosition.Value = pos.TotalSeconds / dur.TotalSeconds * SldPosition.Maximum;
    }

    private async Task LoadFileAsync(string filePath)
    {
        _logger.LogInformation("Loading file {FilePath}", filePath);
        BtnLoad.IsEnabled = false;
        BtnPlay.IsEnabled = false;
        BtnStop.IsEnabled = false;
        TxtStatus.Text = "Loading…";
        Title = "KHost.Screen — Loading…";

        try
        {
            _player.Stop();

            ClearFrame();

            _player.PitchSemitones = 0;
            UpdatePitchLabel();

            await _player.LoadAsync(filePath);

            var name = Path.GetFileName(filePath);
            var info = _player.Info;
            _logger.LogInformation("File loaded successfully: {FileName}", name);
            Title = $"KHost.Screen — {name}";
            TxtStatus.Text = info is not null
                ? $"{info.Width}×{info.Height}  {info.Fps:F2} fps  {FormatTime(info.Duration)}"
                : name;

            SldPosition.Value = 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadFileAsync failed for {FilePath}", filePath);
            Title = "KHost.Screen";
            TxtStatus.Text = "Load failed";
            ShowError($"Could not open file:\n{ex.Message}");
        }
        finally
        {
            UpdateControlState();
            BtnLoad.IsEnabled = true;
        }
    }

    private void UpdateControlState()
    {
        bool loaded = _player.IsLoaded;
        bool playing = _player.IsPlaying;

        BtnPlay.IsEnabled = loaded;
        BtnStop.IsEnabled = loaded;
        BtnPitchDown.IsEnabled = loaded;
        BtnPitchUp.IsEnabled = loaded;
        SldPosition.IsEnabled = loaded;
        BtnPlay.Content = playing ? "⏸  Pause" : "▶  Play";

        if (!loaded)
        {
            SetPlaceholderVisible(true);
            TxtStatus.Text = "No file loaded";
        }
    }

    private void ShowError(string message)
    {
        // Display inline in the status bar rather than spawning a dialog
        TxtStatus.Text = $"⚠ {message.Split('\n')[0]}";
        TxtStatus.Foreground = Avalonia.Media.Brushes.OrangeRed;
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
}
