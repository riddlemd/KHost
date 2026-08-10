namespace KHost.UnitTests;

/// <summary>
/// JsonFileCacheService writes to a single fixed directory under AppContext.BaseDirectory, and
/// one of its tests deletes that directory outright to prove SaveAsync recreates it. xUnit runs
/// test classes in parallel, so every class touching that directory has to share a collection or
/// the delete races whatever else is mid-write.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class FileCacheCollection
{
    public const string Name = "file-cache";
}
