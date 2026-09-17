using KHost.Abstractions.Services;

namespace KHost.DataAccess.Contexts;

/// <summary>Wraps <see cref="EntityFolding"/> rather than reimplementing it.</summary>
/// <remarks>Folding differently here would quietly stop matching what that class wrote on save.</remarks>
internal sealed class TextFolding : ITextFolding
{
    public string Fold(string? value) => EntityFolding.Fold(value);
}
