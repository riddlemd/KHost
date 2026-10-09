using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's theme and the colour of everything the screen draws.</summary>
public partial class EditVenueTheme
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    private const string NoPreset = "none";
    private const string CustomPreset = "custom";

    private string PresetKey
        => (Model.ThemePrimaryColor, Model.ThemeHighlightColor, Model.ThemeTextColor, Model.ThemeShadowColor) is (null, null, null, null)
            ? NoPreset
            : VenueThemePresets.Matching(Model.ThemePrimaryColor, Model.ThemeHighlightColor, Model.ThemeTextColor, Model.ThemeShadowColor)?.Id
                ?? CustomPreset;

    private void PickPreset(ChangeEventArgs e)
    {
        var id = e.Value?.ToString();
        if (id == CustomPreset) return;

        var preset = VenueThemePresets.All.FirstOrDefault(p => p.Id == id);
        Model.ThemePrimaryColor = preset?.Primary;
        Model.ThemeHighlightColor = preset?.Highlight;
        Model.ThemeTextColor = preset?.Text;
        Model.ThemeShadowColor = preset?.Shadow;
    }

    private sealed record ThemeRole(string Key, string Label, string Uses, string Starting, Func<string?> Get, Action<string?> Set);

    private sealed record ElementRow(
        string Key, string Label, Func<string?> Theme, string ThemeName, string ScreenDefault, Func<string?> Get, Action<string?> Set);

    private sealed record ElementGroup(string Title, IReadOnlyList<ElementRow> Rows, string? Note = null);

    private IReadOnlyList<ThemeRole> ThemeRoles =>
    [
        new("primary", "Primary", "Marquee singers and divider, and a visualisation's main colour.", ScreenColourDefaults.ThemePrimary,
            () => Model.ThemePrimaryColor, v => Model.ThemePrimaryColor = v),
        new("highlight", "Highlight", "Sung words, the next singer's name, marquee songs, and a visualisation's light colour.", ScreenColourDefaults.ThemeHighlight,
            () => Model.ThemeHighlightColor, v => Model.ThemeHighlightColor = v),
        new("text", "Text", "Unsung words and the text of every card and band.", ScreenColourDefaults.ThemeText,
            () => Model.ThemeTextColor, v => Model.ThemeTextColor = v),
        new("shadow", "Shadow", "The background, outlines, panels, and a visualisation's dark colour.", ScreenColourDefaults.ThemeShadow,
            () => Model.ThemeShadowColor, v => Model.ThemeShadowColor = v),
    ];

    private IReadOnlyList<ElementGroup> ElementGroups =>
    [
        new("Screen",
        [
            Row("background", "Background", Shadow, "shadow", ScreenColourDefaults.Background, () => Model.ScreenBackgroundColor, v => Model.ScreenBackgroundColor = v),
        ]),
        new("Lyrics",
        [
            Row("lyrics-sung", "Sung words", Highlight, "highlight", ScreenColourDefaults.LyricsSung, () => Model.LyricsSungColor, v => Model.LyricsSungColor = v),
            Row("lyrics-unsung", "Unsung words", Text, "text", ScreenColourDefaults.LyricsUnsung, () => Model.LyricsUnsungColor, v => Model.LyricsUnsungColor = v),
            Row("lyrics-outline", "Outline", Shadow, "shadow", ScreenColourDefaults.LyricsOutline, () => Model.LyricsOutlineColor, v => Model.LyricsOutlineColor = v),
        ], "Only where the song sets none"),
        new("Title card",
        [
            Row("intro-text", "Text", Text, "text", ScreenColourDefaults.IntroText, () => Model.IntroTextColor, v => Model.IntroTextColor = v),
            Row("intro-outline", "Outline", Shadow, "shadow", ScreenColourDefaults.IntroOutline, () => Model.IntroOutlineColor, v => Model.IntroOutlineColor = v),
        ]),
        new("“Up next” card",
        [
            Row("next-text", "Text", Text, "text", ScreenColourDefaults.NextSingerText, () => Model.NextSingerTextColor, v => Model.NextSingerTextColor = v),
            Row("next-name", "Singer's name", Highlight, "highlight", ScreenColourDefaults.NextSingerName, () => Model.NextSingerNameColor, v => Model.NextSingerNameColor = v),
            Row("next-panel", "Panel", Shadow, "shadow", ScreenColourDefaults.NextSingerPanel, () => Model.NextSingerPanelColor, v => Model.NextSingerPanelColor = v),
        ]),
        new("Break music card",
        [
            Row("break-text", "Text", Text, "text", ScreenColourDefaults.BreakMusicCardText, () => Model.BreakMusicCardTextColor, v => Model.BreakMusicCardTextColor = v),
            Row("break-background", "Panel", Shadow, "shadow", ScreenColourDefaults.BreakMusicCardBackground, () => Model.BreakMusicCardBackgroundColor, v => Model.BreakMusicCardBackgroundColor = v),
        ]),
        new("QR code",
        [
            Row("qr-frame", "Frame", Text, "text", ScreenColourDefaults.QrCodeFrame, () => Model.QrCodeFrameColor, v => Model.QrCodeFrameColor = v),
            Row("qr-caption", "Caption", Shadow, "shadow", ScreenColourDefaults.QrCodeCaption, () => Model.QrCodeCaptionColor, v => Model.QrCodeCaptionColor = v),
        ]),
    ];

    private string? Highlight() => Model.ThemeHighlightColor;
    private string? Text() => Model.ThemeTextColor;
    private string? Shadow() => Model.ThemeShadowColor;

    private static ElementRow Row(string key, string label, Func<string?> theme, string themeName, string screenDefault,
        Func<string?> get, Action<string?> set) => new(key, label, theme, themeName, screenDefault, get, set);

    /// <summary>What a colour left unset is drawn in, and where that comes from.</summary>
    internal static (string Colour, string From) FallbackFor(string? theme, string themeName, string screenDefault)
        => theme is not null ? (theme, $"the theme's {themeName}") : (screenDefault, "the screen's own colour");
}
