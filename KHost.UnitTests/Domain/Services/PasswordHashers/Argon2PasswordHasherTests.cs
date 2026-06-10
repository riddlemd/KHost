using KHost.Domain.Services.PasswordHashers;

namespace KHost.UnitTests.Domain.Services.PasswordHashers;

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public async Task HashAsync_ReturnsV2PrefixedString()
    {
        var hash = await _hasher.HashAsync("password");

        Assert.StartsWith("v2:", hash);
        Assert.Equal(3, hash.Split(':').Length);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsTrue_ForCorrectPassword()
    {
        var hash = await _hasher.HashAsync("correct-password");

        var result = await _hasher.VerifyAsync("correct-password", hash);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_ForWrongPassword()
    {
        var hash = await _hasher.HashAsync("correct-password");

        var result = await _hasher.VerifyAsync("wrong-password", hash);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_ForCorruptedStoredHash()
    {
        var result = await _hasher.VerifyAsync("password", "not-a-valid-hash");

        Assert.False(result);
    }

    [Fact]
    public async Task HashAsync_ProducesDifferentHashesForSamePassword()
    {
        var hash1 = await _hasher.HashAsync("password");
        var hash2 = await _hasher.HashAsync("password");

        Assert.NotEqual(hash1, hash2);
    }
}
