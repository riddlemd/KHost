using System.Text;

namespace KHost.Abstractions;

/// <summary>
/// Reduces text to the form comparisons happen on. Folding is done here, in .NET, and the result
/// is stored — the bundled SQLite folds ASCII only, so asking SQL to do it at query time silently
/// fails to match "ZOË" against "zoë". A stored folded value can be indexed like any other column.
/// </summary>
public static class TextFolding
{
    /// <summary>
    /// Composes to NFC before lowercasing, because macOS hands out decomposed text: the same name
    /// read off a filesystem and typed by hand are different strings until they are composed.
    /// </summary>
    public static string Fold(string? value)
        => value is null ? string.Empty : value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
