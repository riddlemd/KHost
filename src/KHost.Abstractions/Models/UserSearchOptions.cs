namespace KHost.Abstractions.Models;

/// <summary>Extra conditions for a user search, passed to
/// <see cref="Services.IUsersService"/>'s typed <c>SearchAsync</c>.</summary>
public sealed class UserSearchOptions
{
    /// <summary>Leaves out anyone in a group flagged ExcludeFromSingerQueue.</summary>
    public bool SingersOnly { get; set; }
}
