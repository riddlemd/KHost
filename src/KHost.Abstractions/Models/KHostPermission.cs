namespace KHost.Abstractions.Models;

public enum KHostPermission
{
    [PermissionGroup("Users")]
    EditUser,

    [PermissionGroup("Users")]
    DeleteUser,

    [PermissionGroup("Groups")]
    EditGroup,

    [PermissionGroup("Groups")]
    DeleteGroup,

    [PermissionGroup("Venue")]
    EditVenue,

    [PermissionGroup("Venue")]
    DeleteVenue,

    [PermissionGroup("Library")]
    ImportLibrary,

    [PermissionGroup("Library")]
    DeleteMedia,

    [PermissionGroup("Queue")]
    AddToQueue,

    [PermissionGroup("Queue")]
    RemoveFromQueue,

    [PermissionGroup("Queue")]
    ReorderQueue,

    [PermissionGroup("Queue")]
    SkipQueue,

    [PermissionGroup("History")]
    ViewPerformanceHistory
}
