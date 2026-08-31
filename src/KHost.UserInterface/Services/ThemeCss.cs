using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

/// <summary>
/// Renders a stored theme to the same stylesheet shape the SCSS build produces, and reads a
/// compiled theme back into the editable values so a built-in can be cloned.
/// </summary>
public static partial class ThemeCss
{
    /// <summary>
    /// Characters that would end the declaration or the <c>:root</c> block. A value reaches here
    /// from an admin form and leaves as bytes in a stylesheet every client loads, so anything that
    /// could open a rule of its own is refused rather than escaped.
    /// </summary>
    private static readonly char[] _forbidden = [';', '{', '}', '<', '>', '\\'];

    private const int MaxValueLength = 200;

    public static bool IsValidValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= MaxValueLength
           && value.IndexOfAny(_forbidden) < 0
           && !value.Contains("/*", StringComparison.Ordinal);

    /// <summary>
    /// Whether a value is usable for a particular field. A colour must be a hex literal as well as
    /// safe: <c>--bs-primary-rgb</c> is computed from <c>--kh-primary</c>, so a named colour or an
    /// <c>rgb()</c> would render correctly while the triplet quietly fell back to another colour.
    /// </summary>
    public static bool IsValidFor(ThemeVariable field, string? value)
        => IsValidValue(value)
           && (field.Kind != ThemeVariableKind.Color || TryParseHex(value, out _, out _, out _));

    public static string Build(ThemeDefinition theme)
    {
        var builder = new StringBuilder();
        builder.AppendLine(":root {");

        foreach (var field in ThemeVariableCatalog.Fields)
            builder.AppendLine($"    {field.Key}: {Safe(field, theme[field.Key])};");

        foreach (var (key, value) in ThemeVariableCatalog.BootstrapAliases)
            builder.AppendLine($"    {key}: {value};");

        // Taken from the same value the declaration above emitted, so the triplet and the colour
        // it describes cannot disagree even for a store someone edited by hand.
        builder.AppendLine($"    --bs-primary-rgb: {Triplet(Safe(PrimaryField, theme[PrimaryField.Key]))};");
        builder.AppendLine("}");

        return builder.ToString();
    }

    /// <summary>
    /// Pulls the editable values out of a compiled theme stylesheet. Derived properties in the file
    /// are ignored: they are recomputed on build, so carrying them would let a clone drift.
    /// </summary>
    public static Dictionary<string, string> Parse(string css)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in DeclarationPattern().Matches(css))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();

            if (ThemeVariableCatalog.Find(key) is { } field && IsValidFor(field, value))
                values[key] = value;
        }

        return values;
    }

    public static bool TryParseHex(string? value, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;

        if (value is null)
            return false;

        var hex = value.Trim().TrimStart('#');

        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return false;

        r = (byte)(packed >> 16);
        g = (byte)((packed >> 8) & 0xFF);
        b = (byte)(packed & 0xFF);
        return true;
    }

    /// <summary>
    /// Re-bases every translucent shade on the colour it is made from, in place. Each shade keeps
    /// the alpha it already had — that is the theme's own tuning, and only the hue went stale when
    /// the base colour changed. A shade that is not an <c>rgba()</c> falls back to its usual alpha.
    /// </summary>
    public static void DeriveShades(Dictionary<string, string> values)
    {
        foreach (var (key, source, defaultAlpha) in ThemeVariableCatalog.ShadeRecipes)
        {
            values.TryGetValue(source, out var sourceValue);

            if (!TryParseHex(sourceValue, out var r, out var g, out var b)
                && !TryParseHex(ThemeVariableCatalog.FallbackFor(source), out r, out g, out b))
                continue;

            values.TryGetValue(key, out var current);
            var alpha = AlphaOf(current) ?? defaultAlpha;

            values[key] = $"rgba({r}, {g}, {b}, {alpha.ToString("0.##", CultureInfo.InvariantCulture)})";
        }
    }

    private static readonly ThemeVariable PrimaryField = ThemeVariableCatalog.Find("--kh-primary")!;

    private static string Safe(ThemeVariable field, string value)
        => IsValidFor(field, value) ? value : field.Fallback;

    private static double? AlphaOf(string? rgba)
    {
        if (rgba is null)
            return null;

        var match = AlphaPattern().Match(rgba);

        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha)
            ? alpha
            : null;
    }

    // Its argument has already been through Safe, so the fallback here is unreachable in practice.
    private static string Triplet(string hex)
        => TryParseHex(hex, out var r, out var g, out var b) ? $"{r}, {g}, {b}" : "0, 0, 0";

    [GeneratedRegex(@"(--[A-Za-z0-9-]+)\s*:\s*([^;{}]+)\s*[;}]")]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex(@"rgba\s*\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*([\d.]+)\s*\)")]
    private static partial Regex AlphaPattern();
}
