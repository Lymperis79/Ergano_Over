using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Stores optional remembered login details in an app-specific file protected by Windows DPAPI.
/// This file and its entropy are unique to S Pharmacy Log and are not shared with ErganiManager.
/// </summary>
public static class RememberedLoginService
{
    private const string AppEntropy = "SPharmacyLog.RememberedLogin.v1";
    private sealed class Credentials
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static string FilePath => Path.Combine(Folder, "remembered-login.dat");

    public static (string Username, string Password)? Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(FilePath)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(FilePath);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Encoding.UTF8.GetBytes(AppEntropy), DataProtectionScope.CurrentUser);
            var credentials = JsonSerializer.Deserialize<Credentials>(clearBytes);
            CryptographicOperations.ZeroMemory(clearBytes);
            if (credentials is null || string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrEmpty(credentials.Password))
                return null;
            return (credentials.Username, credentials.Password);
        }
        catch
        {
            Delete();
            return null;
        }
    }

    public static void Save(string username, string password)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var clearBytes = JsonSerializer.SerializeToUtf8Bytes(new Credentials { Username = username, Password = password });
            var protectedBytes = ProtectedData.Protect(clearBytes, Encoding.UTF8.GetBytes(AppEntropy), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, protectedBytes);
            CryptographicOperations.ZeroMemory(clearBytes);
        }
        catch
        {
            // Do not block sign-in if Windows credential protection/storage is unavailable.
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
