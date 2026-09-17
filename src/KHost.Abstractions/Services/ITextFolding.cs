namespace KHost.Abstractions.Services;

/// <summary>Folds text for search; via DI so transliteration stays out of plugins.</summary>
public interface ITextFolding
{
    /// <summary>Transliterated to ASCII, lowercased, trimmed; never null.</summary>
    string Fold(string? value);
}
