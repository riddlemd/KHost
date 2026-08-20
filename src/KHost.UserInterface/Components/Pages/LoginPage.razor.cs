using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages;

public partial class LoginPage
{
    /// <summary>Set by the login endpoint's redirect when the credentials were wrong.</summary>
    [SupplyParameterFromQuery(Name = "failed")]
    public bool Failed { get; set; }
}
