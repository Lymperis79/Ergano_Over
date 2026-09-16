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
    string Unprotect(string protectedText);
}
