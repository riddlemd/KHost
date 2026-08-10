using System.Security.Cryptography;
using System.Text;
using KHost.Abstractions.Services;
using Konscious.Security.Cryptography;

namespace KHost.Domain.Services.PasswordHashers;

public class Argon2PasswordHasher : IPasswordHasher
{
    private const int _memorySize = 19456;
    private const int _iterations = 2;
    private const int _degreeOfParallelism = 1;
    private const int _hashSize = 32;
    private const int _saltSize = 16;

    public async Task<string> HashAsync(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(_saltSize);
        var hash = await ComputeHashAsync(password, salt);
        return $"v2:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public async Task<bool> VerifyAsync(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 3 || parts[0] != "v2")
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = await ComputeHashAsync(password, salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<byte[]> ComputeHashAsync(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.MemorySize = _memorySize;
        argon2.Iterations = _iterations;
        argon2.DegreeOfParallelism = _degreeOfParallelism;
        return await argon2.GetBytesAsync(_hashSize);
    }
}
