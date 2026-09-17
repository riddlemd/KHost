using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

/// <summary>Renders a stored theme to the stylesheet shape the SCSS build produces.</summary>
/// <remarks>Also parses a compiled stylesheet back into editable values, so a built-in can clone.</remarks>
public static partial class ThemeCss
{
    /// <summary>Chars that would break out of the declaration; a value with one is refused.</summary>
    private static readonly char[] _forbidden = [';', '{', '}', '<', '>', '\\'];

    private const int MaxValueLength = 200;

    public static bool IsValidValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= MaxValueLength
           && value.IndexOfAny(_forbidden) < 0
           && !value.Contains("/*", StringComparison.Ordinal);

    /// <summary>A colour must also be a hex literal: <c>--bs-primary-rgb</c> is computed from it.</summary>
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

    /// <summary>Pulls editable values out of a compiled stylesheet; derived properties are skipped.</summary>
    /// <remarks>Derived values are recomputed on build, so carrying them lets a clone drift.</remarks>
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

    /// <summary>Re-bases every translucent shade on its source colour, keeping its own alpha.</summary>
    /// <remarks>Only the hue went stale; a non-<c>rgba()</c> shade falls back to its usual alpha.</remarks>
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
