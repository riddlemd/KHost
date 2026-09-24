namespace KHost.Abstractions.Services;

/// <summary>Folds text for search; via DI so transliteration stays out of plugins.</summary>
/// <remarks>The same fold the host stores beside names it matches on — the <c>NameFolded</c> columns
/// of users, venues, groups and playlists, and a tip's folded notes — so a plugin comparing against
/// those must fold through this, not its own rule. A media row's folded search text also resolves
/// stylised spellings first, so it is not exactly this fold. A plugin TAKES it; a host singleton,
/// pure and callable from any thread.</remarks>
public interface ITextFolding
{
    /// <summary>Transliterated to ASCII, lowercased, trimmed; never null.</summary>
    /// <returns>Empty for null or empty input.</returns>
    string Fold(string? value);
}
