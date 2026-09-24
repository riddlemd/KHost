namespace KHost.Abstractions.Models;

/// <summary>Marks a permission enum value with the heading it is shown under when a host edits a
/// group's permissions.</summary>
/// <param name="groupName">The heading; permissions sharing one value are shown together.</param>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PermissionGroupAttribute(string groupName) : Attribute
{
    /// <summary>The heading this permission is shown under.</summary>
    public string GroupName { get; } = groupName;
}
