using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>An input with fixed text inside its box before or after the value, as a "$" before an
/// amount or a "%" after a level. The input is the child, so it binds exactly as it would alone.</summary>
/// <remarks>The text is decoration: the input's own label must still say what the value is.</remarks>
public partial class InputAffix
{
    /// <summary>Shown before the value; none when empty.</summary>
    [Parameter] public string? Prefix { get; set; }

    /// <summary>Shown after the value; none when empty.</summary>
    [Parameter] public string? Suffix { get; set; }

    /// <summary>Classes added to the box, for a caller that sizes it.</summary>
    [Parameter] public string Class { get; set; } = "";

    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;
}
