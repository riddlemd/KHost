using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>The `&lt;option&gt;` list a select binds `ImageScaling` against, shared by every dialog
/// that offers it. Renders bare options, so the caller supplies the `&lt;select&gt;` around it.
/// </summary>
public partial class ImageScalingOptions
{
    /// <summary>Only the venue offers this: a blank option meaning "the image's own answer",
    /// which is what a venue that never overrides it already gets.</summary>
    [Parameter] public bool IncludeUseOwn { get; set; }
}
