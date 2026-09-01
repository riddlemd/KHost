using KHost.Abstractions.Services;

namespace KHost.DataAccess.Contexts;

/// <summary>
/// The host's own folding, handed out through DI. Wraps <see cref="EntityFolding"/> rather than
/// reimplementing it: the folded columns are written on save by that class, so anything that
/// folded differently here would quietly stop matching what is stored.
/// </summary>
internal sealed class TextFolding : ITextFolding
{
    public string Fold(string? value) => EntityFolding.Fold(value);
}
