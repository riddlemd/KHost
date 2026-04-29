namespace KHost.Abstractions.Models;

public abstract class RepositoryModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
