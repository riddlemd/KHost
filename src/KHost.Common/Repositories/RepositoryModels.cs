using KHost.Abstractions.Models;

namespace KHost.Common.Repositories;

/// <summary>Tells a seeded, built-in row from one a host created.</summary>
public static class RepositoryModels
{
    // Seeded rows are the only ones with an all-zero id prefix, so the prefix marks a record as
    // built in without anyone having to maintain a list of them.
    private const string IdPrefix = "00000000-0000-0000-0000-";

    /// <summary>Whether <paramref name="id"/> is one of the seeded, built-in rows.</summary>
    public static bool IsBuiltIn(Guid id) => id.ToString().StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="model"/> is one of the seeded, built-in rows.</summary>
    public static bool IsBuiltIn(this RepositoryModel model) => IsBuiltIn(model.Id);
}
