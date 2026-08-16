using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Components.Setup;

public partial class WizardStep1AdminUser
{
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private IUserGroupsService? UserGroupsService { get; set; }
    [Inject] private IPasswordHasher? PasswordHasher { get; set; }

    [Parameter]
    public EventCallback OnComplete { get; set; }

    private SetupAdminUserModel _model = new();
    private EditContext _editContext = default!;
    private string _serverError = string.Empty;
    private bool _isLoading = false;

    private bool IsFormValid =>
        Validator.TryValidateObject(_model, new ValidationContext(_model), null, validateAllProperties: true);

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    private async Task OnNextAsync()
    {
        _serverError = string.Empty;
        _isLoading = true;

        try
        {
            var passwordHash = await PasswordHasher!.HashAsync(_model.Password);
            var adminUser = new KHostUser { Name = _model.Username, PasswordHash = passwordHash };
            await UsersService!.CreateAsync(adminUser);
            await UserGroupsService!.AddUserToGroupAsync(adminUser.Id, KHostUserGroup.AdminGroupId);
            await OnComplete.InvokeAsync();
        }
        catch (Exception ex)
        {
            _serverError = $"Failed to create admin user: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }
}
