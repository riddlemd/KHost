namespace KHost.Abstractions.Models;

// Values are explicit and permanent: groups store these as numbers in a JSON column, so a
// renumbered member silently becomes a different permission in every stored group.
public enum KHostPermission
{
    [PermissionGroup("Users")]
    EditUser = 0,

    [PermissionGroup("Users")]
    DeleteUser = 1,

    [PermissionGroup("Groups")]
    EditGroup = 2,

    [PermissionGroup("Groups")]
    DeleteGroup = 3,

    [PermissionGroup("Venue")]
    EditVenue = 4,

    [PermissionGroup("Venue")]
    DeleteVenue = 5,

    [PermissionGroup("Library")]
    ImportLibrary = 6,

    [PermissionGroup("Library")]
    DeleteMedia = 7,

    [PermissionGroup("Library")]
    ManageMedia = 13,

    [PermissionGroup("Queue")]
    AddToQueue = 8,

    [PermissionGroup("Queue")]
    RemoveFromQueue = 9,

    [PermissionGroup("Queue")]
    ReorderQueue = 10,

    [PermissionGroup("Queue")]
    SkipQueue = 11,

    [PermissionGroup("History")]
    ViewPerformanceHistory = 12
}
