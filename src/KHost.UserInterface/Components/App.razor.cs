using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class App
{
    [Inject] private IConfiguration Configuration { get; set; } = default!;

    // Only the native window, and only in a Release build — a developer keeps their tools, and
    // headless mode is served to a real browser where this would be hostile.
    private bool LockDownShell
        => Configuration.GetValue<bool>(Program.NativeShellKey) && !Program.IsDebugBuild;
}
