using ErganiManager.Core.Interfaces;

namespace ErganiManager.Core.Services;

public class BCryptPasswordHasher : IPasswordHasher
{
    // Work factor 12 is a reasonable default for a desktop app in 2026 —
    // strong enough to resist offline attacks on a stolen DB file, fast
    // enough not to annoy users logging in.
    private const int WorkFactor = 12;

    public string Hash(string plainTextPassword)
    {
        if (string.IsNullOrEmpty(plainTextPassword))
            throw new ArgumentException("Password cannot be empty.", nameof(plainTextPassword));

        return BCrypt.Net.BCrypt.HashPassword(plainTextPassword, workFactor: WorkFactor);
    }

    public bool Verify(string plainTextPassword, string hash)
    {
        if (string.IsNullOrEmpty(plainTextPassword) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(plainTextPassword, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash is malformed/corrupted — treat as verification failure, not a crash.
            return false;
        }
    }
}
