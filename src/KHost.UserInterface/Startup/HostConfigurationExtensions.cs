using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Telemetry;
using KHost.UserInterface.Services;
using Serilog;
using Serilog.Events;

namespace KHost.UserInterface.Startup;

/// <summary>The configuration sources and the logging pipeline, both settled before anything else
/// touches <see cref="WebApplicationBuilder"/>.</summary>
internal static class HostConfigurationExtensions
{
    /// <summary>Adds the sources the rest of startup, and the app itself, read from.</summary>
    internal static void AddKHostConfiguration(this WebApplicationBuilder builder, bool headless)
    {
        // The window locks itself down (no reload, no back, no inspector); a browser tab does not.
        builder.Configuration.AddInMemoryCollection(
            [new KeyValuePair<string, string?>(Program.NativeShellKey, (!headless).ToString())]);

        // The App Settings page writes this overlay; registered last, it wins over the
        // deployment defaults, and reload-on-change lets IOptionsMonitor bindings apply live.
        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "cache", AppSettingsService.OverlayFileName),
            optional: true,
            reloadOnChange: true);
    }

    /// <summary>Wires Serilog, the log directory sweep, and log retention.</summary>
    internal static void AddKHostLogging(this WebApplicationBuilder builder)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        KHostLogFiles.SweepStaleLogs(logDirectory);

        var logFilePath = Path.Combine(logDirectory, KHostLogFiles.HostFileName());
        var logLevel = HostLogLevel.Read(builder.Configuration);
        var frameworkLogLevel = HostLogLevel.ToSerilog(HostLogLevel.ForFramework(logLevel));

        builder.Host.UseSerilog((_, _, cfg) => cfg
            .MinimumLevel.Is(HostLogLevel.ToSerilog(logLevel))
            .MinimumLevel.Override("Microsoft", frameworkLogLevel)
            .MinimumLevel.Override("Microsoft.AspNetCore", frameworkLogLevel)
            .WriteTo.Console()
            .WriteTo.File(
                path: logFilePath,
                // Infinite: the filename already carries the launch timestamp, so a date-rolled
                // segment on top of it would just repeat today's date in the name.
                rollingInterval: RollingInterval.Infinite,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10_000_000,
                retainedFileCountLimit: null,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

        builder.Services.AddSingleton<ILiveLogService>(new LiveLogService(logFilePath));

        // A sweep at launch never fires again for a host left running for weeks.
        builder.Services.AddHostedService(_ => new LogRetentionHostedService(logDirectory));

        // A screen launched while the host is raised is raised with it, unless LocalScreen:LogLevel says otherwise.
        builder.Services.PostConfigure<LocalScreenProvider.ServiceOptions>(options => options.LogLevel ??= logLevel.ToString());
    }
}
