namespace KHost.Abstractions.Models;

public abstract class RepositoryModel
{
    // Seeded rows are the only ones with an all-zero id prefix, so the prefix marks a record as
    // built in without anyone having to maintain a list of them.
    private const string BuiltInIdPrefix = "00000000-0000-0000-0000-";

    public Guid Id { get; set; } = Guid.NewGuid();

    public static bool IsBuiltIn(Guid id)
        => id.ToString().StartsWith(BuiltInIdPrefix, StringComparison.Ordinal);
}
