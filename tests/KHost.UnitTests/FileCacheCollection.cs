namespace KHost.UnitTests;

/// <summary>One test deletes the shared directory, so every class touching it shares a collection.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class FileCacheCollection
{
    public const string Name = "file-cache";
}
