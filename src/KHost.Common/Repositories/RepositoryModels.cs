using KHost.Abstractions.Models;

namespace KHost.Common.Repositories;

public static class RepositoryModels
{
    // Seeded rows are the only ones with an all-zero id prefix, so the prefix marks a record as
    // built in without anyone having to maintain a list of them.
    private const string IdPrefix = "00000000-0000-0000-0000-";

    public static bool IsBuiltIn(Guid id) => id.ToString().StartsWith(IdPrefix, StringComparison.Ordinal);

    public static bool IsBuiltIn(this RepositoryModel model) => IsBuiltIn(model.Id);
}
