namespace ErganiManager.Licensing;

/// <summary>
/// The public half of YOUR license key pair. It is safe to ship: it can only CHECK licenses, never create them.
///
/// Setup (once):
///   1. Run   ErganiManager.LicenseTool genkey   - it writes license-private.key (keep it secret, never ship it)
///      and prints the public key.
///   2. Paste the printed public key between the quotes below and rebuild the application.
///
/// While the value is empty the license check is OFF (every module works, nothing is asked). As soon as a key
/// is pasted here, the application asks for a license on every PC and only accepts keys you signed.
/// </summary>
public static class LicenseKeys
{
    //public const string PublicKeyBase64 = "";
    public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEpIxMdxFQxnAp05RnSUvz3QQLGM/tBN59Vaodo90akm3+JJSqV3108cCDq06rMELHSV4auuUqer0w/DhaMgyKkA==";

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKeyBase64);
}
