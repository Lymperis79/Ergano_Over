using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ErganiManager.Licensing;

public enum LicenseCheck
{
    Valid,
    /// <summary>No license has been entered yet.</summary>
    Missing,
    InvalidFormat,
    BadSignature,
    WrongMachine,
    Expired,
    /// <summary>The system date was set back to stretch an expiring license.</summary>
    ClockTampered,
    /// <summary>The public key built into the program is damaged.</summary>
    NotConfigured
}

public sealed record LicenseResult(LicenseCheck Check, LicensePayload? Payload)
{
    public bool IsValid => Check == LicenseCheck.Valid;
}

/// <summary>
/// Creates and checks license keys. A key looks like  ERG1-&lt;payload&gt;.&lt;signature&gt;  where the payload is the
/// license data (JSON) and the signature is an ECDSA P-256 signature of it made with the private key. The program
/// only holds the public key, so it can verify keys but nobody can create one from it.
/// </summary>
public static class LicenseCodec
{
    private const string Prefix = "ERG1-";

    // -- The supplier side (LicenseTool) -------------------------------------------------------

    public static (string PrivateKeyBase64, string PublicKeyBase64) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
                Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>The public key that belongs to a private key (what goes into LicenseKeys.cs).</summary>
    public static string GetPublicKey(string privateKeyBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public static string Issue(LicensePayload payload, string privateKeyBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);

        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = ecdsa.SignData(json, HashAlgorithmName.SHA256);
        return Prefix + ToBase64Url(json) + "." + ToBase64Url(signature);
    }

    // -- The program side ------------------------------------------------------------------------

    /// <summary>Removes spaces and line breaks (a pasted key is often wrapped).</summary>
    public static string Clean(string? key) => new((key ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

    public static LicenseResult Evaluate(string? key, string publicKeyBase64, string machineCode, DateOnly today)
    {
        var clean = Clean(key);
        if (clean.Length == 0) return new LicenseResult(LicenseCheck.Missing, null);
        if (!clean.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return new LicenseResult(LicenseCheck.InvalidFormat, null);

        var parts = clean[Prefix.Length..].Split('.');
        if (parts.Length != 2) return new LicenseResult(LicenseCheck.InvalidFormat, null);

        byte[] json, signature;
        try
        {
            json = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return new LicenseResult(LicenseCheck.InvalidFormat, null);
        }

        using var ecdsa = ECDsa.Create();
        try { ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _); }
        catch (Exception) { return new LicenseResult(LicenseCheck.NotConfigured, null); }

        bool signed;
        try { signed = ecdsa.VerifyData(json, signature, HashAlgorithmName.SHA256); }
        catch (CryptographicException) { signed = false; }
        if (!signed) return new LicenseResult(LicenseCheck.BadSignature, null);

        LicensePayload? payload;
        try { payload = JsonSerializer.Deserialize<LicensePayload>(json); }
        catch (JsonException) { return new LicenseResult(LicenseCheck.InvalidFormat, null); }
        if (payload == null) return new LicenseResult(LicenseCheck.InvalidFormat, null);

        // "*" = a license that is not tied to one PC (issued on purpose, e.g. for testing).
        if (payload.MachineCode != "*" &&
            MachineCode.Normalize(payload.MachineCode) != MachineCode.Normalize(machineCode))
            return new LicenseResult(LicenseCheck.WrongMachine, payload);

        if (payload.ExpiresDate is { } expires && today > expires)
            return new LicenseResult(LicenseCheck.Expired, payload);

        return new LicenseResult(LicenseCheck.Valid, payload);
    }

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
