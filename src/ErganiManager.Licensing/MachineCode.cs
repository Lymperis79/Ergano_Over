using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ErganiManager.Licensing;

/// <summary>
/// The code that identifies this PC (shown on the activation window and sent to the supplier). It is made from
/// the Windows installation id and the processor model, so copying the program (or even the whole disk) to a
/// different PC gives a different code and the license no longer fits.
/// </summary>
public static class MachineCode
{
    private static string? _cached;

    /// <summary>20 hex digits grouped like XXXX-XXXX-XXXX-XXXX-XXXX.</summary>
    public static string Get() => _cached ??= Compute();

    /// <summary>Upper case, no dashes or spaces - used to compare two codes.</summary>
    public static string Normalize(string? code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string Compute()
    {
        var material = $"{MachineId()}|{ProcessorName()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ErganiManager|" + material));
        var hex = Convert.ToHexString(hash, 0, 10);                       // 20 hex digits
        return string.Join("-", Enumerable.Range(0, 5).Select(i => hex.Substring(i * 4, 4)));
    }

    private static string MachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (key?.GetValue("MachineGuid") is string guid && guid.Length > 0) return guid;
            }
            else
            {
                foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                    if (File.Exists(path)) return File.ReadAllText(path).Trim();
            }
        }
        catch { /* fall through to the machine name */ }

        return Environment.MachineName;
    }

    private static string ProcessorName()
    {
        try
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/cpuinfo"))
            {
                var line = File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
                if (line != null) return line.Split(':').Last().Trim();
            }
        }
        catch { }
        return RuntimeInformation.ProcessArchitecture.ToString();
    }
}
