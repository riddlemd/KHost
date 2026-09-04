namespace KHost.UserInterface.Services;

/// <summary>
/// Console control state that has to outlive the control holding it — a panel is torn down and
/// rebuilt when the selected singer changes, and an operator's pick must not go with it. One
/// holder for every such value rather than an interface per setting. Session-lived: a restart
/// starts each control back on its default.
/// </summary>
public interface IControlState
{
    /// <summary>
    /// Which source the Song Search panel searches by default — a provider's <c>SourceName</c>,
    /// or null/empty for the local library. A Example venue picks Example once, not every time it
    /// comes back to the search.
    /// </summary>
    string? MediaSearchSource { get; set; }
}

public sealed class ControlState : IControlState
{
    public string? MediaSearchSource { get; set; }
}
