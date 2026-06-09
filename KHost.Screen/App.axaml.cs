using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KHost.IPC.SignalR;
using KHost.Screen.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.Screen;

public partial class App : Application
{
    public static ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;
    public static string IpcServerUri { get; set; } = "http://localhost:5000/ipc/screen";
    public static string IpcScreenId { get; set; } = Environment.MachineName;

    private ScreenIpcController? _ipc;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(LoggerFactory);
            desktop.MainWindow = mainWindow;
            desktop.Startup += (_, _) => OnStartup(mainWindow);
            desktop.Exit += (_, _) => OnExit();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnStartup(MainWindow mainWindow)
    {
        var logger = LoggerFactory.CreateLogger<App>();

        var client = ProjectExtensions.CreateScreenClient(LoggerFactory);
        client.StateChanged += (_, e) => mainWindow.SetConnectionState(e.NewState);
        mainWindow.SetConnectionState(client.State); // initial paint

        _ipc = new ScreenIpcController(
            client,
            mainWindow.Player,
            LoggerFactory.CreateLogger<ScreenIpcController>());

        _ = ConnectIpcAsync(logger);
    }

    private async Task ConnectIpcAsync(ILogger logger)
    {
        try
        {
            logger.LogInformation("Connecting to IPC server at {Uri} with screen ID {ScreenId}", IpcServerUri, IpcScreenId);
            await _ipc!.ConnectAsync(IpcServerUri, IpcScreenId);
            logger.LogInformation("Successfully connected to IPC server");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to IPC server at {Uri}", IpcServerUri);
        }
    }

    private void OnExit()
    {
        var logger = LoggerFactory.CreateLogger<App>();
        logger.LogInformation("Application exiting, disconnecting IPC");
        _ = _ipc?.DisposeAsync().AsTask();
    }
}
