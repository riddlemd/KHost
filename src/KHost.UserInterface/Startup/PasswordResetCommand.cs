using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.DataAccess;
using KHost.Domain.Services.PasswordHashers;
using KHost.Telemetry;
using KHost.UserInterface.Auth;

namespace KHost.UserInterface.Startup;

/// <summary>The `--reset-password` command-line path: a recovery route with no UI of its own.</summary>
internal static class PasswordResetCommand
{
    /// <summary>Prints a freshly generated password for the named user, then exits.</summary>
    internal const string Flag = "--reset-password";

    internal static int Run(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine($"Usage: KHost.UserInterface {Flag} <username>");
            return 1;
        }

        var services = new ServiceCollection()
            .AddLogging()
            .AddDataAccess()
            .AddSingleton<IPasswordHasher, Argon2PasswordHasher>()
            .BuildServiceProvider();

        var exitCode = PasswordReset.RunAsync(
            name,
            services.GetRequiredService<IUsersRepository>(),
            services.GetRequiredService<IPasswordHasher>(),
            Console.Out).GetAwaiter().GetResult();

        if (exitCode == 0)
        {
            // The reset must not be silent: whoever reads the logs sees recovery was used.
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            KHostLogFiles.SweepStaleLogs(logDirectory);

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [WRN] Password reset via {Flag} for '{name}'";
            File.AppendAllText(
                Path.Combine(logDirectory, KHostLogFiles.HostFileName()),
                line + Environment.NewLine);
        }

        return exitCode;
    }
}
