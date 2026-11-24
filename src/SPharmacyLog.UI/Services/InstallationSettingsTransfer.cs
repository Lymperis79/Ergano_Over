using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SPharmacyLog.UI.Services;

/// <summary>Password-protected portable configuration backup. Database files and user data are never included.</summary>
public static class InstallationSettingsTransfer
{
    private const int Iterations = 250_000;
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static readonly string[] Files = { "connection.json", "weather.json", "customer-database-sync.json", "company.json", "appearance.json", "application.json" };
    private sealed record Entry(string Name, string Content);
    private sealed record Bundle(string Application, int Version, Entry[] Entries, string? LogoName, string? LogoBase64);

    public static async Task ExportAsync(string path, string password)
    {
        ValidatePassword(password);
        var entries = Files.Where(f => File.Exists(Path.Combine(Folder, f)))
            .Select(f => new Entry(f, File.ReadAllText(Path.Combine(Folder, f)))).ToArray();
        string? logoName = null, logoBase64 = null;
        var companyFile = Path.Combine(Folder, "company.json");
        if (File.Exists(companyFile))
        {
            try
            {
                var company = JsonDocument.Parse(File.ReadAllText(companyFile));
                if (company.RootElement.TryGetProperty("LogoFile", out var logo) && logo.ValueKind == JsonValueKind.String)
                {
                    var logoPath = logo.GetString();
                    if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                    {
                        logoName = Path.GetFileName(logoPath);
                        logoBase64 = Convert.ToBase64String(File.ReadAllBytes(logoPath));
                    }
                }
            } catch { }
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Bundle("SPharmacyLog", 1, entries, logoName, logoBase64));
        await EncryptFileAsync(path, payload, password);
    }

    public static async Task<string[]> ImportAsync(string path, string password)
    {
        ValidatePassword(password);
        var bytes = await DecryptFileAsync(path, password);
        var bundle = JsonSerializer.Deserialize<Bundle>(bytes) ?? throw new InvalidDataException("Configuration package is empty.");
        if (bundle.Application != "SPharmacyLog" || bundle.Version != 1) throw new InvalidDataException("This package is not a compatible PharmaPoint settings export.");
        Directory.CreateDirectory(Folder);
        var names = new List<string>();
        foreach (var entry in bundle.Entries)
        {
            if (!Files.Contains(entry.Name, StringComparer.Ordinal) || Path.GetFileName(entry.Name) != entry.Name)
                throw new InvalidDataException("The package contains an unsupported configuration file.");
        }
        foreach (var entry in bundle.Entries)
        {
            var target = Path.Combine(Folder, entry.Name);
            await File.WriteAllTextAsync(target, entry.Content);
            names.Add(entry.Name);
        }
        if (!string.IsNullOrWhiteSpace(bundle.LogoName) && !string.IsNullOrWhiteSpace(bundle.LogoBase64))
        {
            var ext = Path.GetExtension(bundle.LogoName).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                var target = Path.Combine(Folder, "company-logo" + ext);
                await File.WriteAllBytesAsync(target, Convert.FromBase64String(bundle.LogoBase64));
                var companyPath = Path.Combine(Folder, "company.json");
                if (File.Exists(companyPath))
                {
                    var company = JsonDocument.Parse(File.ReadAllText(companyPath));
                    var name = company.RootElement.TryGetProperty("Name", out var n) ? n.GetString() : "";
                    await File.WriteAllTextAsync(companyPath, JsonSerializer.Serialize(new { Name = name, LogoFile = target }, new JsonSerializerOptions { WriteIndented = true }));
                }
                names.Add("company logo");
            }
        }
        return names.ToArray();
    }

    private static async Task EncryptFileAsync(string path, byte[] payload, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[payload.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, payload, cipher, tag);
        await using var stream = File.Create(path);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("SPHCFG1"));
        await stream.WriteAsync(salt); await stream.WriteAsync(nonce); await stream.WriteAsync(tag); await stream.WriteAsync(cipher);
        CryptographicOperations.ZeroMemory(key);
    }
    private static async Task<byte[]> DecryptFileAsync(string path, string password)
    {
        var all = await File.ReadAllBytesAsync(path);
        if (all.Length < 51 || Encoding.ASCII.GetString(all, 0, 7) != "SPHCFG1") throw new InvalidDataException("Not a valid PharmaPoint configuration package.");
        var salt = all.AsSpan(7,16).ToArray(); var nonce=all.AsSpan(23,12).ToArray(); var tag=all.AsSpan(35,16).ToArray(); var cipher=all.AsSpan(51).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var clear = new byte[cipher.Length];
        try { using var aes = new AesGcm(key,16); aes.Decrypt(nonce,cipher,tag,clear); return clear; }
        catch (CryptographicException) { throw new InvalidDataException("Incorrect password or the configuration package is damaged."); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void ValidatePassword(string password) { if (string.IsNullOrWhiteSpace(password) || password.Length < 8) throw new ArgumentException("Use a package password of at least 8 characters."); }
}
