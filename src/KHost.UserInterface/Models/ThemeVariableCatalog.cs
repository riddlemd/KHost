namespace KHost.UserInterface.Models;

public enum ThemeVariableKind
{
    Color,
    Text
}

public sealed record ThemeVariable(string Key, string Label, string Group, ThemeVariableKind Kind, string Fallback);

/// <summary>
/// The editable surface of a theme: 61 of the 73 custom properties a stylesheet needs. The other
/// 12 are Bootstrap aliases that only ever point back here.
///
/// The translucent shades are stored rather than derived, because the shipped themes tune their
/// own alphas (a border runs from 24% to 35% across them) and famicom's primary shades are not its
/// primary at all — deriving them would quietly repaint any theme cloned from one. The recipes
/// survive as <see cref="ShadeRecipes"/>, which the editor offers as an action instead.
/// </summary>
public static class ThemeVariableCatalog
{
    public const string BrandGroup = "Brand";
    public const string SurfacesGroup = "Surfaces";
    public const string TextGroup = "Text & Links";
    public const string StatusGroup = "Status";
    public const string AccentsGroup = "Accents & Flags";
    public const string ShadesGroup = "Shades";
    public const string ShapeGroup = "Shape & Type";
    public const string BackdropsGroup = "Backdrops";

    /// <summary>Group order as the editor renders it; colour groups first, then the typed-in ones.</summary>
    public static readonly IReadOnlyList<string> Groups =
    [
        BrandGroup, SurfacesGroup, TextGroup, StatusGroup, AccentsGroup, ShadesGroup, ShapeGroup, BackdropsGroup
    ];

    public static readonly IReadOnlyList<ThemeVariable> Fields =
    [
        new("--kh-primary", "Primary", BrandGroup, ThemeVariableKind.Color, "#5D2B90"),
        new("--kh-primary-hover", "Primary hover", BrandGroup, ThemeVariableKind.Color, "#7B3CB8"),
        new("--kh-primary-bright", "Primary bright", BrandGroup, ThemeVariableKind.Color, "#A86AD4"),
        new("--kh-accent", "Accent", BrandGroup, ThemeVariableKind.Color, "#CC5500"),
        new("--kh-accent-hover", "Accent hover", BrandGroup, ThemeVariableKind.Color, "#B84600"),
        new("--kh-accent-bright", "Accent bright", BrandGroup, ThemeVariableKind.Color, "#FF6B1A"),
        new("--kh-button-primary-text", "Primary button text", BrandGroup, ThemeVariableKind.Color, "#DDD4F0"),

        new("--kh-bg", "Page background", SurfacesGroup, ThemeVariableKind.Color, "#0B0814"),
        new("--kh-bg-card", "Card", SurfacesGroup, ThemeVariableKind.Color, "#131025"),
        new("--kh-bg-card-dark", "Card (sunken)", SurfacesGroup, ThemeVariableKind.Color, "#0f0b1f"),
        new("--kh-bg-elevated", "Elevated", SurfacesGroup, ThemeVariableKind.Color, "#1C1836"),
        new("--kh-bg-input", "Input", SurfacesGroup, ThemeVariableKind.Color, "#0E0C1C"),
        new("--kh-np-gradient-from", "Now playing gradient", SurfacesGroup, ThemeVariableKind.Color, "#271649"),

        new("--kh-text", "Text", TextGroup, ThemeVariableKind.Color, "#DDD4F0"),
        new("--kh-text-secondary", "Text secondary", TextGroup, ThemeVariableKind.Color, "#8878A8"),
        new("--kh-text-muted", "Text muted", TextGroup, ThemeVariableKind.Color, "#6b5a82"),
        new("--bs-link-color", "Link colour", TextGroup, ThemeVariableKind.Text, "var(--kh-primary-bright)"),
        new("--bs-link-hover-color", "Link hover", TextGroup, ThemeVariableKind.Color, "#C59AE8"),

        new("--kh-success", "Success", StatusGroup, ThemeVariableKind.Color, "#22C55E"),
        new("--kh-warning", "Warning", StatusGroup, ThemeVariableKind.Color, "#F59E0B"),
        new("--kh-info", "Info", StatusGroup, ThemeVariableKind.Color, "#2686e2"),
        new("--kh-danger", "Danger", StatusGroup, ThemeVariableKind.Color, "#EF4444"),
        new("--kh-danger-bright", "Danger bright", StatusGroup, ThemeVariableKind.Color, "#ff6b6b"),

        new("--kh-logo-from", "Logo gradient from", AccentsGroup, ThemeVariableKind.Color, "#A86AD4"),
        new("--kh-logo-to", "Logo gradient to", AccentsGroup, ThemeVariableKind.Color, "#C59AE8"),
        new("--kh-badge-info-text", "Badge text", AccentsGroup, ThemeVariableKind.Color, "#C59AE8"),
        new("--kh-flag-tipper", "Tipper flag", AccentsGroup, ThemeVariableKind.Color, "#4ADE80"),
        new("--kh-flag-regular", "Regular flag", AccentsGroup, ThemeVariableKind.Color, "#F472B6"),

        new("--kh-primary-glow", "Primary glow", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.45)"),
        new("--kh-primary-subtle", "Primary subtle", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.14)"),
        new("--kh-primary-dim", "Primary dim", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.08)"),
        new("--kh-primary-active", "Primary active", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.3)"),
        new("--kh-primary-glow-strong", "Primary glow (strong)", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.7)"),
        new("--kh-border", "Border", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.28)"),
        new("--kh-border-bright", "Border bright", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.55)"),
        new("--kh-card-header-bg", "Card header", ShadesGroup, ThemeVariableKind.Text, "rgba(93, 43, 144, 0.12)"),
        new("--kh-primary-bright-glow", "Primary bright glow", ShadesGroup, ThemeVariableKind.Text, "rgba(168, 106, 212, 0.7)"),
        new("--kh-badge-info-bg", "Badge background", ShadesGroup, ThemeVariableKind.Text, "rgba(168, 106, 212, 0.2)"),
        new("--kh-badge-info-border", "Badge border", ShadesGroup, ThemeVariableKind.Text, "rgba(168, 106, 212, 0.4)"),
        new("--kh-accent-glow", "Accent glow", ShadesGroup, ThemeVariableKind.Text, "rgba(204, 85, 0, 0.45)"),
        new("--kh-accent-subtle", "Accent subtle", ShadesGroup, ThemeVariableKind.Text, "rgba(204, 85, 0, 0.14)"),
        new("--kh-danger-glow", "Danger glow", ShadesGroup, ThemeVariableKind.Text, "rgba(239, 68, 68, 0.3)"),
        new("--kh-danger-bg-subtle", "Danger background", ShadesGroup, ThemeVariableKind.Text, "rgba(239, 68, 68, 0.1)"),
        new("--kh-danger-bg-dim", "Danger background (dim)", ShadesGroup, ThemeVariableKind.Text, "rgba(239, 68, 68, 0.2)"),
        new("--kh-danger-border-subtle", "Danger border", ShadesGroup, ThemeVariableKind.Text, "rgba(239, 68, 68, 0.35)"),
        new("--kh-danger-text-subtle", "Danger text", ShadesGroup, ThemeVariableKind.Text, "rgba(239, 68, 68, 0.7)"),
        new("--kh-warning-subtle", "Warning background", ShadesGroup, ThemeVariableKind.Text, "rgba(245, 158, 11, 0.1)"),
        new("--kh-warning-border-subtle", "Warning border", ShadesGroup, ThemeVariableKind.Text, "rgba(245, 158, 11, 0.3)"),
        new("--kh-success-subtle", "Success background", ShadesGroup, ThemeVariableKind.Text, "rgba(34, 197, 94, 0.1)"),
        new("--kh-success-border-subtle", "Success border", ShadesGroup, ThemeVariableKind.Text, "rgba(34, 197, 94, 0.25)"),

        new("--kh-radius", "Corner radius", ShapeGroup, ThemeVariableKind.Text, "8px"),
        new("--kh-radius-lg", "Corner radius (large)", ShapeGroup, ThemeVariableKind.Text, "12px"),
        new("--kh-transition", "Transition", ShapeGroup, ThemeVariableKind.Text, "0.15s ease"),
        new("--kh-font", "Font", ShapeGroup, ThemeVariableKind.Text, "'Segoe UI', sans-serif"),
        new("--kh-font-display", "Display font", ShapeGroup, ThemeVariableKind.Text, "'Segoe UI', sans-serif"),

        new("--kh-bg-header", "Header", BackdropsGroup, ThemeVariableKind.Text, "linear-gradient(180deg, #271649 0%, #131025 100%)"),
        new("--kh-error-bg", "Error screen", BackdropsGroup, ThemeVariableKind.Text, "linear-gradient(135deg, #2D0A0A, #1A0A0A)"),
        new("--kh-scrim", "Dialog scrim", BackdropsGroup, ThemeVariableKind.Text, "rgba(0, 0, 0, 0.6)"),
        new("--kh-track-bg", "Slider track", BackdropsGroup, ThemeVariableKind.Text, "rgba(255, 255, 255, 0.08)"),
        new("--kh-table-stripe", "Table stripe", BackdropsGroup, ThemeVariableKind.Text, "rgba(0, 0, 0, 0.2)"),
        new("--kh-badge-pending-bg", "Pending badge", BackdropsGroup, ThemeVariableKind.Text, "rgba(255, 255, 255, 0.04)")
    ];

    /// <summary>
    /// How each shade relates to the colour it is made from, as (shade, source, usual alpha). Used
    /// only when a host asks for the shades to be re-derived after changing a base colour.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Source, double Alpha)> ShadeRecipes =
    [
        ("--kh-primary-glow", "--kh-primary", 0.45),
        ("--kh-primary-subtle", "--kh-primary", 0.14),
        ("--kh-primary-dim", "--kh-primary", 0.08),
        ("--kh-primary-active", "--kh-primary", 0.3),
        ("--kh-primary-glow-strong", "--kh-primary", 0.7),
        ("--kh-border", "--kh-primary", 0.28),
        ("--kh-border-bright", "--kh-primary", 0.55),
        ("--kh-card-header-bg", "--kh-primary", 0.12),
        ("--kh-primary-bright-glow", "--kh-primary-bright", 0.7),
        ("--kh-badge-info-bg", "--kh-primary-bright", 0.2),
        ("--kh-badge-info-border", "--kh-primary-bright", 0.4),
        ("--kh-accent-glow", "--kh-accent", 0.45),
        ("--kh-accent-subtle", "--kh-accent", 0.14),
        ("--kh-danger-glow", "--kh-danger", 0.3),
        ("--kh-danger-bg-subtle", "--kh-danger", 0.1),
        ("--kh-danger-bg-dim", "--kh-danger", 0.2),
        ("--kh-danger-border-subtle", "--kh-danger", 0.35),
        ("--kh-danger-text-subtle", "--kh-danger", 0.7),
        ("--kh-warning-subtle", "--kh-warning", 0.1),
        ("--kh-warning-border-subtle", "--kh-warning", 0.3),
        ("--kh-success-subtle", "--kh-success", 0.1),
        ("--kh-success-border-subtle", "--kh-success", 0.25)
    ];

    /// <summary>Bootstrap aliases that only ever point at a KHost property.</summary>
    public static readonly IReadOnlyList<(string Key, string Value)> BootstrapAliases =
    [
        ("--bs-body-bg", "var(--kh-bg)"),
        ("--bs-body-color", "var(--kh-text)"),
        ("--bs-secondary-color", "var(--kh-text-secondary)"),
        ("--bs-tertiary-color", "var(--kh-text-muted)"),
        ("--bs-card-bg", "var(--kh-bg-card)"),
        ("--bs-card-border-color", "var(--kh-border)"),
        ("--bs-border-color", "var(--kh-border)"),
        ("--bs-border-color-translucent", "var(--kh-border)"),
        ("--bs-primary", "var(--kh-primary)"),
        ("--bs-table-hover-bg", "var(--kh-primary-subtle)"),
        ("--bs-table-striped-bg", "var(--kh-primary-dim)")
    ];

    private static readonly Dictionary<string, ThemeVariable> _byKey =
        Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static IEnumerable<ThemeVariable> FieldsIn(string group)
        => Fields.Where(f => f.Group == group);

    public static string FallbackFor(string key)
        => _byKey.TryGetValue(key, out var field) ? field.Fallback : "";

    public static bool IsKnown(string key) => _byKey.ContainsKey(key);

    public static ThemeVariable? Find(string key) => _byKey.TryGetValue(key, out var field) ? field : null;

    /// <summary>The grape palette, used to seed a theme created from scratch.</summary>
    public static Dictionary<string, string> Defaults()
        => Fields.ToDictionary(f => f.Key, f => f.Fallback, StringComparer.Ordinal);
}
