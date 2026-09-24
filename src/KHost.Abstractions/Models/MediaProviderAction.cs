namespace KHost.Abstractions.Models;

/// <summary>One action a host can take on a <see cref="MediaSearchEntity"/> search result.</summary>
public class MediaProviderAction
{
    /// <summary>The action's label, shown on its button or menu entry.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Longer text for a tooltip. Null shows none.</summary>
    public string? Description { get; set; }

    /// <summary>A Bootstrap Icons name shown beside <see cref="DisplayName"/>. Null shows none.</summary>
    public string? Icon { get; set; }

    /// <summary>Actions nested under this one, offered as a submenu.</summary>
    public List<MediaProviderAction> SubActions { get; set; } = [];

    /// <summary>Re-runs the search after this action, for one that changes results, e.g. sign-in.</summary>
    public bool RefreshesResults { get; set; }

    /// <summary>Runs the action against the result it was offered on.</summary>
    public required Func<MediaSearchEntity, Task> PerformAsync { get; set; }
}
