namespace KHost.Abstractions.Models;

public interface ISongSearchEntity
{
    string FilePath { get; }
    string DisplayName { get; }
    string Format { get; }
}
