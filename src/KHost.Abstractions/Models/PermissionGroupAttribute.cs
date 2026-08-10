namespace KHost.Abstractions.Models;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PermissionGroupAttribute(string groupName) : Attribute
{
    public string GroupName { get; } = groupName;
}
