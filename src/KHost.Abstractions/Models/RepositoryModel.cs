namespace KHost.Abstractions.Models;

/// <summary>Base for a row that is persisted and needs a stable identity.</summary>
public abstract class RepositoryModel
{
    /// <summary>The row's own id. Generated when the object is created; a caller may also set one
    /// explicitly before it is first saved.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
}
