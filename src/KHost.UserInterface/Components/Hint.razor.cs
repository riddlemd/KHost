using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>A short explanation shown over what it wraps, on hover, on keyboard focus, or on a tap.</summary>
/// <remarks>The tap is what a touchscreen console has in place of hover; it toggles, and leaving the
/// element closes it. Not a native title: that never shows on touch, and its delay hides it from a
/// host reading at a glance.</remarks>
public partial class Hint
{
    private readonly string _id = $"kh-hint-{Guid.NewGuid():N}";

    private bool _open;

    /// <summary>What the hint says.</summary>
    [Parameter, EditorRequired] public string Text { get; set; } = "";

    /// <summary>What the hint explains; it is drawn as given.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string Class { get; set; } = "";

    private void Toggle() => _open = !_open;

    private void Close() => _open = false;
}
