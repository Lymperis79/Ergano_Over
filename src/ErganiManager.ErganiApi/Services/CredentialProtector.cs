using System.Security.Cryptography;
using System.Text;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// Encrypts/decrypts secrets (Ergani passwords, SMTP passwords) before they
/// are stored in the database. Uses DPAPI on Windows (user-scoped, no key
/// management needed). On Linux, DPAPI is unavailable, so falls back to
/// AES with a machine-local key file — adequate for "don't store plaintext
/// in the DB" but NOT a substitute for OS-level disk encryption. Document
/// this clearly for Linux deployments: secure the key file's permissions.
/// </summary>
public class CredentialProtector : ICredentialProtector
{
    private readonly string _keyFilePath;

    public CredentialProtector(string appDataFolder)
    {
        _keyFilePath = Path.Combine(appDataFolder, ".keyfile");
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        if (OperatingSystem.IsWindows())
            return ProtectWindows(plainText);

        return ProtectAes(plainText);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return string.Empty;

        if (OperatingSystem.IsWindows())
            return UnprotectWindows(protectedText);

        return UnprotectAes(protectedText);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ProtectWindows(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string UnprotectWindows(string protectedText)
    {
        var bytes = Convert.FromBase64String(protectedText);
        var decrypted = ProtectedData.Unprotect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private byte[] GetOrCreateAesKey()
    {
        if (File.Exists(_keyFilePath))
            return Convert.FromBase64String(File.ReadAllText(_keyFilePath));

        var key = RandomNumberGenerator.GetBytes(32); // AES-256
        File.WriteAllText(_keyFilePath, Convert.ToBase64String(key));

        // Best-effort lock-down of permissions on POSIX systems.
        try
        {
            File.SetUnixFileMode(_keyFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
            // Not POSIX (shouldn't happen here since this branch is non-Windows-only
            // in practice, but guard anyway rather than crash).
        }

        return key;
    }

    private string ProtectAes(string plainText)
    {
        var key = GetOrCreateAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Store IV + ciphertext together, base64-encoded.
        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    private string UnprotectAes(string protectedText)
    {
        var key = GetOrCreateAesKey();
        var combined = Convert.FromBase64String(protectedText);

        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[16];
        Buffer.BlockCopy(combined, 0, iv, 0, iv.Length);
        aes.IV = iv;

        var cipherBytes = new byte[combined.Length - iv.Length];
        Buffer.BlockCopy(combined, iv.Length, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
