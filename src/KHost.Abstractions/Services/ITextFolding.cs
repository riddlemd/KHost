namespace KHost.Abstractions.Services;

/// <summary>
/// Reduces text to what the host actually searches on. Offered rather than shipped as a helper on
/// purpose: the implementation carries a transliteration dependency, and a plugin resolving this
/// from DI gets the host's exact behaviour without that package travelling inside it.
///
/// A plugin wanting the media rule — stylised spellings resolved first, so "Ke$ha" folds like
/// "kesha" — composes <c>StylisedSpelling.Resolve</c> from KHost.Common ahead of this.
/// </summary>
public interface ITextFolding
{
    /// <summary>
    /// Transliterated to ASCII, lowercased and trimmed. Empty in, empty out — never null, so a
    /// folded value is always safe to compare against a stored one.
    /// </summary>
    string Fold(string? value);
}
