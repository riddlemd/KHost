using System.Text;

namespace KHost.Common.Orthography;

/// <summary>Resolves stylised spellings where a symbol stands in for a letter, e.g. "Ke$ha".</summary>
/// <remarks>Separate from transliteration: these characters are already ASCII.</remarks>
public static class StylisedSpelling
{
    /// <summary>No digits: "Deadmau5" to "deadmaus" would also wreck "Blink-182" and "50 Cent".</summary>
    /// <remarks>That needs a per-artist alias list, not a character map.</remarks>
    private static readonly Dictionary<char, char> _substitutions = new()
    {
        ['$'] = 's',
        ['!'] = 'i',
        ['@'] = 'a',
    };

    public static string ResolveToPlainSpelling(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
            builder.Append(_substitutions.TryGetValue(ch, out var replacement) ? replacement : ch);

        return builder.ToString();
    }
}
