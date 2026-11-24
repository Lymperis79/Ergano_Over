using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SPharmacyLog.Data;

namespace SPharmacyLog.UI.Services;

public static class InstallationSettingsTransfer
{
    private const int Iterations = 250_000;
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static readonly string[] Files = { "connection.json", "weather.json", "company.json", "appearance.json", "application.json" };
    private sealed record Entry(string Name, string Content);
    private sealed record Bundle(string Application, int Version, Entry[] Entries, string? LogoName, string? LogoBase64);

    public static async Task ExportAsync(string path, string password)
    {
        ValidatePassword(password);
        var entries = Files.Where(f => File.Exists(Path.Combine(Folder, f)))
            .Select(f => new Entry(f, File.ReadAllText(Path.Combine(Folder, f)))).ToList();
        entries.Add(new Entry("customer-sync-settings.json", JsonSerializer.Serialize(CustomerDatabaseSyncService.LoadSettings())));\n        var brandingPath = Path.Combine(AppContext.BaseDirectory, "application-settings.json");\n        if (File.Exists(brandingPath)) entries.Add(new Entry("application-settings.json", File.ReadAllText(brandingPath)));
        string? logoName = null, logoBase64 = null;
        var companyPath = Path.Combine(Folder, "company.json");
        if (File.Exists(companyPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(companyPath));
                if (doc.RootElement.TryGetProperty("LogoFile", out var logo))
                {
                    var logoPath = logo.GetString();
                    if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                    { logoName = Path.GetFileName(logoPath); logoBase64 = Convert.ToBase64String(File.ReadAllBytes(logoPath)); }
                }
            } catch { }
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Bundle("SPharmacyLog", 1, entries.ToArray(), logoName, logoBase64));
        await EncryptFileAsync(path, payload, password);
    }

    public static async Task<string[]> ImportAsync(string path, string password)
    {
        ValidatePassword(password);
        var bundle = JsonSerializer.Deserialize<Bundle>(await DecryptFileAsync(path, password))
            ?? throw new InvalidDataException("Configuration package is empty.");
        if (bundle.Application != "SPharmacyLog" || bundle.Version != 1)
            throw new InvalidDataException("This is not a compatible PharmaPoint settings package.");
        foreach (var entry in bundle.Entries)
            if (!(Files.Contains(entry.Name, StringComparer.Ordinal) || entry.Name is "customer-sync-settings.json" or "application-settings.json") || Path.GetFileName(entry.Name) != entry.Name)
                throw new InvalidDataException("Package contains an unsupported configuration file.");
        Directory.CreateDirectory(Folder);
        var names = new List<string>();
        foreach (var entry in bundle.Entries)
        {
            if (entry.Name == "application-settings.json")\n            {\n                await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, entry.Name), entry.Content);\n                names.Add("application branding");\n                continue;\n            }\n            if (entry.Name == "customer-sync-settings.json")
            {
                CustomerDatabaseSyncService.SaveSettings(JsonSerializer.Deserialize<CustomerDatabaseSyncSettings>(entry.Content) ?? new());
                names.Add("customer database synchronization settings");
                continue;
            }
            if (entry.Name == "connection.json")
            {
                var config = JsonSerializer.Deserialize<DbConfig>(entry.Content);
                if (config?.DatabaseProvider == DatabaseProvider.Sqlite) config.SqlitePath = PharmacyDb.DefaultSqlitePath;
                await File.WriteAllTextAsync(Path.Combine(Folder, entry.Name), JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            else await File.WriteAllTextAsync(Path.Combine(Folder, entry.Name), entry.Content);
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
                    using var doc = JsonDocument.Parse(File.ReadAllText(companyPath));
                    var name = doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() : "";
                    await File.WriteAllTextAsync(companyPath, JsonSerializer.Serialize(new { Name = name, LogoFile = target }, new JsonSerializerOptions { WriteIndented = true }));
                }
                names.Add("company logo");
            }
        }
        else
        {
            var companyPath = Path.Combine(Folder, "company.json");
            if (File.Exists(companyPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(companyPath));
                var name = doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() : "";
                await File.WriteAllTextAsync(companyPath, JsonSerializer.Serialize(new { Name = name, LogoFile = (string?)null }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        return names.ToArray();
    }

    private static async Task EncryptFileAsync(string path, byte[] payload, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[payload.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, payload, ciphertext, tag);
        await using var stream = File.Create(path);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("SPHCFG1"));
        await stream.WriteAsync(salt); await stream.WriteAsync(nonce); await stream.WriteAsync(tag); await stream.WriteAsync(ciphertext);
        CryptographicOperations.ZeroMemory(key);
    }
    private static async Task<byte[]> DecryptFileAsync(string path, string password)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.Length < 51 || Encoding.ASCII.GetString(bytes, 0, 7) != "SPHCFG1") throw new InvalidDataException("Not a valid PharmaPoint settings package.");
        var salt = bytes.AsSpan(7, 16).ToArray(); var nonce = bytes.AsSpan(23, 12).ToArray(); var tag = bytes.AsSpan(35, 16).ToArray(); var cipher = bytes.AsSpan(51).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32); var clear = new byte[cipher.Length];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, clear); return clear; }
        catch (CryptographicException) { throw new InvalidDataException("Wrong password or corrupted settings package."); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void ValidatePassword(string password) { if (string.IsNullOrWhiteSpace(password) || password.Length < 8) throw new ArgumentException("Package password must be at least 8 characters."); }
}
