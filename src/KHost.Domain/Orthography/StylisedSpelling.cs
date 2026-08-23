using System.Text;

namespace KHost.Domain.Orthography;

/// <summary>
/// Resolves the stylised spellings artists use, where a symbol stands in for a letter, so "Ke$ha"
/// is found by typing "kesha". Separate from transliteration because these characters are already
/// ASCII — Unidecode has no reason to touch them, and which symbol reads as which letter is
/// knowledge about names rather than about Unicode.
/// </summary>
public static class StylisedSpelling
{
    /// <summary>
    /// Digits are deliberately absent. The rule that turns "Deadmau5" into "deadmaus" also ruins
    /// "Blink-182" and "50 Cent", where the digits are digits — that class of stylisation needs a
    /// per-artist alias list, not a character mapping.
    /// </summary>
    private static readonly Dictionary<char, char> _substitutions = new()
    {
        ['$'] = 's',
        ['!'] = 'i',
        ['@'] = 'a',
    };

    public static string Resolve(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
            builder.Append(_substitutions.TryGetValue(ch, out var replacement) ? replacement : ch);

        return builder.ToString();
    }
}
