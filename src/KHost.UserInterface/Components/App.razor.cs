using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class App
{
    [Inject] private IConfiguration Configuration { get; set; } = default!;

    // Only the native window, and only in a Release build. A developer keeps their tools, and
    // headless mode is served to a real browser where this would be hostile.
    private bool LockDownShell
        => IsNativeShell && !Program.IsDebugBuild;

    private bool IsNativeShell => Configuration.GetValue<bool>(Program.NativeShellKey);

    /// <summary>Read by browser-keys.js: a browser tab keeps the browser's own tab and window keys.</summary>
    private string Surface => IsNativeShell ? "native" : "browser";

    /// <summary>Devtools chords follow the same Debug/Release line as the webview's own switch.</summary>
    private string Devtools => LockDownShell ? "block" : "allow";
}
