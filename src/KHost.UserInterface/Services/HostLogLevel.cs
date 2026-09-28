using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace KHost.UserInterface.Services;

/// <summary>The host's minimum log level, read from configuration like any other setting.</summary>
internal static class HostLogLevel
{
    /// <summary>The conventional key, so <c>Logging__LogLevel__Default=Debug</c> and
    /// <c>--Logging:LogLevel:Default=Debug</c> both reach Serilog.</summary>
    internal const string ConfigurationKey = "Logging:LogLevel:Default";

    /// <summary>Information when the key is missing or names no level.</summary>
    internal static LogLevel Read(IConfiguration configuration)
        => Enum.TryParse<LogLevel>(configuration[ConfigurationKey], ignoreCase: true, out var level)
           && Enum.IsDefined(level)
            ? level
            : LogLevel.Information;

    /// <summary>The framework's own chatter stays at Warning unless the host asks for less still.</summary>
    internal static LogLevel ForFramework(LogLevel level) => level > LogLevel.Warning ? level : LogLevel.Warning;

    internal static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        // Serilog has no "off"; Fatal is the least it will write.
        _ => LogEventLevel.Fatal,
    };
}
