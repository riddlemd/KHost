using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages;

public partial class LoginPage
{
    /// <summary>
    /// Set by the login endpoint's redirect when the credentials were wrong. A string, not a
    /// bool: the query binder throws on anything but true/false, and a crashed circuit renders
    /// a white window instead of an error banner.
    /// </summary>
    [SupplyParameterFromQuery(Name = "failed")]
    public string? Failed { get; set; }
}
