using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The marquee's colours, kept with the venue's other screen colours and shown whether
/// the marquee is on or not, so they can be set before it is.</summary>
public partial class EditVenueMarqueeColours
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    // What each colour left unset is drawn in, as the screen resolves it: the theme, else the
    // screen's own, the divider following the band's text as the screen's does.
    private (string Colour, string From) Background
        => EditVenueTheme.FallbackFor(Model.ThemeShadowColor, "shadow", ScreenColourDefaults.MarqueeBackground);

    private (string Colour, string From) TextColour
        => EditVenueTheme.FallbackFor(Model.ThemeTextColor, "text", ScreenColourDefaults.MarqueeText);

    private (string Colour, string From) Singer
        => EditVenueTheme.FallbackFor(Model.ThemePrimaryColor, "primary", ScreenColourDefaults.MarqueeSinger);

    private (string Colour, string From) Song
        => EditVenueTheme.FallbackFor(Model.ThemeHighlightColor, "highlight", ScreenColourDefaults.MarqueeSong);

    private (string Colour, string From) Divider
        => Model.ThemePrimaryColor is { } primary ? (primary, "the theme's primary") : (Model.MarqueeTextColor ?? TextColour.Colour, "the marquee's text, faintly");
}
