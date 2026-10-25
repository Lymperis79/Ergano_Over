namespace ErganiManager.Core.Interfaces;

/// <summary>
/// Abstraction for encrypting/decrypting secrets (Ergani passwords, SMTP
/// passwords) before they are stored in the database. The interface lives in
/// Core so services like CompanyService can depend on it without Core taking
/// a dependency on ErganiManager.ErganiApi. The concrete implementation
/// (DPAPI on Windows, AES+keyfile on Linux) is registered by the host app
/// from ErganiManager.ErganiApi's ServiceCollectionExtensions.
/// </summary>
public interface ICredentialProtector
{
    string Protect(string plainText);

    /// <exception cref="CredentialUnreadableException">The text cannot be decrypted on this PC.</exception>
    string Unprotect(string protectedText);

    /// <summary>True for values saved in the current format, which every PC of the database can read.</summary>
    bool IsPortable(string? protectedText);
}

/// <summary>A saved password cannot be decrypted here (it was saved by an older version on another PC).</summary>
public class CredentialUnreadableException : Exception
{
    public CredentialUnreadableException(string message, Exception? inner = null) : base(message, inner) { }
}
