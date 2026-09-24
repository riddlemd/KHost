namespace KHost.Abstractions.Models;

// Values are explicit and permanent: groups store these as numbers in a JSON column, so a
// renumbered member silently becomes a different permission in every stored group.
/// <summary>One thing a user group may be allowed to do; a user has whatever their groups grant.</summary>
public enum KHostPermission
{
    /// <summary>Create or edit a user account.</summary>
    [PermissionGroup("Users")]
    EditUser = 0,

    /// <summary>Delete a user account.</summary>
    [PermissionGroup("Users")]
    DeleteUser = 1,

    /// <summary>Create or edit a user group, including its permissions.</summary>
    [PermissionGroup("Groups")]
    EditGroup = 2,

    /// <summary>Delete a user group.</summary>
    [PermissionGroup("Groups")]
    DeleteGroup = 3,

    /// <summary>Create or edit a venue's settings.</summary>
    [PermissionGroup("Venue")]
    EditVenue = 4,

    /// <summary>Delete a venue.</summary>
    [PermissionGroup("Venue")]
    DeleteVenue = 5,

    /// <summary>Import media into the library.</summary>
    [PermissionGroup("Library")]
    ImportLibrary = 6,

    /// <summary>Delete a library row.</summary>
    [PermissionGroup("Library")]
    DeleteMedia = 7,

    /// <summary>Edit a library row's own fields, short of deleting it.</summary>
    [PermissionGroup("Library")]
    ManageMedia = 13,

    /// <summary>Add a song to the singer queue.</summary>
    [PermissionGroup("Queue")]
    AddToQueue = 8,

    /// <summary>Remove a song from the singer queue.</summary>
    [PermissionGroup("Queue")]
    RemoveFromQueue = 9,

    /// <summary>Change the order of the singer queue.</summary>
    [PermissionGroup("Queue")]
    ReorderQueue = 10,

    /// <summary>Skip past the singer at the front of the queue.</summary>
    [PermissionGroup("Queue")]
    SkipQueue = 11,

    /// <summary>View a singer's past performances.</summary>
    [PermissionGroup("History")]
    ViewPerformanceHistory = 12
}
