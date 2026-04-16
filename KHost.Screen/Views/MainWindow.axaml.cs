using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KHost.Abstractions.MediaPlayer;
using System.Runtime.InteropServices;

namespace KHost.Screen.Views;

public partial class MainWindow : Window
{
    private readonly IMediaPlayer _player;
    private readonly DispatcherTimer _positionTimer;

    // Double-buffered bitmap swap: background thread writes _pendingBitmap;
    // the UI thread promotes it to _displayBitmap.
    private WriteableBitmap? _pendingBitmap;
    private WriteableBitmap? _displayBitmap;

    // Prevents position timer from fighting with manual slider drags.
    private bool _userDraggingSlider;

    public MainWindow()
    {
        InitializeComponent();

        _player = new DefaultMediaPlayer();
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

    // ── Player event handlers (arrive on background threads) ────────────────

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
        Dispatcher.UIThread.Post(() =>
        {
            var bmp = Interlocked.Exchange(ref _pendingBitmap, null);
            if (bmp is null) return;

            _displayBitmap?.Dispose();
            _displayBitmap = bmp;

            ImgVideo.Source = _displayBitmap;
            TxtPlaceholder.IsVisible = false;
        }, DispatcherPriority.Render);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(UpdateControlState);

    private void OnErrorOccurred(object? sender, string message)
        => Dispatcher.UIThread.Post(() => ShowError(message));

    // ── Toolbar button handlers ──────────────────────────────────────────────

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

    // ── Slider seek ──────────────────────────────────────────────────────────

    private void SldPosition_PointerPressed(object? sender, PointerPressedEventArgs e)
        => _userDraggingSlider = true;

    private void SldPosition_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _userDraggingSlider = false;

        if (!_player.IsLoaded) return;

        double fraction = SldPosition.Value / SldPosition.Maximum;
        _player.Seek(TimeSpan.FromTicks((long)(fraction * _player.Duration.Ticks)));
    }

    // ── Position timer ────────────────────────────────────────────────────────

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_player.IsLoaded) return;

        var pos = _player.Position;
        var dur = _player.Duration;

        TxtTime.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";

        if (!_userDraggingSlider && dur > TimeSpan.Zero)
            SldPosition.Value = pos.TotalSeconds / dur.TotalSeconds * SldPosition.Maximum;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task LoadFileAsync(string filePath)
    {
        BtnLoad.IsEnabled = false;
        BtnPlay.IsEnabled = false;
        BtnStop.IsEnabled = false;
        TxtStatus.Text = "Loading…";
        Title = "KHost.Screen — Loading…";

        try
        {
            _player.Stop();

            // Clear the current frame while loading
            ImgVideo.Source = null;
            _displayBitmap?.Dispose();
            _displayBitmap = null;
            TxtPlaceholder.IsVisible = true;

            await _player.LoadAsync(filePath);

            var name = Path.GetFileName(filePath);
            var info = _player.Info;
            Title = $"KHost.Screen — {name}";
            TxtStatus.Text = info is not null
                ? $"{info.Width}×{info.Height}  {info.Fps:F2} fps  {FormatTime(info.Duration)}"
                : name;

            SldPosition.Value = 0;
        }
        catch (Exception ex)
        {
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
        SldPosition.IsEnabled = loaded;
        BtnPlay.Content = playing ? "⏸  Pause" : "▶  Play";

        if (!loaded)
        {
            TxtPlaceholder.IsVisible = true;
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
