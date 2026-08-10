namespace KHost.UnitTests;

/// <summary>
/// One cache test deletes the shared cache directory outright, so every class touching that
/// directory must share a collection or xUnit's parallel classes race the delete.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class FileCacheCollection
{
    public const string Name = "file-cache";
}
