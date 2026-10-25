using System.Security.Cryptography;
using System.Text;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// Encrypts/decrypts secrets (Ergani passwords, SMTP passwords) before they are stored in the database.
///
/// Every PC that uses the same database must be able to read what another PC saved, so the passwords are
/// encrypted with AES-256-GCM using ONE key per database. That key is created automatically the first time and
/// kept in the database (table AppSecrets); each PC also keeps a copy of it (protected for this Windows user), so
/// scans can still be handled while the database is not reachable.
///
/// Older values were encrypted with Windows DPAPI for one user on one PC (or with a local key file on Linux).
/// They are still read on the PC that wrote them, and the program converts them to the new format at the first
/// start there (see CompanyService.MigrateCredentialsAsync). New values start with "v2:".
/// </summary>
public class CredentialProtector : ICredentialProtector
{
    private const string Prefix = "v2:";
    private const string KeyName = "CredentialKey";
    private static readonly TimeSpan KeyCacheTime = TimeSpan.FromMinutes(10);

    private static readonly object Gate = new();
    private static byte[]? _masterKey;
    private static DateTime _masterKeyLoadedAt;

    private readonly string _legacyKeyFilePath;
    private readonly string _localKeyPath;
    private readonly IConnectionStateService _connectionState;

    public CredentialProtector(string appDataFolder, IConnectionStateService connectionState)
    {
        _legacyKeyFilePath = Path.Combine(appDataFolder, ".keyfile");
        _localKeyPath = Path.Combine(appDataFolder, ".credkey");
        _connectionState = connectionState;
    }

    public bool IsPortable(string? protectedText) =>
        protectedText != null && protectedText.StartsWith(Prefix, StringComparison.Ordinal);

    // -- Protect / Unprotect ----------------------------------------------------------------------

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var key = GetMasterKey();
        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using (var gcm = new AesGcm(key, 16))
            gcm.Encrypt(nonce, plain, cipher, tag);

        var combined = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, combined, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(combined);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return string.Empty;

        return IsPortable(protectedText)
            ? UnprotectPortable(protectedText[Prefix.Length..])
            : UnprotectLegacy(protectedText);
    }

    private string UnprotectPortable(string base64)
    {
        try
        {
            var combined = Convert.FromBase64String(base64);
            var nonce = combined.AsSpan(0, 12);
            var tag = combined.AsSpan(12, 16);
            var cipher = combined.AsSpan(28);
            var plain = new byte[cipher.Length];

            using (var gcm = new AesGcm(GetMasterKey(), 16))
                gcm.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new CredentialUnreadableException(
                ErganiManager.Data.UiText.T("The saved password cannot be read with this database's encryption key. Enter the password again in Companies."), ex);
        }
    }

    // -- The key shared by all PCs of this database ------------------------------------------------

    private byte[] GetMasterKey()
    {
        lock (Gate)
        {
            if (_masterKey != null && DateTime.UtcNow - _masterKeyLoadedAt < KeyCacheTime)
                return _masterKey;

            // 1. The key in the database - the same for every PC that uses it.
            var fromDatabase = ReadOrCreateDatabaseKey();
            if (fromDatabase != null)
            {
                _masterKey = fromDatabase;
                _masterKeyLoadedAt = DateTime.UtcNow;
                SaveLocalCopy(fromDatabase);
                return fromDatabase;
            }

            // 2. The database cannot be reached: the copy this PC kept earlier.
            if (_masterKey != null) return _masterKey;
            var local = ReadLocalCopy();
            if (local != null)
            {
                _masterKey = local;
                _masterKeyLoadedAt = DateTime.UtcNow;
                return local;
            }
        }

        throw new CredentialUnreadableException(
            ErganiManager.Data.UiText.T("The encryption key of this database is not available. Connect to the database once, or enter the password again in Companies."));
    }

    private AppDbContext? OpenDb()
    {
        var config = _connectionState.LoadConfig();
        if (config == null) return null;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);
        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>Reads the shared key; creates it when this is the first PC. Null when the database is not reachable.</summary>
    private byte[]? ReadOrCreateDatabaseKey()
    {
        try
        {
            using var db = OpenDb();
            if (db == null) return null;

            var existing = db.AppSecrets.AsNoTracking().FirstOrDefault(s => s.Name == KeyName);
            if (existing != null) return Convert.FromBase64String(existing.Value);

            var key = RandomNumberGenerator.GetBytes(32);
            db.AppSecrets.Add(new AppSecret { Name = KeyName, Value = Convert.ToBase64String(key) });
            try
            {
                db.SaveChanges();
                return key;
            }
            catch (DbUpdateException)
            {
                // Another PC created the key at the same moment: use theirs.
                using var again = OpenDb();
                var theirs = again?.AppSecrets.AsNoTracking().FirstOrDefault(s => s.Name == KeyName);
                return theirs != null ? Convert.FromBase64String(theirs.Value) : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>This PC's own copy of the key, so saved passwords still work while the database is unreachable.</summary>
    private void SaveLocalCopy(byte[] key)
    {
        try
        {
            var data = OperatingSystem.IsWindows()
                ? ProtectedData.Protect(key, optionalEntropy: null, scope: DataProtectionScope.CurrentUser)
                : key;
            File.WriteAllText(_localKeyPath, Convert.ToBase64String(data));

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_localKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Not critical: the key is read from the database next time.
        }
    }

    private byte[]? ReadLocalCopy()
    {
        try
        {
            if (!File.Exists(_localKeyPath)) return null;
            var data = Convert.FromBase64String(File.ReadAllText(_localKeyPath));
            return OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(data, optionalEntropy: null, scope: DataProtectionScope.CurrentUser)
                : data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // -- Values saved by older versions (Windows DPAPI for one user on one PC / local key file) -------

    private string UnprotectLegacy(string protectedText)
    {
        try
        {
            return OperatingSystem.IsWindows() ? UnprotectWindows(protectedText) : UnprotectAes(protectedText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new CredentialUnreadableException(
                ErganiManager.Data.UiText.T("The saved password was encrypted on another PC and cannot be read here. Start the program once on the PC where it was entered (it converts the password for all PCs), or enter the password again in Companies."), ex);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string UnprotectWindows(string protectedText)
    {
        var bytes = Convert.FromBase64String(protectedText);
        var decrypted = ProtectedData.Unprotect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private string UnprotectAes(string protectedText)
    {
        if (!File.Exists(_legacyKeyFilePath))
            throw new CryptographicException("No local key file.");

        var key = Convert.FromBase64String(File.ReadAllText(_legacyKeyFilePath));
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
