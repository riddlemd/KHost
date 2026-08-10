namespace KHost.Abstractions.Services;

public interface IPasswordHasher
{
    Task<string> HashAsync(string password);
    Task<bool> VerifyAsync(string password, string storedHash);
}
