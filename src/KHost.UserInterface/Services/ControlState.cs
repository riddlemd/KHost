namespace KHost.UserInterface.Services;

/// <summary>Console control state that outlives the panel holding it.</summary>
/// <remarks>A panel is rebuilt when the selected singer changes; session-lived, reset on restart.</remarks>
public interface IControlState
{
    /// <summary>Which source Song Search uses by default; null/empty means the local library.</summary>
    string? MediaSearchSource { get; set; }
}

public sealed class ControlState : IControlState
{
    public string? MediaSearchSource { get; set; }
}
