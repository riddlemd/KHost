using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Authentication;

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
            return AuthResults.Failed("No password set for this user");

        var valid = await _passwordHasher.VerifyAsync(password, user.PasswordHash);
        return valid
            ? AuthResults.Succeeded(user)
            : AuthResults.Failed("Invalid name or password");
    }
}
