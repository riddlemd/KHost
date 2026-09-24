namespace KHost.Abstractions.Services;

/// <summary>Turns a password into something safe to store, and checks one against it later.</summary>
/// <remarks>A plugin that collects a password to verify later may take this and keep only the hash;
/// a raw password is never kept. Hashing is deliberately slow and memory-hungry, so do not call it in
/// a loop. A host singleton, callable from any thread.</remarks>
public interface IPasswordHasher
{
    /// <summary>A salted hash of <paramref name="password"/>, self-describing enough for
    /// <see cref="VerifyAsync"/> to check. Two calls with the same password give different
    /// strings.</summary>
    Task<string> HashAsync(string password);

    /// <summary>Whether <paramref name="password"/> matches a hash from <see cref="HashAsync"/>.
    /// </summary>
    /// <returns>False, never an exception, for a mismatch or a hash this hasher does not
    /// recognise.</returns>
    Task<bool> VerifyAsync(string password, string storedHash);
}
