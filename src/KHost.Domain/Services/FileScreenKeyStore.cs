using System.Security.Cryptography;
using System.Text;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>
/// Per-screen keys as files under cache/screens/. The filename is a hash of the screen id, never the
/// id itself: a screen names its own id, so a crafted one carrying path separators must not be able
/// to escape the directory or address another screen's key file.
/// </summary>
public sealed class FileScreenKeyStore : IScreenKeyStore
{
    private const int KeyBytes = 32;

    private readonly string _root;
    private readonly ILogger<FileScreenKeyStore> _logger;

    public FileScreenKeyStore(ILogger<FileScreenKeyStore> logger)
        : this(Path.Combine(AppContext.BaseDirectory, "cache", "screens"), logger)
    {
    }

    internal FileScreenKeyStore(string root, ILogger<FileScreenKeyStore> logger)
    {
        _root = root;
        _logger = logger;
    }

    public string Provision(string screenId)
    {
        Directory.CreateDirectory(_root);

        var path = PathFor(screenId);
        File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes)));

        // Owner-only where the platform honours it: a key another local user can read is a key.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return path;
    }

    public byte[]? GetKey(string screenId)
    {
        var path = PathFor(screenId);
        if (!File.Exists(path)) return null;

        try
        {
            return Convert.FromBase64String(File.ReadAllText(path).Trim());
        }
        catch (FormatException)
        {
            _logger.LogWarning("Screen key file for '{ScreenId}' is not valid base64", screenId);
            return null;
        }
    }

    public void Revoke(string screenId)
    {
        var path = PathFor(screenId);

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not remove screen key for '{ScreenId}'", screenId);
        }
    }

    private string PathFor(string screenId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(screenId));
        return Path.Combine(_root, Convert.ToHexStringLower(hash) + ".key");
    }
}
