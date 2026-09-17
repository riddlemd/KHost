using System.Security.Cryptography;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Auth;

/// <summary>Physical access to launch this already implies ownership of the database file.</summary>
/// <remarks>So this grants nothing new; it avoids hand-crafting an Argon2 hash into SQLite by hand.</remarks>
internal static class PasswordReset
{
    // No 0/O/1/l/I: this password gets read off a terminal and typed back in.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
    private const int Length = 12;

    internal static async Task<int> RunAsync(
        string name, IUsersRepository users, IPasswordHasher hasher, TextWriter output)
    {
        var user = await users.FindByNameAsync(name);

        if (user is null)
        {
            await output.WriteLineAsync($"No user named '{name}' exists.");
            return 1;
        }

        var password = GeneratePassword();
        user.PasswordHash = await hasher.HashAsync(password);
        await users.UpdateAsync(user);

        await output.WriteLineAsync($"Password for '{user.Name}' is now: {password}");
        await output.WriteLineAsync("Sign in with it, then set your own in the user editor.");
        return 0;
    }

    private static string GeneratePassword()
        => new(Enumerable.Range(0, Length)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])
            .ToArray());
}
