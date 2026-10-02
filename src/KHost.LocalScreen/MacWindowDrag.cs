using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KHost.LocalScreen;

/// <summary>Hands a drag of the window's strip to AppKit, which carries the window between displays itself.</summary>
/// <remarks>Moved by hand instead, a window straddling two displays is pulled back to a stale frame by
/// the window server every ~100ms while "Displays have separate Spaces" is on, which reads as jitter.
/// Photino exposes no NSWindow on macOS, so the window comes from the event itself.</remarks>
[SupportedOSPlatform("macos")]
internal static class MacWindowDrag
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // NSEventTypeLeftMouseDown and NSEventTypeLeftMouseDragged. The page asks only after the pointer
    // has travelled, so the event AppKit is dispatching is usually a drag rather than the press.
    private const ulong LeftMouseDown = 1;
    private const ulong LeftMouseDragged = 6;

    /// <returns>False, with why, when there is no mouse event to start from; the caller then drags by hand.</returns>
    /// <remarks>Main thread only: page messages arrive there on macOS, inside AppKit's own dispatch.</remarks>
    public static bool TryBegin(out string detail)
    {
        var app = SendPointer(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var current = SendPointer(app, sel_registerName("currentEvent"));

        if (current == IntPtr.Zero)
        {
            detail = "there is no current event";
            return false;
        }

        var type = SendUInt64(current, sel_registerName("type"));

        if (type is not (LeftMouseDown or LeftMouseDragged))
        {
            detail = $"the current event is type {type}, not a left mouse press or drag";
            return false;
        }

        var window = SendPointer(current, sel_registerName("window"));

        if (window == IntPtr.Zero)
        {
            detail = "the current event belongs to no window";
            return false;
        }

        SendWithPointer(window, sel_registerName("performWindowDragWithEvent:"), current);
        detail = $"from event type {type}";
        return true;
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern ulong SendUInt64(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendWithPointer(IntPtr receiver, IntPtr selector, IntPtr argument);
}
