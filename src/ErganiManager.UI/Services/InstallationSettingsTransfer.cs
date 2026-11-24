using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErganiManager.LocalCache;
using ErganiManager.Data;

namespace ErganiManager.UI.Services;

/// <summary>Password-protected portable configuration package. Does not include databases or user data.</summary>
public static class InstallationSettingsTransfer
{
    private const int Iterations = 250_000;
    private static readonly string Folder = AppPaths.GetAppDataFolder();
    private static readonly string[] Files = { "connection.json", "portal.json", "rounding.json", "appearance.json" };
    private sealed record Entry(string Name, string Content);
    private sealed record Bundle(string Application, int Version, Entry[] Entries);

    public static async Task ExportAsync(string path, string password)
    {
        ValidatePassword(password);
        var entries = Files.Where(f => File.Exists(Path.Combine(Folder, f))).Select(f => new Entry(f, File.ReadAllText(Path.Combine(Folder, f)))).ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Bundle("ErganiManager", 1, entries));
        await EncryptFileAsync(path, payload, password);
    }
    public static async Task<string[]> ImportAsync(string path, string password)
    {
        ValidatePassword(password);
        var bytes = await DecryptFileAsync(path, password);
        var bundle = JsonSerializer.Deserialize<Bundle>(bytes) ?? throw new InvalidDataException("Configuration package is empty.");
        if (bundle.Application != "ErganiManager" || bundle.Version != 1) throw new InvalidDataException("This package is not a compatible Ergani Manager settings export.");
        foreach (var entry in bundle.Entries)
            if (!Files.Contains(entry.Name, StringComparer.Ordinal) || Path.GetFileName(entry.Name) != entry.Name)
                throw new InvalidDataException("The package contains an unsupported configuration file.");
        Directory.CreateDirectory(Folder);
        foreach (var entry in bundle.Entries)
        {
            var content = entry.Content;
            if (entry.Name == "connection.json")
            {
                var config = JsonSerializer.Deserialize<DbConfig>(content);
                if (config?.DatabaseProvider == DatabaseProvider.Sqlite) config.SqlitePath = Path.Combine(AppPaths.GetAppDataFolder(), "erganimanager.db");
                content = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            }
            await File.WriteAllTextAsync(Path.Combine(Folder, entry.Name), content);
        }
        return bundle.Entries.Select(e => e.Name).ToArray();
    }
    private static async Task EncryptFileAsync(string path, byte[] payload, string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16); var nonce=RandomNumberGenerator.GetBytes(12); var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
        var cipher=new byte[payload.Length]; var tag=new byte[16]; using var aes=new AesGcm(key,16); aes.Encrypt(nonce,payload,cipher,tag);
        await using var stream=File.Create(path); await stream.WriteAsync(Encoding.ASCII.GetBytes("ERGCNF1")); await stream.WriteAsync(salt); await stream.WriteAsync(nonce); await stream.WriteAsync(tag); await stream.WriteAsync(cipher); CryptographicOperations.ZeroMemory(key);
    }
    private static async Task<byte[]> DecryptFileAsync(string path,string password)
    {
        var all=await File.ReadAllBytesAsync(path); if(all.Length<51||Encoding.ASCII.GetString(all,0,7)!="ERGCNF1") throw new InvalidDataException("Not a valid Ergani Manager settings package.");
        var salt=all.AsSpan(7,16).ToArray(); var nonce=all.AsSpan(23,12).ToArray(); var tag=all.AsSpan(35,16).ToArray(); var cipher=all.AsSpan(51).ToArray();
        var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32); var clear=new byte[cipher.Length];
        try{using var aes=new AesGcm(key,16);aes.Decrypt(nonce,cipher,tag,clear);return clear;}catch(CryptographicException){throw new InvalidDataException("Incorrect password or the configuration package is damaged.");}finally{CryptographicOperations.ZeroMemory(key);}
    }
    private static void ValidatePassword(string password){if(string.IsNullOrWhiteSpace(password)||password.Length<8)throw new ArgumentException("Use a package password of at least 8 characters.");}
}
