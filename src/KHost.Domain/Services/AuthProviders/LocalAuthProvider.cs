using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.AuthProviders;

public class LocalAuthProvider : IAuthProvider
{
    private readonly IPasswordHasher _passwordHasher;

    public LocalAuthProvider(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    public bool CanHandle(KHostUser user) => user.PasswordHash is not null;

    public async Task<AuthResult> AuthenticateAsync(KHostUser user, string password)
    {
        if (user.PasswordHash is null)
            return AuthResult.Fail("No password set for this user");

        var valid = await _passwordHasher.VerifyAsync(password, user.PasswordHash);
        return valid
            ? AuthResult.Ok(user)
            : AuthResult.Fail("Invalid name or password");
    }
}
