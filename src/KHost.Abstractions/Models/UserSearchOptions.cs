namespace KHost.Abstractions.Models;

/// <summary>Extra conditions for a user search, passed through the searchable options hook.</summary>
public sealed class UserSearchOptions
{
    /// <summary>Leaves out anyone in a group flagged ExcludeFromSingerQueue.</summary>
    public bool SingersOnly { get; set; }
}
